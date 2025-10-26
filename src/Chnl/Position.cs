using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Chnl;

/// Represents a channel reader/writer position.
/// <br/>
/// It is expected to be stored as 64-bit integer and the encoding as follows:
/// <list type="bullet">
///     <item>[0-31]  - <see cref="Sequence"/> (bits 0-30: Lap, bit 31: Closed flag)</item>
///     <item>[32-63] - <see cref="Index"/></item>
/// </list>
[StructLayout(LayoutKind.Explicit)]
internal readonly record struct Position
{
    /// 31st bit is responsible for closure indication
    public const uint ClosedMask = 1u << 31;
    public const uint MaxLap = ClosedMask - 1;

    /// Lap index and Closed flag
    [FieldOffset(0)] public readonly uint Sequence;

    /// Index within the current lap
    [FieldOffset(4)] public readonly int Index;

    public uint Lap => Sequence & (ClosedMask - 1);
    public bool IsClosed => (Sequence & ClosedMask) != 0;

    // Not private only for test purposes
    internal Position(uint sequence, int index)
    {
        Sequence = sequence;
        Index = index;
    }

    public static readonly Position Closed = new(ClosedMask, 0);
    public static readonly Position TailStart = new(0, 0);
    public static readonly Position HeadStart = new(1, 0);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ToInt() => Unsafe.BitCast<Position, long>(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Position From(long value) => Unsafe.BitCast<long, Position>(value);

    /// Creates a new Position representing start of the next lap (current + 2) and index 0
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Position MoveNextLap() => new((ClosedMask & Sequence) | WrapAddLap(2), 0);

    /// Creates a new Position representing the next index within the same lap
    /// <br/>
    /// Throws <see cref="OverflowException"/> if the current index is int.MaxValue
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Position MoveNextIndex() => new(Sequence, unchecked(Index + 1));

    /// Creates a new Position with Closed bit set
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Position Close() => new(ClosedMask | Sequence, Index);

    /// Computes the incremented lap value with wrapping
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint WrapAddLap(uint incBy) => WrapAddLap(Lap, incBy);

    /// Computes the incremented lap value with wrapping
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint WrapAddLap(uint lap, uint incBy) => (lap + incBy) & (ClosedMask - 1);
}