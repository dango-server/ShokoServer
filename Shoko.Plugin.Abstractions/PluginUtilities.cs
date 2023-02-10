using System;
using System.Text.RegularExpressions;

namespace Shoko.Plugin.Abstractions
{
    public static class PluginUtilities
    {
        public static string RemoveInvalidPathCharacters(this string path)
        {
            string ret = path.Replace(@"*", string.Empty);
            ret = ret.Replace(@"|", string.Empty);
            ret = ret.Replace(@"\", string.Empty);
            ret = ret.Replace(@"/", string.Empty);
            ret = ret.Replace(@":", string.Empty);
            ret = ret.Replace("\"", string.Empty); // double quote
            ret = ret.Replace(@">", string.Empty);
            ret = ret.Replace(@"<", string.Empty);
            ret = ret.Replace(@"?", string.Empty);
            while (ret.EndsWith("."))
                ret = ret.Substring(0, ret.Length - 1);
            return ret.Trim();
        }

        public static string ReplaceInvalidPathCharacters(this string path)
        {
            string ret = path;
            // Replace all invalid characters with spaces
            ret = ret.Replace(@"*", " "); // ★ (BLACK STAR)
            ret = ret.Replace(@"|", " "); // ¦ (BROKEN BAR)
            ret = ret.Replace(@"\", " "); // ⧹ (BIG REVERSE SOLIDUS)
            ret = ret.Replace(@"/", " "); // ⁄ (FRACTION SLASH)
            ret = ret.Replace(@":", " "); // ։ (ARMENIAN FULL STOP)
            ret = ret.Replace("\"", " "); // ″ (DOUBLE PRIME)
            ret = ret.Replace(@">", " "); // › (SINGLE RIGHT-POINTING ANGLE QUOTATION MARK)
            ret = ret.Replace(@"<", " "); // ‹ (SINGLE LEFT-POINTING ANGLE QUOTATION MARK)
            ret = ret.Replace(@"?", " "); // ？ (FULL WIDTH QUESTION MARK)
            // ret = ret.Replace(@"...", "\u2026"); // … (HORIZONTAL ELLIPSIS)
            // Replace multiple whitespace with a single space
            ret = Regex.Replace(ret, @"\s+", " ");
            if (ret.StartsWith(".", StringComparison.Ordinal)) ret = "․" + ret.Substring(1, ret.Length - 1);
            if (ret.EndsWith(".", StringComparison.Ordinal)) // U+002E
                ret = ret.Substring(0, ret.Length - 1) + "․"; // U+2024
            return ret.Trim();
        }

        public static string PadZeroes(this int num, int total)
        {
            int zeroPadding = total.ToString().Length;
            return num.ToString().PadLeft(zeroPadding, '0');
        }
    }
}
