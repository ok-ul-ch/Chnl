namespace Chnl.Tests;

public class PositionTests
{
    [Test]
    public void MoveNextLap_ResetsIndex()
    {
        var lap = 42u;
        var bufferIndex = 24;

        var initial = new Position(lap, bufferIndex);
        var updated = initial.MoveNextLap();

        Assert.Multiple(() =>
        {
            Assert.That(updated.Index, Is.EqualTo(0));
            Assert.That(updated.Lap, Is.EqualTo(lap + 2));
        });
    }

    [Test]
    public void MoveNextLap_PreservesClosedBit()
    {
        var lap = 42u;
        var bufferIndex = 24;

        var initial = new Position(lap, bufferIndex).Close();
        var updated = initial.MoveNextLap();

        Assert.Multiple(() =>
        {
            Assert.That(updated.Lap, Is.EqualTo(lap + 2));
            Assert.That(updated.IsClosed);
        });
    }

    [TestCase(Position.ClosedMask - 1, ExpectedResult = 1u)]
    [TestCase(Position.ClosedMask - 2, ExpectedResult = 0u)]
    public uint MoveNextLap_Overflow_PreservesClosedBit(uint inputLap)
    {
        var bufferIndex = 24;

        var initial = new Position(inputLap, bufferIndex).Close();
        var updated = initial.MoveNextLap();

        Assert.That(updated.IsClosed);

        return updated.Lap;
    }

    [TestCase(Position.ClosedMask - 1, ExpectedResult = 1u)]
    [TestCase(Position.ClosedMask - 2, ExpectedResult = 0u)]
    public uint MoveNextLap_Overflow_Wraps(uint inputLap)
    {
        var bufferIndex = 24;

        var initial = new Position(inputLap, bufferIndex);
        var updated = initial.MoveNextLap();

        Assert.That(!updated.IsClosed);

        return updated.Lap;
    }

    [Test]
    public void MoveNextIndex_Increments()
    {
        var lap = 42u;
        var bufferIndex = 24;

        var initial = new Position(lap, bufferIndex);
        var updated = initial.MoveNextIndex();

        Assert.Multiple(() =>
        {
            Assert.That(updated.Index, Is.EqualTo(bufferIndex + 1));
            Assert.That(updated.Lap, Is.EqualTo(lap));
        });
    }

    [Test]
    public void MoveNextIndex_Overflow_Wraps()
    {
        var lap = 42u;
        var bufferIndex = int.MaxValue;

        var initial = new Position(lap, bufferIndex);
        var updated = initial.MoveNextIndex();

        Assert.That(updated.Index, Is.EqualTo(int.MinValue));
    }

    [Test]
    public void ToIntAndFrom_EncodeDecodeCorrectly()
    {
        var original = Position.TailStart.MoveNextIndex().MoveNextLap();

        var encoded = original.ToInt();
        Assert.That(encoded, Is.EqualTo((long)original.Index << 32 | original.Lap));

        var decoded = Position.From(encoded);
        Assert.That(original, Is.EqualTo(decoded));
    }
}