using System.Buffers;
using System.Diagnostics.CodeAnalysis;
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
    /// the same reference back if <paramref name="content"/> is empty or a single UTF-16 code
    /// unit (nothing to reorder); otherwise a new reversed string.
    /// </returns>
    /// <remarks>
    /// Provides a string-reversal implementation that does not rely on
    /// Array.Reverse, Enumerable.Reverse, or any built-in "reverse" helper,
    /// and that is aware of the difference between simple single-code-unit
    /// text and text containing multi-code-unit constructs such as UTF-16
    /// surrogate pairs (astral characters, e.g. emoji) and combining
    /// character sequences (base char + diacritics), while allocating as
    /// little as possible at every input size.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/>
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> begins with a Unicode combining mark that
    /// has no base character to attach to. Reversing such text is not
    /// well-defined: the mark would attach to whichever base character
    /// ends up before it after reversal, which is not necessarily
    /// invertible. Well-formed text (marks always follow a base
    /// character) never triggers this.
    /// </exception>
    public static string Reverse([AllowNull] this string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length <= 1)
            return content;
        content.ThrowIfOrphanLeadingCombiningMark();
        if (Ascii.IsValid(content))
            return content.ReverseSimpleFast();
        return content.NeedsClusterAwareReversal()
            ? content.ReverseByClusters()
            : content.ReverseSimpleFast();
    }
    /// <summary>
    /// String reversal by using Array.Reverse
    /// </summary>
    /// <param name="content">The input string. May be null or empty.</param>
    /// <returns>A new reversed string</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="content"/>
    /// </exception>
    public static string ReverseString([AllowNull] this string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        char[] charArray = content.ToCharArray();
        Array.Reverse(charArray);
        return new string(charArray);
    }

    internal static string BuildAscii(this Random rng, int length)
    {
        StringBuilder sb = new(length);
        for (int index = 0; index < length; index++)
        {
            sb.Append((char)rng.Next(MinAsciiValue, MaxAsciiValue));
        }
        return sb.ToString();
    }
    internal static bool IsUnicodeCategoryMark(this UnicodeCategory category) => category is UnicodeCategory.NonSpacingMark
                     or UnicodeCategory.SpacingCombiningMark
                     or UnicodeCategory.EnclosingMark;

    private static string BuildClusterString(this string content, int[] starts, int clusterCount)
    {
        return string.Create(content.Length, (content, starts, clusterCount), static (dest, state) =>
        {
            (string src, int[] st, int count) = state;
            int destPos = 0;
            for (int cnt = count - 1; cnt >= 0; cnt--)
            {
                int start = st[cnt];
                int end = st[cnt + 1];
                int len = end - start;
                src.AsSpan(start, len).CopyTo(dest.Slice(destPos, len));
                destPos += len;
            }
        });
    }

    private static int CodePointLength(this string content, int index) => char.IsHighSurrogate(content[index]) && index + 1 < content.Length && char.IsLowSurrogate(content[index + 1])
            ? 2
            : 1;
    private static bool NeedsClusterAwareReversal(this string content)
    {
        for (int index = 0; index < content.Length; index++)
        {
            char c = content[index];
            if (c <= MaxAsciiValue)
                continue;

            if (char.IsSurrogate(c))
                return true;

            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat.IsUnicodeCategoryMark())
                return true;
        }
        return false;
    }
    private static int NextClusterStart(this string content, int index)
    {
        int stringLength = content.Length;
        int nextIndex = index + content.CodePointLength(index);

        while (nextIndex < stringLength)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(content, nextIndex);
            if (cat.IsUnicodeCategoryMark())
            {
                nextIndex += content.CodePointLength(nextIndex);
            }
            else
            {
                break;
            }
        }
        return nextIndex;
    }
    private static string ReverseByClusters(this string content)
    {
        int contentLength = content.Length;
        int[] starts = ArrayPool<int>.Shared.Rent(contentLength + 1);
        try
        {
            int clusterCount = 0;
            int index = 0;
            while (index < contentLength)
            {
                starts[clusterCount++] = index;
                index = content.NextClusterStart(index);
            }
            starts[clusterCount] = contentLength; // sentinel end index

            return content.BuildClusterString(starts, clusterCount);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(starts);
        }
    }
    private static string ReverseSimpleFast(this string content)
    {
        return string.Create(content.Length, content, static (dest, src) =>
        {
            int last = src.Length - 1;
            for (int index = 0; index <= last; index++)
            {
                dest[last - index] = src[index];
            }
        });
    }
    private static void ThrowIfOrphanLeadingCombiningMark(this string content)
    {
        if (content[0] <= MaxAsciiValue)
            return; // ASCII can never be a combining mark

        UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(content, 0);
        if (cat.IsUnicodeCategoryMark())
            throw new ArgumentException(SharedResources.InvalidReverseMethodInput, nameof(content));
    }
}
