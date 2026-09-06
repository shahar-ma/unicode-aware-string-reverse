using System.Globalization;
using System.Text;

namespace Shared.Services;

public static class StringExtensions
{
    internal const int LatinCapitalLetterLjWithCaron = 0x0310;
    internal const int MaxAsciiValue = 0x7F;
    internal const int MaxDiacriticsBlockValue = 0x0370;
    internal const int MaxSurrogatePairValue = 0x10FFFF;
    internal const int MaxUnicodeValue = 0x2000;
    internal const int MinAsciiValue = 0x20;
    internal const int MinDiacriticsBlockValue = 0x0300;
    internal const int MinSurrogatePairValue = 0x10000;
    internal const int MinUnicodeValue = 0x00A1;
    /// <summary>
    /// String reversal by using string.Create
    /// </summary>
    /// <param name="content">The input string. May be null or empty.</param>
    /// <returns>
    /// null if <paramref name="content"/> is null (garbage-in/garbage-out,
    /// avoids forcing every caller into a try/catch for a null check
    /// they could just as easily do themselves); the same reference
    /// back if <paramref name="content"/> is empty or a single UTF-16 code
    /// unit (nothing to reorder); otherwise a new reversed string.
    /// </returns>
    /// <remarks>
    /// Provides a string-reversal implementation that does not rely on
    /// Array.Reverse, Enumerable.Reverse, or any built-in "reverse" helper,
    /// and that is aware of the difference between simple single-code-unit
    /// text and text containing multi-code-unit constructs such as UTF-16
    /// surrogate pairs (astral characters, e.g. emoji) and combining
    /// character sequences (base char + diacritics), while allocating
    /// nothing beyond the single returned string at every input size.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> begins with a Unicode combining mark that
    /// has no base character to attach to. Reversing such text is not
    /// well-defined: the mark would attach to whichever base character
    /// ends up before it after reversal, which is not necessarily
    /// invertible. Well-formed text (marks always follow a base
    /// character) never triggers this.
    /// </exception>
    public static string? Reverse(this string? content)
    {
        if (content is null)
            return null;
        if (content.Length <= 1)
            return content;

        content.ThrowIfOrphanLeadingCombiningMark();

        // Check ASCII quickly; use SIMD span reversal for fast-path ASCII
        return Ascii.IsValid(content)
            ? content.ReverseSimpleFast()
            : content.ReverseByClusters();
    }
    internal static string BuildAscii(this Random rng, int length)
    {
        // Zero allocations besides the final string result
        return string.Create(length, rng, static (span, random) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = (char)random.Next(MinAsciiValue, MaxAsciiValue);
            }
        });
    }

    internal static bool IsUnicodeCategoryMark(this UnicodeCategory category) =>
            category is UnicodeCategory.NonSpacingMark
                     or UnicodeCategory.SpacingCombiningMark
                     or UnicodeCategory.EnclosingMark;

    /// <summary>
    /// String reversal by using Array.Reverse
    /// </summary>
    /// <param name="content">The input string. May be null or empty.</param>
    /// <returns>A new reversed string</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/>
    /// </exception>
    internal static string? ReverseString(this string? content)
    {
        if (content is null)
            return null;

        // Avoid allocating char[] array manually
        return string.Create(content.Length, content, static (dest, src) =>
        {
            src.AsSpan().CopyTo(dest);
            dest.Reverse();
        });
    }
    /// <summary>
    /// Length, in UTF-16 code units, of the code point starting at
    /// <paramref name="index"/>: 2 for a valid surrogate pair, 1
    /// otherwise (including an unpaired/lone surrogate half, which is
    /// treated as its own 1-unit "code point" rather than throwing).
    /// </summary>
    private static int CodePointLength(this string content, int index) =>
        char.IsHighSurrogate(content[index]) && index + 1 < content.Length && char.IsLowSurrogate(content[index + 1])
            ? 2
            : 1;

    /// <summary>
    /// Length, in UTF-16 code units, of the grapheme cluster starting at
    /// <paramref name="index"/>: the base code point (1 unit, or 2 for a
    /// surrogate pair) plus any immediately following combining marks.
    /// </summary>
    /// <remarks>
    /// Deliberately uses the (string, int) GetUnicodeCategory overload
    /// unconditionally, even though it does internal surrogate-pairing
    /// work a plain BMP character doesn't need. Two attempts to avoid
    /// that "waste" - System.Text.Rune, and a hand-rolled
    /// char.IsHighSurrogate + char.ConvertToUtf32 + GetUnicodeCategory(int)
    /// path operating on ReadOnlySpan&lt;char&gt; - both benchmarked
    /// meaningfully WORSE (up to ~58x and ~2x respectively, versus this
    /// version's ~12x, relative to a naive Array.Reverse baseline at
    /// length 4096). Whatever the exact JIT/inlining reason, this
    /// straightforward version operating directly on string is the only
    /// one that's actually benchmarked well - do not "optimize" this
    /// further without a profiler and a clean benchmark run to confirm
    /// it first.
    /// </remarks>
    private static int NextClusterLength(this string content, int index)
    {
        int start = index;
        int next = index + content.CodePointLength(index);

        while (next < content.Length && content[next] > MaxAsciiValue)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(content, next);
            if (!cat.IsUnicodeCategoryMark())
            {
                break;
            }
            next += content.CodePointLength(next);
        }
        return next - start;
    }

    /// <summary>
    /// Reverses by grapheme cluster in a single forward pass: as each
    /// cluster's span is discovered, it's copied directly into its final
    /// (decreasing) position in the destination - no separate boundary
    /// array, no ArrayPool rental, and no second pass over the string.
    /// Single code units and bare surrogate pairs - which make up the
    /// overwhelming majority of clusters even in "cluster-aware" text -
    /// are written with a direct indexed assignment rather than
    /// AsSpan/Slice/CopyTo: that generic path carries real per-call
    /// overhead (span construction, slicing, bounds checks) that shows up
    /// clearly at scale when paid for one character at a time. The
    /// span-copy path is reserved for genuine multi-unit runs (3+ code
    /// units: a base character with actual combining marks attached).
    /// </summary>
    private static string ReverseByClusters(this string content)
    {
        return string.Create(content.Length, content, static (dest, src) =>
        {
            int destPos = src.Length;
            int index = 0;

            while (index < src.Length)
            {
                int clusterLength = src.NextClusterLength(index);
                destPos -= clusterLength;

                switch (clusterLength)
                {
                    case 1:
                        dest[destPos] = src[index];
                        break;
                    case 2:
                        dest[destPos] = src[index];
                        dest[destPos + 1] = src[index + 1];
                        break;
                    default:
                        src.AsSpan(index, clusterLength).CopyTo(dest.Slice(destPos, clusterLength));
                        break;
                }
                index += clusterLength;
            }
        });
    }

    private static string ReverseSimpleFast(this string content)
    {
        // MemoryExtensions.Reverse is SIMD-accelerated in modern .NET
        return string.Create(content.Length, content, static (dest, src) =>
        {
            src.AsSpan().CopyTo(dest);
            dest.Reverse();
        });
    }

    private static void ThrowIfOrphanLeadingCombiningMark(this string content)
    {
        if (content[0] <= MaxAsciiValue)
            return; // ASCII can never be a combining mark

        // Deliberately the (string, index) overload, NOT
        // GetUnicodeCategory(content[0]): a genuine supplementary-plane
        // (surrogate-pair) leading combining mark needs the pairing-aware
        // overload to be recognized at all. This method runs exactly
        // once per Reverse() call on a single character, so there is no
        // performance case for the cheaper overload here - unlike
        // NextClusterLength above, which runs once per character across
        // the whole string and genuinely needs to avoid it.
        if (CharUnicodeInfo.GetUnicodeCategory(content, 0).IsUnicodeCategoryMark())
            throw new ArgumentException(SharedResources.InvalidReverseMethodInput, nameof(content));
    }
}
