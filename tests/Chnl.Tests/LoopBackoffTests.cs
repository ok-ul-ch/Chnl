namespace Chnl.Tests;

[TestFixture]
public class LoopBackoffTests
{
    [Test]
    public void Wait_LeadsToExhaustion()
    {
        var backoff = new LoopBackoff();

        while (!backoff.IsExhausted)
        {
            backoff.SpinOrYield();
        }

        Assert.That(backoff.IsExhausted);
    }

    [Test]
    public void Wait_TransitionsFromSpinToYield()
    {
        var backoff = new LoopBackoff();

        for (var i = 0; i < LoopBackoff.MaxSpinIteration; i++)
        {
            backoff.SpinOrYield();
        }

        Assert.That(!backoff.IsExhausted);

        for (var i = LoopBackoff.MaxSpinIteration; i <= LoopBackoff.MaxYieldIteration; i++)
        {
            backoff.SpinOrYield();
        }

        Assert.That(backoff.IsExhausted);
    }
}