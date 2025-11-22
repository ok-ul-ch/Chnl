namespace Chnl;

internal struct Read;
internal struct Write;

/// A container for blocking channel operations
internal class Blocked<T>
{
    /// IMPORTANT: All methods must be called ONLY by the <see cref="Blocked{T}"/> as it handles all its thread-safety invariants
    internal class Operation : IDisposable
    {
        private readonly ManualResetEventSlim _resetEvent = new(false);

        public void Block()
        {
            _resetEvent.Wait();
        }

        public void Unblock()
        {
            _resetEvent.Set();
        }

        public void Dispose()
        {
            _resetEvent.Dispose();
        }
    }

    private readonly Lock _lock = new();

    // TODO: Model LinkedList/Resizable ring buffer/Queue usage and benchmark which one is better
    private readonly List<Operation> _waitOperations = [];

    // NOTE: Volatile is not sufficient for this field as we need sequential consistency
    private int _isEmpty = 1;
    private bool _isClosed = false;

    internal bool IsClosed
    {
        get
        {
            // This is a test-only property, but still better to guard it with lock
            lock (_lock)
            {
                return _isClosed;
            }
        }
    }

    public bool IsEmpty
    {
        get => Interlocked.CompareExchange(ref _isEmpty, 0, 0) == 1;
        private set => Interlocked.Exchange(ref _isEmpty, value ? 1 : 0);
    }

    public void Block(Operation op)
    {
        using (op)
        {
            op.Block();
        }
    }

    public void Cancel(Operation op)
    {
        using (op)
        {
            lock (_lock)
            {
                _waitOperations.Remove(op);
                IsEmpty = _waitOperations.Count == 0;
            }
        }
    }

    /// Attempts to register a new block operation. Returns false if the waiting channel is closed
    ///
    /// The caller must double-check his "wait" predicate and call <see cref="Cancel"/> method if it is not relevant anymore (i.e. the caller can proceed without blocking as requested operation has become available just now) 
    public bool TryRegister(out Operation? op)
    {
        lock (_lock)
        {
            if (_isClosed)
            {
                op = null;
                return false;
            }

            op = new Operation();
            _waitOperations.Add(op);
            IsEmpty = false;
            return true;
        }
    }


    /// Finds the next available Operation and Unblocks it
    public void UnblockNext()
    {
        if (IsEmpty)
        {
            // No blocked operations available.
            return;
        }

        lock (_lock)
        {
            if (IsEmpty)
            {
                // We lost the race and someone else has unblocked all remaining operations 
                return;
            }

            var op = _waitOperations[0];
            _waitOperations.RemoveAt(0);
            op.Unblock();

            IsEmpty = _waitOperations.Count == 0;
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            foreach (var op in _waitOperations)
            {
                op.Unblock();
            }

            _waitOperations.Clear();
            IsEmpty = true;
            _isClosed = true;
        }
    }
}