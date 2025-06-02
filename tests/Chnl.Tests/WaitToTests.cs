namespace Chnl.Tests;

[TestFixture]
public class WaitToTests
{
    [Test, CancelAfter(1000)]
    public void Wait_ThenUnblock_Released()
    {
        var wait = new WaitTo<bool>();

        var blockedThread = new Thread(() =>
        {
            wait.Wait();
        });
        
        blockedThread.Start();

        Thread.Sleep(100);
        
        // Ensure that the thread is still waiting
        Assert.That(blockedThread.IsAlive);
        
        wait.Unblock();

        // Join with the blocked thread. In case if it is still blocked the test will fail with timeout
        blockedThread.Join();
        Assert.That(!blockedThread.IsAlive);
    }


    [Test, CancelAfter(100)]
    public void Unblock_ThenWait_ImmediatelyReleased()
    {
        var wait = new WaitTo<bool>();
        wait.Unblock();
        wait.Wait();
    }
}