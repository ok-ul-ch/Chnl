namespace Chnl;

internal struct Reads;
internal struct Writes;

/// A container for blocking channel operations
internal class Blocked<T>
{
    private readonly Lock _lock = new();
    private volatile bool _isEmpty = true;

    /// TODO: Model scenarios where LinkedList usage is cheaper and benchmark them
    /// TODO: Reuse instances of <see cref="WaitTo{T}"/>. So that there is no need to allocate them for each <see cref="TryRegister"/> call
    private readonly List<WaitTo<T>> _waitOperations = [];

    public bool IsEmpty => _isEmpty;
    public bool IsClosed { get; private set; }

    /// Attempts to register a new wait operation. Returns false if the waiting channel is closed
    ///
    /// The caller must double-check his "wait" predicate and call <see cref="Unregister"/> method if it is not relevant anymore (i.e. the caller can proceed without blocking as requested operation has become available just now) 
    public bool TryRegister(out WaitTo<T>? waitTo)
    {
        lock (_lock)
        {
            if (IsClosed)
            {
                waitTo = null;
                return false;
            }

            waitTo = new WaitTo<T>();
            _waitOperations.Add(waitTo);
            _isEmpty = false;
            return true;
        }
    }

    /// Removes an existing wait operation
    public void Unregister(WaitTo<T> waitTo)
    {
        lock (_lock)
        {
            _waitOperations.Remove(waitTo);
            _isEmpty = _waitOperations.Count == 0;
        }
    }

    /// Finds the next available <see cref="WaitTo{T}"/> and unblocks it
    public void UnblockNext()
    {
        if (_isEmpty)
        {
            // No blocked operations available.
            return;
        }

        lock (_lock)
        {
            if (_isEmpty)
            {
                // We lost the race and someone else has unblocked all remaining operations 
                return;
            }

            var waitTo = _waitOperations.FirstOrDefault();

            if (waitTo == null)
            {
                return;
            }

            waitTo.Unblock();
            _waitOperations.RemoveAt(0);
            _isEmpty = _waitOperations.Count == 0;
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            foreach (var wait in _waitOperations)
            {
                wait.Unblock();
            }

            _waitOperations.Clear();
            _isEmpty = true;
            IsClosed = true;
        }
    }
}