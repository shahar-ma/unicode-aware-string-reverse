using System.Globalization;
using System.Text;

namespace Shared.Services.Test;

internal static class StringExtensionsInternal
{
    internal static string RandomCombiningMark(this Random rng) => ((char)rng.Next(StringExtensions.MinDiacriticsBlockValue, StringExtensions.MaxDiacriticsBlockValue)).ToString();
    internal static string RandomWellFormedUnicode(this Random rng, int approxLength)
    {
        StringBuilder sb = new(approxLength);
        int written = 0;
        while (written < approxLength)
        {
            int choice = rng.Next(0, 3);
            switch (choice)
            {
                case 0:
                    sb.AppendAscii(rng, ref written);
                    break;
                case 1:
                    sb.AppendUnicode(rng, ref written);
                    break;
                default:
                    sb.AppendSurrogate(rng, ref written);
                    break;
            }
        }
        return sb.ToString();
    }
    private static void AppendAscii(this StringBuilder sb, Random rng, ref int written)
    {
        sb.Append((char)rng.Next(StringExtensions.MinAsciiValue, StringExtensions.MaxAsciiValue));
        written++;
    }
    private static void AppendUnicode(this StringBuilder sb, Random rng, ref int written)
    {
        sb.Append(rng.RandomBmpBaseChar());
        written++;
        written += sb.AppendRandomCombiningMarks(rng);
    }
    private static void AppendSurrogate(this StringBuilder sb, Random rng, ref int written)
    {
        int codepoint = rng.Next(StringExtensions.MinSurrogatePairValue, StringExtensions.MaxSurrogatePairValue);
        sb.Append(char.ConvertFromUtf32(codepoint));
        written += 2;
        written += sb.AppendRandomCombiningMarks(rng);
    }
    private static int AppendRandomCombiningMarks(this StringBuilder sb, Random rng)
    {
        int count = rng.Next(0, 3);
        for (int index = 0; index < count; index++)
        {
            sb.Append(rng.RandomCombiningMark());
        }
        return count;
    }
    private static char RandomBmpBaseChar(this Random rng)
    {
        while (true)
        {
            int codepoint = rng.Next(StringExtensions.MinUnicodeValue, StringExtensions.MaxUnicodeValue);
            if (char.IsSurrogate((char)codepoint))
                continue;

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory((char)codepoint);
            if (!category.IsUnicodeCategoryMark())
                return (char)codepoint;
        }
    }
}
