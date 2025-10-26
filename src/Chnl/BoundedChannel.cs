using System.Runtime.CompilerServices;

namespace Chnl;

/// MPMC Bounded channel built on top of the preallocated ring buffer
///
/// Implementation is based on the following resources:
/// * https://web.archive.org/web/20240213171458/http://www.1024cores.net/home/lock-free-algorithms/queues/bounded-mpmc-queue
/// * https://github.com/crossbeam-rs/crossbeam/blob/master/crossbeam-channel/src/flavors/array.rs
/// * https://docs.google.com/document/d/1yIAYmbvL3JxOKOjuCyon7JhW4cSv1wy5hC0ApeGMV9s/pub
public class BoundedChannel<T>
{
    private struct Slot(uint targetActionLap)
    {
        /// Defines the target Lap at which we can Write OR Read from that slot.
        ///
        /// Writes are even, Reads are odd
        ///
        /// We can write/read this slot ONLY if the Channel's tail/head's Lap is equal to <see cref="TargetActionLap"/> 
        public volatile uint TargetActionLap = targetActionLap;

        public T? Value = default;
    }

    /// Position of the next `Read` operation. Head of the channel's queue
    private long _head;

    /// Position of the next `Write` operation. The queue's tail
    private long _tail;

    /// Underlying ring buffer. Stores transmitted values
    private readonly Slot[] _buffer;

    private readonly Blocked<Read> _blockedReads;
    private readonly Blocked<Write> _blockedWrites;

    public int Capacity => _buffer.Length;

    public BoundedChannel(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _tail = Position.TailStart.ToInt();
        _head = Position.HeadStart.ToInt();
        _buffer = new Slot[capacity];
        _blockedReads = new();
        _blockedWrites = new();

        for (var i = 0; i < _buffer.Length; i++)
        {
            _buffer[i] = new Slot(Position.TailStart.Lap);
        }
    }

    internal BoundedChannel(int capacity, uint tailLap, uint headLap)
        : this(capacity)
    {
        _tail = new Position(tailLap, 0).ToInt();
        _head = new Position(headLap, 0).ToInt();

        for (var i = 0; i < _buffer.Length; i++)
        {
            _buffer[i] = new Slot(tailLap);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Position ReadHead() => Position.From(Interlocked.Read(ref _head));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Position WriteTail(Position newTail) => Position.From(Interlocked.Exchange(ref _tail, newTail.ToInt()));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Position ReadTail() => Position.From(Interlocked.Read(ref _tail));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Position CompareExchangeHead(Position newHead, Position oldHead) =>
        Position.From(Interlocked.CompareExchange(ref _head, newHead.ToInt(), oldHead.ToInt()));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Position CompareExchangeTail(Position newTail, Position oldTail) =>
        Position.From(Interlocked.CompareExchange(ref _tail, newTail.ToInt(), oldTail.ToInt()));

    public int Length()
    {
        while (true)
        {
            // Load tail and head
            var tail = ReadTail();
            var head = ReadHead();

            // Load tail again.
            // If it hasn't changed while we were loading the head - we got the consistent view on state, as there were no new writes
            if (ReadTail() != tail)
            {
                continue;
            }

            var headIndex = head.Index;
            var tailIndex = tail.Index;

            if (headIndex < tailIndex)
            {
                // Can happen only when we are in the same lap
                // Then the length is just - write index minus read index
                return tailIndex - headIndex;
            }

            if (headIndex > tailIndex)
            {
                // Read/Write in different laps. Thus compute the length with the help of Capacity
                return Capacity - headIndex + tailIndex;
            }

            // The channel is either empty or full
            return head.Lap == tail.WrapAddLap(1) ? 0 : Capacity;
        }
    }

    public bool IsEmpty()
    {
        var head = ReadHead();
        var tail = ReadTail();

        // The channel is empty when head is exactly one lap ahead
        return head.Index == tail.Index && head.Lap == tail.WrapAddLap(1);
    }

    public bool IsFull()
    {
        var head = ReadHead();
        var tail = ReadTail();

        // The channel is full when head is exactly one lap behind
        return head.Index == tail.Index && head.WrapAddLap(1) == tail.Lap;
    }

    public bool IsClosed()
    {
        return ReadTail().IsClosed;
    }

    public bool TryClose()
    {
        if (WriteTail(Position.Closed).IsClosed)
        {
            return false;
        }

        _blockedReads.Close();
        _blockedWrites.Close();
        return true;
    }

    public bool TryWrite(T? item)
    {
        var writeSlot = AcquireWriteSlot();
        if (!writeSlot.CanMutate)
        {
            return false;
        }

        WriteIntoSlot(item, writeSlot);
        return true;
    }

    public Result<Void> Write(T? item)
    {
        while (true)
        {
            // Try to write without blocking the calling thread
            if (TryWriteNonBlocking(item))
            {
                return Result<Void>.Success(new Void());
            }

            if (!_blockedWrites.TryRegister(out var write))
            {
                // Register can fail only if the channel is closed
                return Result<Void>.Closed();
            }

            using (write)
            {
                var isClosed = IsClosed();
                if (!isClosed && IsFull())
                {
                    // Fall back to the blocking wait as most likely reader(s) can't keep up with writer(s), thus we'll have to wait longer 
                    write!.Wait();
                }
                else
                {
                    // The channel has become available/closed just now, we shall try to write again without blocking
                    _blockedWrites.Unregister(write!);

                    if (isClosed)
                    {
                        return Result<Void>.Closed();
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteNonBlocking(T? item)
    {
        var backoff = new LoopBackoff();
        while (true)
        {
            if (TryWrite(item))
            {
                return true;
            }

            if (backoff.IsExhausted)
            {
                return false;
            }

            backoff.SpinOrYield();
        }
    }

    private readonly ref struct AcquiredSlot(ref Slot slot, uint? targetActionLap)
    {
        /// A reference to an element of inner `_buffer`. Used to mutate this element directly (either during read or write op) 
        public readonly ref Slot Slot = ref slot;

        public readonly uint? TargetActionLap = targetActionLap;
        public bool CanMutate => TargetActionLap.HasValue;
    }

    private AcquiredSlot AcquireWriteSlot()
    {
        var backoff = new LoopBackoff();
        var tail = ReadTail();

        while (true)
        {
            // IMPORTANT: Buffer Index can't grow above `Capacity - 1` or be negative for Bounded channel
            ref var tailSlot = ref _buffer[tail.Index];

            if (tail.IsClosed)
            {
                // TO-OPTIMIZE: Return the error explicitly, so that the caller can exit early
                return new AcquiredSlot(ref tailSlot, null);
            }

            var writeAtLap = tailSlot.TargetActionLap;

            // There is a slot available for writing (not used by in-progress read)
            if (tail.Lap == writeAtLap)
            {
                // Prepare position for next write
                var nextTail = tail.Index < Capacity - 1
                    ? tail.MoveNextIndex()
                    : tail.MoveNextLap();

                // Acquire the Write slot
                var exchangedTail = CompareExchangeTail(nextTail, tail);

                if (exchangedTail == tail)
                {
                    return new AcquiredSlot(ref tailSlot, tail.WrapAddLap(1));
                }

                // Other thread has won the race
                tail = exchangedTail;

                // Spin backoff as most likely next tail is available
                backoff.Spin();
            }
            else if (tail.Lap == Position.WrapAddLap(writeAtLap, 1))
            {
                // Channel is full as we have full lap of lag
                return new AcquiredSlot(ref tailSlot, null);
            }
            else
            {
                // There is an unfinished read thus we wait
                backoff.SpinOrYield();
                tail = ReadTail();
            }
        }
    }

    public bool TryRead(out T? item)
    {
        var readSlot = AcquireReadSlot();
        if (!readSlot.CanMutate)
        {
            item = default;
            return false;
        }

        item = ReadFromSlot(readSlot);
        return true;
    }

    public Result<T> Read()
    {
        while (true)
        {
            // Try to write without blocking the calling thread
            if (TryReadNonBlocking(out var item))
            {
                return Result<T>.Success(item);
            }

            if (!_blockedReads.TryRegister(out var read))
            {
                // Register can fail only if the channel is closed
                return Result<T>.Closed();
            }

            using (read)
            {
                if (IsEmpty())
                {
                    if (IsClosed())
                    {
                        return Result<T>.Closed();
                    }

                    // Fall back to the blocking wait as most likely the channel will remain empty for longer time
                    read!.Wait();
                }
                else
                {
                    // Meanwhile, some writer has written to the channel. We shall not wait and try to perform the read again
                    _blockedReads.Unregister(read!);
                }
            }
        }
    }

    private bool TryReadNonBlocking(out T? item)
    {
        var backoff = new LoopBackoff();
        while (true)
        {
            if (TryRead(out item))
            {
                return true;
            }

            if (backoff.IsExhausted)
            {
                return false;
            }

            backoff.SpinOrYield();
        }
    }


    private AcquiredSlot AcquireReadSlot()
    {
        var backoff = new LoopBackoff();
        var head = ReadHead();

        while (true)
        {
            // IMPORTANT: Buffer Index can't grow above `Capacity - 1` or be negative for Bounded channel
            ref var headSlot = ref _buffer[head.Index];
            var nextReadLap = headSlot.TargetActionLap;

            // There is something to read from this slot
            if (head.Lap == nextReadLap)
            {
                // Prepare position for next read
                var nextHead = head.Index < Capacity - 1
                    ? head.MoveNextIndex()
                    : head.MoveNextLap();

                // Acquire the Read slot
                var exchangedHead = CompareExchangeHead(nextHead, head);

                if (exchangedHead == head)
                {
                    return new AcquiredSlot(ref headSlot, Position.WrapAddLap(head.Lap, 1));
                }

                // Other thread has won the race
                head = exchangedHead;

                // Spin backoff as most likely next head is available
                backoff.Spin();
            }
            else if (head.Lap == Position.WrapAddLap(nextReadLap, 1))
            {
                // TO-OPTIMIZE: Check if the channel is closed an return the error explicitly, so that the caller can exit early

                // Channel is empty
                return new AcquiredSlot(ref headSlot, null);
            }
            else
            {
                // The slot is not ready to be read from. We wait until concurrent write completes
                backoff.SpinOrYield();
                head = ReadHead();
            }
        }
    }

    private void WriteIntoSlot(T? item, AcquiredSlot acquiredSlot)
    {
        // We hold the exclusive access to the given slot, thus we can read/write there directly
        ref var slot = ref acquiredSlot.Slot;
        slot.Value = item;
        slot.TargetActionLap = acquiredSlot.TargetActionLap!.Value;

        // Notify next blocked reader
        _blockedReads.UnblockNext();
    }

    private T? ReadFromSlot(AcquiredSlot acquiredSlot)
    {
        // We hold the exclusive access to the given slot, thus we can read/write there directly
        ref var slot = ref acquiredSlot.Slot;

        var value = slot.Value;
        slot.Value = default;

        slot.TargetActionLap = acquiredSlot.TargetActionLap!.Value;

        // Notify next blocked writer
        _blockedWrites.UnblockNext();

        return value;
    }
}