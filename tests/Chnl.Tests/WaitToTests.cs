namespace Chnl.Tests;

[TestFixture]
public class WaitToTests
{
    [Test, CancelAfter(1000)]
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


    [Test, CancelAfter(100)]
    public void Unblock_ThenWait_ImmediatelyReleased()
    {
        var wait = new WaitTo<bool>();
        wait.Unblock();
        wait.Wait();
    }
}