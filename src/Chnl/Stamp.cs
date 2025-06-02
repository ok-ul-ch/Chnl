using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Chnl;

[StructLayout(LayoutKind.Explicit)]
internal record struct Stamp(uint Lap, int BufferIndex)
{
    [FieldOffset(0)] public uint Lap = Lap;
    [FieldOffset(4)] public int BufferIndex = BufferIndex;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ulong ToInt() => Unsafe.BitCast<Stamp, ulong>(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Stamp From(ulong value) => Unsafe.BitCast<ulong, Stamp>(value);

    public static readonly Stamp Closed = new(0, 0);
    public static readonly Stamp TailStart = new(2, 0);
    public static readonly Stamp HeadStart = new(3, 0);
}