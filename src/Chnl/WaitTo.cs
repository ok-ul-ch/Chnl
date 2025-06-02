namespace Chnl;

internal class WaitTo<T> : IDisposable
{
    private readonly ManualResetEventSlim _resetEvent = new(false);

    public void Wait()
    {
        _resetEvent.Wait();
    }

    public void Unblock()
    {
        _resetEvent.Set();
    }

    public void Dispose()
    {
        _resetEvent.Dispose();
    }
}