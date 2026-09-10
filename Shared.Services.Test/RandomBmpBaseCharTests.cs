using Xunit;

namespace Shared.Services.Test;

public class RandomBmpBaseCharTests
{
    [Fact]
    public void RandomBmpBaseChar_SkipsSurrogateAndMark_ReturnsValidChar()
    {
        // Sequence of Next(...) results used by RandomWellFormedUnicode:
        // 1 -> choose BMP branch
        // 0xD800 -> surrogate (should be skipped)
        // 0x0300 -> combining mark (should be skipped)
        // 0x00A1 -> valid non-mark BMP char (returned)
        // 0 -> no combining marks appended
        int[] seq = [1, 0xD800, 0x0300, 0x00A1, 0];
        TestRandom rng = new(seq);

        string result = rng.RandomWellFormedUnicode(1);

        Assert.Equal(((char)0x00A1).ToString(), result);
    }

    [Fact]
    public void TestRandom_Next_WithEmptyQueue_ReturnsMinValueBranchCovered()
    {
        // When the queue is empty, TestRandom.Next returns the provided minValue.
        // This exercises the 'return minValue' branch in TestRandom.Next.
        TestRandom rng = new([]);

        // RandomWellFormedUnicode will call Next(0,3) -> returns 0 -> ASCII branch
        // then Next(MinAsciiValue, MaxAsciiValue) -> returns MinAsciiValue
        // and finally Next(0,3) for combining marks -> returns 0 (no marks)
        string result = rng.RandomWellFormedUnicode(1);

        Assert.Equal(((char)StringExtensions.MinAsciiValue).ToString(), result);
    }

    private sealed class TestRandom(IEnumerable<int> values) : Random
    {
        private readonly Queue<int> _values = new(values);

        public override int Next(int minValue, int maxValue)
        {
            if (_values.Count == 0)
                return minValue;
            return _values.Dequeue();
        }
    }
}
