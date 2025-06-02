namespace Chnl.Tests;

[TestFixture]
public class LoopBackoffTests
{
    [Test]
    public void SpinWait_DoesNotLeadToExhaustion()
    {
        var backoff = new LoopBackoff();
        
        for (var i = 0; i < LoopBackoff.MaxSpinIteration * 2; i++)
        {
            backoff.SpinWait();
        }
        
        Assert.That(!backoff.IsExhausted);
    }

    [Test]
    public void Wait_LeadsToExhaustion()
    {
        var backoff = new LoopBackoff();
        
        while (!backoff.IsExhausted)
        {
            backoff.Wait();
        }
        
        Assert.That(backoff.IsExhausted);
    }

    [Test]
    public void Wait_TransitionsFromSpinToYield()
    {
        var backoff = new LoopBackoff();
        
        for (var i = 0; i < LoopBackoff.MaxSpinIteration; i++)
        {
            backoff.Wait();
        }
        
        Assert.That(!backoff.IsExhausted);
        
        for (var i = LoopBackoff.MaxSpinIteration; i <= LoopBackoff.MaxYieldIteration; i++)
        {
            backoff.Wait();
        }
        
        Assert.That(backoff.IsExhausted);
    }
}