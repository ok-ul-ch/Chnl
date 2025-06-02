using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Chnl;

[StructLayout(LayoutKind.Explicit)]
internal record struct Stamp
{
    [FieldOffset(0)] public uint Lap;
    [FieldOffset(4)] public int BufferIndex;

    public Stamp(uint lap, int bufferIndex)
    {
        Lap = lap;
        BufferIndex = bufferIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ulong ToInt() => Unsafe.BitCast<Stamp, ulong>(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Stamp From(ulong value) => Unsafe.BitCast<ulong, Stamp>(value);

    public static readonly Stamp Closed = new(0, 0);
    public static readonly Stamp TailStart = new(2, 0);
    public static readonly Stamp HeadStart = new(3, 0);
}

/// MPMC Bounded channel built on top of the preallocated ring buffer
///
/// Implementation is based on the following resources:
/// * https://web.archive.org/web/20240213171458/http://www.1024cores.net/home/lock-free-algorithms/queues/bounded-mpmc-queue
/// * https://github.com/crossbeam-rs/crossbeam/blob/master/crossbeam-channel/src/flavors/array.rs
/// * https://docs.google.com/document/d/1yIAYmbvL3JxOKOjuCyon7JhW4cSv1wy5hC0ApeGMV9s/pub
public class BoundedChannel<T>
{
    private struct Slot
    {
        private ulong _nextActionLap;

        public Slot(ulong nextActionLap)
        {
            _nextActionLap = nextActionLap;
        }

        /// Defines the next Lap at which we can Write OR Read from that slot.
        ///
        /// Writes are even, Reads are odd
        ///
        /// We can write/read this slot ONLY if the Channel's tail/head's Lap is equal to <see cref="NextActionLap"/> 
        public uint NextActionLap
        {
            get => (uint)Interlocked.Read(ref _nextActionLap);
            set => Interlocked.Exchange(ref _nextActionLap, value);
        }

        public T? Value;
    }

    /// Current read pointer. Head of the channel's queue
    private ulong _head;

    /// Current write pointer. The queue's tail
    private ulong _tail;

    /// Underlying ring buffer to store transmitted values in   
    private readonly Slot[] _buffer;

    private readonly Blocked<Reads> _blockedReads;
    private readonly Blocked<Writes> _blockedWrites;

    public int Capacity => _buffer.Length;

    public BoundedChannel(uint capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _tail = Stamp.TailStart.ToInt();
        _head = Stamp.HeadStart.ToInt();
        _buffer = new Slot[capacity];
        _blockedReads = new();
        _blockedWrites = new();

        for (var i = 0; i < _buffer.Length; i++)
        {
            _buffer[i] = new Slot(Stamp.TailStart.Lap);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Stamp ReadHead() => Stamp.From(Interlocked.Read(ref _head));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Stamp WriteTail(Stamp newTail) => Stamp.From(Interlocked.Exchange(ref _tail, newTail.ToInt()));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Stamp ReadTail() => Stamp.From(Interlocked.Read(ref _tail));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Stamp CompareExchangeHead(Stamp newHead, Stamp oldHead) =>
        Stamp.From(Interlocked.CompareExchange(ref _head, newHead.ToInt(), oldHead.ToInt()));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Stamp CompareExchangeTail(Stamp newTail, Stamp oldTail) =>
        Stamp.From(Interlocked.CompareExchange(ref _tail, newTail.ToInt(), oldTail.ToInt()));

    public int Length()
    {
        while (true)
        {
            // Load tail and head
            var tail = ReadTail();
            var head = ReadHead();

            // Load tail again.
            // If it hasn't changed while we were loading the head - we got the consistent view on state
            if (ReadTail() != tail)
            {
                continue;
            }

            var headIndex = head.BufferIndex;
            var tailIndex = tail.BufferIndex;

            if (headIndex < tailIndex)
            {
                return tailIndex - headIndex;
            }

            if (headIndex > tailIndex)
            {
                return Capacity - headIndex + tailIndex;
            }

            return head.Lap > tail.Lap ? 0 : Capacity;
        }
    }

    public bool IsEmpty()
    {
        var tail = ReadTail();
        var head = ReadHead();

        return head.BufferIndex == tail.BufferIndex && head.Lap > tail.Lap;
    }

    public bool IsFull()
    {
        var tail = ReadTail();
        var head = ReadHead();

        return head.BufferIndex == tail.BufferIndex && head.Lap < tail.Lap;
    }

    public bool IsClosed()
    {
        return ReadTail() == Stamp.Closed;
    }


    public bool TryClose()
    {
        if (WriteTail(Stamp.Closed) == Stamp.Closed)
        {
            return false;
        }

        _blockedReads.Close();
        _blockedWrites.Close();
        return true;
    }

    public bool TryWrite(T? item)
    {
        // TODO: Find better way to return reference to slot as well as next lap to read
        ref var slot = ref AcquireWriteSlot(out var readAtLap);

        if (readAtLap == null)
        {
            return false;
        }

        WriteSlot(item, ref slot, readAtLap.Value);
        return true;
    }

    public WriteResult Write(T? item)
    {
        while (true)
        {
            // Try to write without blocking the calling thread
            if (TryWriteNonBlocking(item))
            {
                return WriteResult.Success();
            }

            if (!_blockedWrites.TryRegister(out var write))
            {
                // Register can fail only if the channel is closed
                return WriteResult.Closed();
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
                        return WriteResult.Closed();
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

            backoff.Wait();
        }
    }


    private ref Slot AcquireWriteSlot(out uint? readAtLap)
    {
        var backoff = new LoopBackoff();
        var tail = ReadTail();

        while (true)
        {
            ref var tailSlot = ref _buffer[tail.BufferIndex];

            if (tail == Stamp.Closed)
            {
                readAtLap = null;
                return ref tailSlot;
            }

            var writeAtLap = tailSlot.NextActionLap;

            // There is a slot available for writing (not used by in-progress read)
            if (tail.Lap == writeAtLap)
            {
                // Prepare position for next write
                var nextTail = tail.BufferIndex + 1 < Capacity
                    ? tail with { BufferIndex = tail.BufferIndex + 1 }
                    : new Stamp(tail.Lap + 2, 0);

                // Acquire the Write slot
                var exchangedTail = CompareExchangeTail(nextTail, tail);

                if (exchangedTail == tail)
                {
                    readAtLap = tail.Lap + 1;
                    return ref tailSlot;
                }

                // Other thread has won the race
                tail = exchangedTail;

                // Spin backoff as most likely next tail is available
                backoff.SpinWait();
            }
            else if (tail.Lap - writeAtLap > 0)
            {
                // Channel is full as we have full lap of lag
                readAtLap = null;
                return ref tailSlot;
            }
            else
            {
                // There is an unfinished read thus we wait
                backoff.Wait();
                tail = ReadTail();
            }
        }
    }


    public bool TryRead(out T? item)
    {
        ref var slot = ref AcquireReadSlot(out var writeAtLap);
        if (writeAtLap == null)
        {
            item = default;
            return false;
        }

        item = ReadSlot(ref slot, writeAtLap.Value);
        return true;
    }

    public ReadResult<T> Read()
    {
        while (true)
        {
            // Try to write without blocking the calling thread
            if (TryReadNonBlocking(out var item))
            {
                return ReadResult<T>.Success(item);
            }

            if (!_blockedReads.TryRegister(out var read))
            {
                // Register can fail only if the channel is closed
                return ReadResult<T>.Closed(item);
            }

            using (read)
            {
                var isClosed = IsClosed();
                if (!isClosed && IsEmpty())
                {
                    // Fall back to the blocking wait as most likely the channel will remain empty for longer time
                    read!.Wait();
                }
                else
                {
                    // Meanwhile, some writer has written to the channel. We shall not wait and try to perform the read again
                    _blockedReads.Unregister(read!);

                    if (isClosed)
                    {
                        return ReadResult<T>.Closed(default);
                    }
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

            backoff.Wait();
        }
    }


    private ref Slot AcquireReadSlot(out uint? writeAtLap)
    {
        var backoff = new LoopBackoff();
        var head = ReadHead();

        while (true)
        {
            ref var headSlot = ref _buffer[head.BufferIndex];
            var nextReadLap = headSlot.NextActionLap;

            // There is something to read from this slot
            if (head.Lap == nextReadLap)
            {
                // Prepare position for next read
                var nextHead = head.BufferIndex + 1 < Capacity
                    ? head with { BufferIndex = head.BufferIndex + 1 }
                    : new Stamp(head.Lap + 2, 0);

                // Acquire the Read slot
                var exchangedHead = CompareExchangeHead(nextHead, head);

                if (exchangedHead == head)
                {
                    writeAtLap = head.Lap + 1;
                    return ref headSlot;
                }

                // Other thread has won the race
                head = exchangedHead;

                // Spin backoff as most likely next head is available
                backoff.SpinWait();
            }
            else if (head.Lap - nextReadLap > 0)
            {
                // Channel is empty
                writeAtLap = null;
                return ref headSlot;
            }
            else
            {
                // The slot is not ready to be read from. We wait until concurrent write completes
                backoff.Wait();
                head = ReadHead();
            }
        }
    }

    private void WriteSlot(T? item, ref Slot slot, uint readAtLap)
    {
        // We hold the exclusive access to the slot, thus we can write directly 
        slot.Value = item;
        slot.NextActionLap = readAtLap;

        // Notify next blocked reader
        _blockedReads.UnblockNext();
    }

    private T? ReadSlot(ref Slot slot, uint writeAtLap)
    {

        // We hold the exclusive access to the slot, thus we can read/write directly
        slot.NextActionLap = writeAtLap;
        
        var value = slot.Value;
        slot.Value = default;
        
        // Notify next blocked writer
        _blockedWrites.UnblockNext();

        return value;
    }
}