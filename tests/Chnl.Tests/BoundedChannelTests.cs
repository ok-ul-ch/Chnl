using System.Collections.Concurrent;

namespace Chnl.Tests;

#pragma warning disable CS0618 // Type or member is obsolete
[TestFixture, Timeout(5000)]
#pragma warning restore CS0618 // Type or member is obsolete
public class BoundedChannelTests
{
    private BoundedChannel<int> _channel;
    private const int DefaultCapacity = 5;

    [SetUp]
    public void SetUp()
    {
        _channel = new BoundedChannel<int>(DefaultCapacity);
    }

    [Test]
    public void Ctor_ValidCapacity_CreatesChannel()
    {
        var channel = new BoundedChannel<int>(10);
        Assert.That(channel.Capacity, Is.EqualTo(10));
    }

    [Test]
    public void Ctor_ZeroCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new BoundedChannel<int>(0); });
    }

    [Test]
    public void Length_EmptyChannel_Zero()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_channel.Length(), Is.EqualTo(0));
            Assert.That(_channel.IsEmpty(), Is.True);
            Assert.That(_channel.IsFull(), Is.False);
        }
    }

    [Test]
    public void Length_PartiallyFilledChannel_CorrectCount()
    {
        for (var i = 0; i < _channel.Capacity; i++)
        {
            Assert.That(_channel.TryWrite(i), Is.True);
            Assert.That(_channel.Length(), Is.EqualTo(i + 1));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_channel.Length(), Is.EqualTo(_channel.Capacity));
            Assert.That(_channel.IsEmpty(), Is.False);
            Assert.That(_channel.IsFull(), Is.True);
        }

        // Edge case when write/read indices require Capacity to compute Length
        Assert.That(_channel.TryRead(out _), Is.True);
        Assert.That(_channel.Length(), Is.EqualTo(_channel.Capacity - 1));
    }

    [Test]
    public void Length_FullChannel_Capacity()
    {
        for (var i = 0; i < DefaultCapacity; i++)
        {
            Assert.That(_channel.TryWrite(i), Is.True);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_channel.Length(), Is.EqualTo(_channel.Capacity));
            Assert.That(_channel.IsEmpty(), Is.False);
            Assert.That(_channel.IsFull(), Is.True);
        }
    }

    [Test]
    public void Length_AfterReadingItems_CorrectCount()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.That(_channel.TryWrite(i), Is.True);
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.That(_channel.TryRead(out _), Is.True);
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.That(_channel.TryWrite(i), Is.True);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_channel.Length(), Is.EqualTo(5));
            Assert.That(_channel.IsEmpty(), Is.False);
            Assert.That(_channel.IsFull(), Is.True);
        }
    }

    [Test]
    public void TryWrite_FullChannel_False()
    {
        for (var i = 0; i < DefaultCapacity; i++)
        {
            Assert.That(_channel.TryWrite(i), Is.True);
        }

        Assert.That(_channel.TryWrite(42), Is.False);
    }

    [Test]
    public void TryRead_EmptyChannel_False()
    {
        Assert.That(_channel.TryRead(out _), Is.False);
    }


    [Test]
    public void TryWrite_TryRead_MultipleLaps_WorksCorrectly()
    {
        var totalOperations = DefaultCapacity * 3; // Three 'Laps'

        for (var i = 0; i < totalOperations; i++)
        {
            Assert.That(_channel.TryWrite(i), Is.True);
            Assert.That(_channel.TryRead(out var item), Is.True);

            // Verify FIFO order as well
            Assert.That(item, Is.EqualTo(i));
        }
    }

    [Test]
    public void Write_EmptyChannel_Success()
    {
        var result = _channel.Write(42);
        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Read_NonEmptyChannel_Success()
    {
        _channel.TryWrite(42);
        var result = _channel.Read();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Unwrap(), Is.EqualTo(42));
    }


    [Test]
    public void Write_FullChannel_BlocksUntilSpaceAvailable()
    {
        for (var i = 0; i < DefaultCapacity; i++)
        {
            _channel.TryWrite(i);
        }

        Assert.That(_channel.TryWrite(42), Is.False);

        var writeCompleted = new ManualResetEventSlim();
        var writerThread = new Thread(() =>
        {
            _channel.Write(42);
            writeCompleted.Set();
        });

        writerThread.Start();
        Thread.Sleep(20); // Give time for writer thread to block

        Assert.That(writeCompleted.IsSet, Is.False);

        // Free up the single slot in channel
        _channel.TryRead(out _);

        // Wait for writer thread to complete
        writerThread.Join();
        Assert.That(writeCompleted.IsSet, Is.True);

        for (var i = 0; i < DefaultCapacity - 1; i++)
        {
            _channel.TryRead(out _);
        }

        // Ensure the value from "blocked" writer is read 
        _channel.TryRead(out var item);
        Assert.That(item, Is.EqualTo(42));
    }

    [Test]
    public void Read_EmptyChannel_BlocksUntilItemAvailable()
    {
        var readCompleted = new ManualResetEventSlim(false);
        var readValue = 0;

        var readerThread = new Thread(() =>
        {
            var result = _channel.Read();
            readValue = result.Unwrap();
            readCompleted.Set();
        });

        readerThread.Start();
        Thread.Sleep(20); // Give time for reader thread to block

        Assert.That(readCompleted.IsSet, Is.False);

        _channel.TryWrite(42);

        readerThread.Join();
        Assert.That(readCompleted.IsSet, Is.True);
        Assert.That(readValue, Is.EqualTo(42));
    }

    [Test]
    public void ReadWrite_MultipleThreads_AllMessagesDelivered()
    {
        var channel = new BoundedChannel<string>(DefaultCapacity);
        var writtenValues = new ConcurrentBag<string>();
        var readValues = new ConcurrentBag<string>();

        // Write `Capacity * 2 * 3` messages concurrently
        var writers = Enumerable.Range(0, 8).Select(t =>
            {
                var thread = new Thread(() =>
                {
                    for (var i = 0; i < DefaultCapacity * 2; i++)
                    {
                        var value = (t << 16 | i).ToString();

                        writtenValues.Add(value);
                        var writeResult = channel.Write(value);
                        writeResult.EnsureSuccess();

                        Thread.Yield();
                    }
                });
                thread.Start();
                return thread;
            })
            .ToList();

        // Read all messages concurrently
        var readers =
            Enumerable.Range(0, 8).Select(_ =>
                {
                    var thread = new Thread(() =>
                    {
                        for (var i = 0; i < DefaultCapacity * 2; i++)
                        {
                            var result = channel.Read();
                            result.EnsureSuccess();
                            readValues.Add(result.Unwrap()!);
                            Thread.Yield();
                        }
                    });
                    thread.Start();
                    return thread;
                })
                .ToList();

        foreach (var thread in writers.Concat(readers))
        {
            thread.Join();
        }

        // Verify all values written were read
        Assert.That(readValues.Count, Is.EqualTo(writtenValues.Count));

        // Since we can't guarantee exact order with multiple threads, sort both values before comparison compare
        var read = readValues.OrderBy(i => i).ToArray();
        var written = writtenValues.OrderBy(i => i).ToArray();

        for (var i = 0; i < written.Length; i++)
        {
            Assert.That(ReferenceEquals(written[i], read[i]), Is.True);
        }
    }

    [Test]
    public void TryClose_NotClosed_True()
    {
        Assert.That(_channel.TryClose(), Is.True);
        Assert.That(_channel.IsClosed(), Is.True);
    }

    [Test]
    public void TryClose_AlreadyClosed_False()
    {
        _channel.TryClose();
        Assert.That(_channel.TryClose(), Is.False);
    }

    [Test]
    public void TryWrite_ClosedChannel_False()
    {
        _channel.TryClose();
        Assert.That(_channel.TryWrite(42), Is.False);
    }

    [Test]
    public void Write_ClosedChannel_ClosedResult()
    {
        _channel.TryClose();
        var result = _channel.Write(42);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(Status.Closed));
    }

    [Test]
    public void Read_ClosedEmptyChannel_ClosedResult()
    {
        _channel.TryClose();
        var result = _channel.Read();

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Status, Is.EqualTo(Status.Closed));
    }

    [Test]
    public void Read_AfterClose_DeliversAllMessages()
    {
        for (var i = 0; i < DefaultCapacity; i++)
        {
            _channel.TryWrite(i);
        }

        _channel.TryClose();

        for (var i = 0; i < DefaultCapacity; i++)
        {
            var result = _channel.Read();
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Unwrap(), Is.EqualTo(i));
        }
    }


    [Test]
    public void Close_UnblocksBlockedWriters()
    {
        for (var i = 0; i < DefaultCapacity; i++)
        {
            _channel.TryWrite(42);
        }

        var writerResults = new ConcurrentBag<Result<Void>>();
        var writersCompletedEvents = new List<ManualResetEventSlim>();

        var writerThreads = Enumerable.Range(0, 8).Select(i =>
        {
            var completed = new ManualResetEventSlim(false);
            writersCompletedEvents.Add(completed);

            var thread = new Thread(() =>
            {
                var result = _channel.Write(i);
                writerResults.Add(result);
                completed.Set();
            });

            thread.Start();

            return thread;
        }).ToList();


        Thread.Sleep(20); // Give writers time to block

        foreach (var evt in writersCompletedEvents)
        {
            Assert.That(evt.IsSet, Is.False);
        }

        _channel.TryClose();

        foreach (var thread in writerThreads)
        {
            thread.Join();
        }

        foreach (var evt in writersCompletedEvents)
        {
            Assert.That(evt.IsSet, Is.True);
        }

        Assert.That(writerResults.Count, Is.EqualTo(8));
        Assert.That(writerResults.All(r => r.Status == Status.Closed), Is.True);
    }


    [Test]
    public void Close_UnblocksBlockedReaders()
    {
        var readerResults = new ConcurrentBag<Result<int>>();
        var readersCompletedEvents = new List<ManualResetEventSlim>();
        var readerThreads = Enumerable.Range(0, 8).Select(_ =>
        {
            var completed = new ManualResetEventSlim(false);
            readersCompletedEvents.Add(completed);
            var thread = new Thread(() =>
            {
                var result = _channel.Read();
                readerResults.Add(result);
                completed.Set();
            });

            thread.Start();
            return thread;
        }).ToList();

        Thread.Sleep(20); // Give readers time to block

        foreach (var evt in readersCompletedEvents)
        {
            Assert.That(evt.IsSet, Is.False);
        }

        _channel.TryClose();

        foreach (var thread in readerThreads)
        {
            thread.Join();
        }

        foreach (var evt in readersCompletedEvents)
        {
            Assert.That(evt.IsSet, Is.True);
        }

        // Verify all readers got closed status
        Assert.That(readerResults.Count, Is.EqualTo(8));
        Assert.That(readerResults.All(r => r.Status == Status.Closed), Is.True);
    }

    [Test]
    public void AllOperations_HeadWraps_Success()
    {
        var channel = new BoundedChannel<int>(DefaultCapacity, Position.MaxLap, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.Length(), Is.EqualTo(0));
            Assert.That(channel.IsEmpty(), Is.True);
            Assert.That(channel.IsFull(), Is.False);
        }

        for (var i = 1; i <= DefaultCapacity; i++)
        {
            Assert.That(channel.TryWrite(i), Is.True);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.Length(), Is.EqualTo(DefaultCapacity));
            Assert.That(channel.IsEmpty(), Is.False);
            Assert.That(channel.IsFull(), Is.True);
        }

        Assert.That(channel.TryWrite(99), Is.False);

        for (var i = 1; i <= DefaultCapacity; i++)
        {
            Assert.That(channel.TryRead(out var item), Is.True);
            Assert.That(item, Is.EqualTo(i));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.Length(), Is.EqualTo(0));
            Assert.That(channel.IsEmpty(), Is.True);
            Assert.That(channel.IsFull(), Is.False);
        }

        Assert.That(channel.TryRead(out _), Is.False);
    }


    [Test]
    public void AllOperations_TailWraps_Success()
    {
        var channel = new BoundedChannel<int>(DefaultCapacity, Position.MaxLap - 1, Position.MaxLap);

        // Fill the channel in full so the tail wraps to 0
        for (var i = 1; i <= DefaultCapacity; i++)
        {
            Assert.That(channel.TryWrite(i), Is.True);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.Length(), Is.EqualTo(DefaultCapacity));
            Assert.That(channel.IsEmpty(), Is.False);
            Assert.That(channel.IsFull(), Is.True);
        }

        Assert.That(channel.TryWrite(99), Is.False);

        for (var i = 1; i <= DefaultCapacity; i++)
        {
            Assert.That(channel.TryRead(out var item), Is.True);
            Assert.That(item, Is.EqualTo(i));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel.Length(), Is.EqualTo(0));
            Assert.That(channel.IsEmpty(), Is.True);
            Assert.That(channel.IsFull(), Is.False);
        }

        Assert.That(channel.TryRead(out _), Is.False);
    }
}