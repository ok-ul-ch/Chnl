using System.Runtime.CompilerServices;

namespace Chnl;

/// Exponential backoff for loops operating under contention. Aimed to reduce contention and improve CPU resources utilization 
internal struct LoopBackoff
{
    /// Maximum number of thread spin iterations. The value is somewhat arbitrary and copied from `crossbeam` 
    internal const int MaxSpinIteration = 6;

    /// Maximum number of thread yield iterations. The value is somewhat arbitrary and copied from `crossbeam`  
    internal  const int MaxYieldIteration = 10;

    private int _waitIteration = 0;

    /// Signals to the caller that it is time for him to give up waiting this Backoff and block the executing thread instead
    public bool IsExhausted => _waitIteration > MaxYieldIteration;

    public LoopBackoff()
    {
    }

    /// Waits for backoff time using only CPU spinning
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SpinWait()
    {
        Thread.SpinWait(1 << Math.Min(_waitIteration, MaxSpinIteration));

        if (_waitIteration <= MaxSpinIteration)
        {
            _waitIteration++;
        }
    }

    /// Waits for backoff time. It can either be Spin (busy-wait/PAUSE) OR Yield (give up own CPU time and allow the OS scheduler to do other work)    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Wait()
    {
        if (_waitIteration < MaxSpinIteration)
        {
            Thread.SpinWait(1 << _waitIteration);
        }
        else
        {
            Thread.Yield();
        }

        _waitIteration++;
    }
}