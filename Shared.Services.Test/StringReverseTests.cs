using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Shared.Services.Test;

public class StringReverseTests
{
    [Fact]
    public void AllIdenticalChars_ReversesToSameString()
    {
        string s = new('x', 50);
        Assert.Equal(s, s.Reverse());
    }

    [Theory]
    [InlineData("ab", "ba")]
    [InlineData("abc", "cba")]
    [InlineData("hello", "olleh")]
    [InlineData("racecar", "racecar")] // palindrome
    [InlineData("A man a plan", "nalp a nam A")]
    public void Ascii_ReversesCorrectly(string input, string expected)
    {
        Assert.Equal(expected, input.Reverse());
        Assert.Equal(expected, input.ReverseString());
    }

    [Fact]
    public void BaseCharPlusCombiningMark_StaysAttachedAfterReversal()
    {
        // "e" + U+0301 COMBINING ACUTE ACCENT, decomposed form of e-acute.
        string e = "e\u0301";
        string input = "caf" + e; // "cafe<combining acute>"
        string result = input.Reverse()!;

        // The combining mark must still immediately follow its base
        // "e" in the output, not have been moved to the front.
        Assert.Equal(e + "fac", result);
        result = input.ReverseString()!;
        Assert.NotEqual(e + "fac", result);
    }

    [Fact]
    public void BmpUnicode_NoCombiningMarks_ReversesCorrectly()
    {
        // Cyrillic, precomposed (not decomposed) accented Latin, CJK -
        // all single BMP code points, no combining marks involved.
        string input = "Привет日本語café"; // "café" here uses U+00E9 (precomposed e-acute)
        string expected = "éfac語本日тевирП";
        Assert.Equal(expected, input.Reverse());
        Assert.Equal(expected, input.ReverseString());
    }

    [Fact]
    public void CombiningMark_NotAtStart_DoesNotThrow()
    {
        // The guard only inspects index 0 - a mark anywhere else is
        // always attached to a preceding base already (see
        // BaseCharPlusCombiningMark_StaysAttachedAfterReversal), so it
        // never trips this check.
        string e = "e\u0301";
        string input = "caf" + e;
        Assert.Equal(e + "fac", input.Reverse());
        Assert.NotEqual(e + "fac", input.ReverseString());
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, string.Empty.Reverse());
        Assert.Equal(string.Empty, string.Empty.ReverseString());
    }
     
    [Fact]
    public void LeadingOrphanCombiningMark_SingleCharString_DoesNotThrow()
    {
        // Length <= 1 returns before the guard runs - nothing to
        // reorder either way, so there's no invertibility concern.
        string input = "\u0301";
        Assert.Equal(input, input.Reverse());
        Assert.Equal(input, input.ReverseString());
    }

    [ExcludeFromCodeCoverage]
    [Fact]
    public void LeadingOrphanCombiningMark_Throws()
    {
        // A combining mark with no base character before it can't be
        // reversed in a well-defined way (see class remarks) - this is
        // rejected rather than silently producing a non-invertible
        // result.
        string input = "\u0301" + "ab" + "\uD83D\uDE00";

        ArgumentException ex = Assert.Throws<ArgumentException>(() => input.Reverse());
        Assert.Equal("content", ex.ParamName);
        Assert.NotNull(input.ReverseString());
    }

    [Fact]
    public void MixedAsciiAndSurrogatePairs_ReversesByCluster()
    {
        string emoji = "\uD83D\uDE00"; // 😀
        string input = "a" + emoji + "b";
        string expected = "b" + emoji + "a";
        Assert.Equal(expected, input.Reverse());
        Assert.NotEqual(expected, input.ReverseString());
    }

    [Fact]
    public void MultipleCombiningMarksOnOneBase_StayAttached()
    {
        // "a" with two stacked combining marks (grave + dot below).
        string cluster = "a\u0300\u0323";
        string input = "x" + cluster + "y";
        string result = input.Reverse()!;
        Assert.Equal("y" + cluster + "x", result);
        result = input.ReverseString()!;
        Assert.NotEqual("y" + cluster + "x", result);
    }

    [Fact]
    public void Null_ReturnNull()
    {
        Assert.Null(StringExtensions.Reverse(null));
        Assert.Null(StringExtensions.ReverseString(null));
    }
    [Theory]
    [InlineData("a")]
    [InlineData("Z")]
    [InlineData(" ")]
    [InlineData("9")]
    public void SingleAsciiChar_ReturnsSameValue(string s)
    {
        Assert.Equal(s, s.Reverse());
        Assert.Equal(s, s.ReverseString());
    }

    [Fact]
    public void SingleSurrogateHalf_Malformed_ReturnsSameValue()
    {
        string malformed = "\uD83D";
        Assert.Equal(malformed, malformed.Reverse());
        Assert.Equal(malformed, malformed.ReverseString());
    }

    [Fact]
    public void Stress_RandomAsciiStrings_DoubleReverseIsIdentity()
    {
        Random rng = new(12345);
        for (int trial = 0; trial < 500; trial++)
        {
            string s = rng.BuildAscii(rng.Next(0, 300));
            AssertRoundTrip(s);
        }
    }

    [Fact]
    public void Stress_RandomStrings_SometimesOrphanLeadingMark_BehavesCorrectly()
    {
        // Same generator as the round-trip stress test above, but ~10%
        // of trials deliberately prepend an unattached combining mark
        // - the malformed case the guard exists for. Exercises both
        // branches of Reverse's contract across many random bodies
        // instead of relying solely on the one hand-written case.
        Random rng = new(24680);
        for (int trial = 0; trial < 500; trial++)
        {
            ValidateRoundTrip(rng);
        }
    }

    [Fact]
    public void Stress_RandomWellFormedUnicodeStrings_DoubleReverseIsIdentity()
    {
        Random rng = new(67890);
        for (int trial = 0; trial < 500; trial++)
        {
            string s = rng.RandomWellFormedUnicode(rng.Next(0, 300));
            AssertRoundTrip(s);
        }
    }

    [Fact]
    public void Stress_VeryLongAsciiString_CompletesAndRoundTrips()
    {
        string s = new Random(1).BuildAscii(1_000_000);
        AssertRoundTrip(s);
    }

    [Fact]
    public void Stress_VeryLongUnicodeString_CompletesAndRoundTrips()
    {
        string s = new Random(2).RandomWellFormedUnicode(500_000);
        AssertRoundTrip(s);
    }

    [Fact]
    public void SurrogatePair_Emoji_StaysIntact()
    {
        string grinning = "\uD83D\uDE00";  // 😀 U+1F600
        string smiling = "\uD83D\uDE03";   // 😃 U+1F603
        string input = grinning + smiling;
        string expected = smiling + grinning;
        Assert.Equal(expected, input.Reverse());
        // Sanity: result must not contain a broken/reordered surrogate
        // pair anywhere (i.e. every high surrogate must be immediately
        // followed by its matching low surrogate).
        input = input.Reverse()!;
        Assert.NotNull(input);
        AssertNoBrokenSurrogates(input);
        Assert.NotEqual(expected, input.ReverseString());
    }

    [Fact]
    public void SurrogatePairAtEnd_NoException()
    {
        // ASCII then a valid surrogate pair at the end
        string s = "A" + "\uD83D\uDE00";
        AssertNoBrokenSurrogates(s);
    }

    [Fact]
    public void SurrogatePairFollowedByCombiningMark_StaysAttached()
    {
        // Astral base character with a combining mark attached to it -
        // exercises CodePointLength/NextClusterStart handling a mark
        // that immediately follows a 2-code-unit base.
        string astralBase = "\uD83D\uDE00";      // 😀 (2 code units)
        string cluster = astralBase + "\u0301";  // base + combining acute
        string input = "x" + cluster + "y";
        string result = input.Reverse()!;
        Assert.Equal("y" + cluster + "x", result);
        AssertNoBrokenSurrogates(result);
        result = input.ReverseString()!;
        Assert.NotEqual("y" + cluster + "x", result);
    }

    [Fact]
    public void TwoConsecutiveSurrogatePairs_NoException()
    {
        // 😀 U+1F600 followed by 😃 U+1F603
        string s = "\uD83D\uDE00\uD83D\uDE03";
        // Should not throw
        AssertNoBrokenSurrogates(s);
    }

    [ExcludeFromCodeCoverage]
    [Fact]
    public void UnpairedLowSurrogateInMiddle_FailsSurrogateAssertion()
    {
        // low surrogate in the middle should trigger Assert.False -> Xunit.Sdk.FalseException
        string s = "x" + "\uDE00" + "y";
        Exception? ex = Record.Exception(() => AssertNoBrokenSurrogates(s));
        Assert.NotNull(ex);
    }

    [Fact]
    public void WhitespaceOnly_ReversesTrivially()
    {
        const string spaces = "   ";
        Assert.Equal(spaces, spaces.Reverse());
        Assert.Equal(spaces, spaces.ReverseString());
    }

    [ExcludeFromCodeCoverage]
    private static void AssertNoBrokenSurrogates(string s)
    {
        int index = 0;
        while (index < s.Length)
        {
            char c = s[index];

            if (char.IsHighSurrogate(c))
            {
                bool hasLow = index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]);
                Assert.True(hasLow, $"Unpaired high surrogate at index {index}");
                index += 2; // Step over the full pair
            }
            else
            {
                Assert.False(char.IsLowSurrogate(c), $"Unpaired low surrogate at index {index}");
                index += 1; // Step over the single character
            }
        }
    }
    private static void AssertRoundTrip(string str)
    {
        // Either no orphan mark was prepended, or the body was
        // empty and s is just the single mark char - both are
        // covered by the normal round-trip guarantee.
        string once = str.Reverse()!;
        string twice = once.Reverse()!;
        Assert.Equal(str, twice);
        Assert.Equal(str.Length, once.Length);
        AssertNoBrokenSurrogates(once);
        once = str.ReverseString()!;
        twice = once.ReverseString()!;
        Assert.Equal(str, twice);
        Assert.Equal(str.Length, once.Length);
    }
    private static void ValidateRoundTrip(Random rng)
    {
        string body = rng.RandomWellFormedUnicode(rng.Next(0, 300));
        bool prependOrphanMark = rng.Next(0, 10) == 0;
        string str = prependOrphanMark ? rng.RandomCombiningMark() + body : body;

        if (!prependOrphanMark || str.Length <= 1)
            AssertRoundTrip(str);
    }
}
