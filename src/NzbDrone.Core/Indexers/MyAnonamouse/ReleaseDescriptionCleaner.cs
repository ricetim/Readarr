using System.Net;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    /// <summary>
    /// Converts a tracker-supplied release description to plain text.
    /// Uploaders write descriptions in BBCode, in HTML pasted from publisher or retailer
    /// pages, or a mixture of both, so this handles both markup languages.
    /// Descriptions are user-generated content from a private tracker and are never
    /// rendered as markup — stripping keeps the value a safe plain string.
    /// </summary>
    public static class ReleaseDescriptionCleaner
    {
        private static readonly Regex ScriptOrStyleRegex = new Regex(
            @"<(script|style)\b[^<>]*>.*?</\1\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex BbCodeImgRegex = new Regex(
            @"\[img[^\]]*\].*?\[/img\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex BbCodeTagRegex = new Regex(
            @"\[/?[a-z0-9*]+(?:=[^\]]*)?\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Tags that end a visual block, so the text either side must not run together.
        private static readonly Regex HtmlBreakRegex = new Regex(
            @"<br\s*/?>|</(p|div|li|tr|h[1-6])\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Deliberately requires a letter (optionally after '/') immediately following '<',
        // so decoded comparisons such as "5 < 10 and 20 > 15" are not mistaken for a tag
        // and silently deleted.
        private static readonly Regex HtmlTagRegex = new Regex(
            @"</?[a-z][a-z0-9]*\b[^<>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ExcessNewlineRegex = new Regex(
            @"\n{3,}",
            RegexOptions.Compiled);

        public static string Strip(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var text = input;

            text = ScriptOrStyleRegex.Replace(text, string.Empty);
            text = BbCodeImgRegex.Replace(text, string.Empty);

            text = HtmlBreakRegex.Replace(text, "\n");
            text = HtmlTagRegex.Replace(text, string.Empty);
            text = BbCodeTagRegex.Replace(text, string.Empty);

            // Decode last so entities never become markup that the strips above have
            // already passed over. The second pass catches descriptions that arrive
            // with their markup HTML-escaped.
            text = WebUtility.HtmlDecode(text);
            text = HtmlBreakRegex.Replace(text, "\n");
            text = HtmlTagRegex.Replace(text, string.Empty);

            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            text = ExcessNewlineRegex.Replace(text, "\n\n");

            text = text.Trim();

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}
