using System.Net;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    /// <summary>
    /// Converts tracker-supplied BBCode to plain text.
    /// Descriptions are user-generated content from a private tracker, so they are
    /// never rendered as markup — stripping keeps the value a safe plain string.
    /// </summary>
    public static class BbCodeCleaner
    {
        private static readonly Regex ImgTagRegex = new Regex(
            @"\[img[^\]]*\].*?\[/img\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex AnyTagRegex = new Regex(
            @"\[/?[a-z0-9*]+(?:=[^\]]*)?\]",
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

            var text = WebUtility.HtmlDecode(input);

            text = ImgTagRegex.Replace(text, string.Empty);
            text = AnyTagRegex.Replace(text, string.Empty);

            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            text = ExcessNewlineRegex.Replace(text, "\n\n");

            text = text.Trim();

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}
