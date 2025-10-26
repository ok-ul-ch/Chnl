namespace Chnl.Tests;

#pragma warning disable CS0618 // Type or member is obsolete
[TestFixture, Timeout(5000)]
#pragma warning restore CS0618 // Type or member is obsolete
public class WaitToTests
{
    [Test]
    public void Wait_ThenUnblock_Released()
    {
        var wait = new WaitTo<bool>();
        var waitCompleted = new ManualResetEventSlim(false);
        var blockedThread = new Thread(() =>
        {
            wait.Wait();
            waitCompleted.Set();
        });

        blockedThread.Start();

        // Delay to ensure that Wait is blocking indeed
        Thread.Sleep(100);

        Assert.That(blockedThread.IsAlive);
        Assert.That(!waitCompleted.IsSet);

        wait.Unblock();

        blockedThread.Join();
        Assert.That(!blockedThread.IsAlive);
        Assert.That(waitCompleted.IsSet);
    }


    [Test]
    public void Unblock_ThenWait_ImmediatelyReleased()
    {
        var wait = new WaitTo<bool>();
        wait.Unblock();
        wait.Wait();
    }
}