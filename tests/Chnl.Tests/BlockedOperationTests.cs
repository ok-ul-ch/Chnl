namespace Chnl.Tests;

#pragma warning disable CS0618 // Type or member is obsolete
[TestFixture, Timeout(5000)]
#pragma warning restore CS0618 // Type or member is obsolete
public class BlockedOperationTests
{
    [Test]
    public void Block_ThenUnblock_Released()
    {
        var op = new Blocked<bool>.Operation();
        var waitCompleted = new ManualResetEventSlim(false);
        var blockedThread = new Thread(() =>
        {
            op.Block();
            waitCompleted.Set();
        });

        blockedThread.Start();

        // Delay to ensure that Wait is blocking indeed
        Thread.Sleep(100);

        Assert.That(blockedThread.IsAlive);
        Assert.That(!waitCompleted.IsSet);

        op.Unblock();

        blockedThread.Join();
        Assert.That(!blockedThread.IsAlive);
        Assert.That(waitCompleted.IsSet);
    }


    [Test]
    public void Unblock_ThenBlock_ImmediatelyReleased()
    {
        var op = new Blocked<bool>.Operation();
        op.Unblock();
        op.Block();
    }
}