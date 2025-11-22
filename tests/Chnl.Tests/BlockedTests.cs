namespace Chnl.Tests;

#pragma warning disable CS0618 // Type or member is obsolete
[TestFixture, Timeout(5000)]
#pragma warning restore CS0618 // Type or member is obsolete
public class BlockedTests
{
    private Blocked<Write> _blocked;

    private static Thread SpawnWaitThread(Blocked<Write>.Operation op)
    {
        var thread = new Thread(op.Block);
        thread.Start();
        return thread;
    }

    [SetUp]
    public void SetUp()
    {
        _blocked = new Blocked<Write>();
    }

    [Test]
    public void TryRegister_NonClosed_True()
    {
        var registerResult = _blocked.TryRegister(out var wait);

        Assert.That(registerResult, Is.True);
        Assert.That(wait, Is.Not.Null);
    }

    [Test]
    public void TryRegister_Closed_False()
    {
        _blocked.Close();

        var registerResult = _blocked.TryRegister(out var wait);
        Assert.That(registerResult, Is.False);
        Assert.That(wait, Is.Null);
    }

    [Test]
    public void Unregister_ItemExists_Removes()
    {
        Assert.That(_blocked.IsEmpty, Is.True);

        _blocked.TryRegister(out var wait);
        Assert.That(_blocked.IsEmpty, Is.False);

        _blocked.Cancel(wait!);
        Assert.That(_blocked.IsEmpty, Is.True);
    }

    [Test]
    public void Unregister_UnknownItem_Ignored()
    {
        Assert.DoesNotThrow(() => _blocked.Cancel(new Blocked<Write>.Operation()));
    }

    [Test]
    public void UnblockNext_Empty_NoAction()
    {
        Assert.That(_blocked.IsEmpty, Is.True);
        Assert.DoesNotThrow(() => _blocked.UnblockNext());
    }

    [Test]
    public void UnblockNext_MultipleItems_UnblocksFIFO()
    {
        var waitOps = Enumerable.Range(0, 10).Select(_ =>
        {
            _blocked.TryRegister(out var wait);
            return SpawnWaitThread(wait!);
        }).ToArray();

        foreach (var i in Enumerable.Range(0, 10))
        {
            if (i > 0)
            {
                Assert.That(waitOps.Take(i).All(t => t.IsAlive), Is.False);
            }

            Assert.That(waitOps.Skip(i).All(t => t.IsAlive), Is.True);
            _blocked.UnblockNext();

            // If the unblock happens in a wrong order - Join will fail and the the task will fail with timeout 
            waitOps[i].Join();
        }
    }


    [Test]
    public void Close_AlreadyClosed_NoAction()
    {
        _blocked.Close();
        Assert.DoesNotThrow(() => _blocked.Close());

        Assert.That(_blocked.IsEmpty, Is.True);
        Assert.That(_blocked.IsClosed, Is.True);
    }

    [Test]
    public void Close_MultipleBlockedThreads_UnblocksAll()
    {
        var waitOps = Enumerable.Range(0, 10).Select(_ =>
        {
            _blocked.TryRegister(out var wait);
            return SpawnWaitThread(wait!);
        }).ToArray();

        foreach (var thread in waitOps)
        {
            Assert.That(thread.IsAlive, Is.True);
        }

        _blocked.Close();

        foreach (var thread in waitOps)
        {
            thread.Join();
            Assert.That(thread.IsAlive, Is.False);
        }
    }
}