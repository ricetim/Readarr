using System.Collections.Generic;
using System.Text.RegularExpressions;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public abstract class SearchCriteriaBase
    {
        private static readonly Regex NonWord = new Regex(@"[^\w`'’]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BeginningThe = new Regex(@"^the\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Collapse runs of single-letter initials (e.g. "C.J.", "J.R.R.") before the period-to-space pass; otherwise indexers receive single-letter tokens.
        private static readonly Regex Initials = new Regex(@"(?:\b[A-Za-z]\.){2,}", RegexOptions.Compiled);

        public virtual bool MonitoredBooksOnly { get; set; }
        public virtual bool UserInvokedSearch { get; set; }
        public virtual bool InteractiveSearch { get; set; }

        public Author Author { get; set; }
        public List<Book> Books { get; set; }

        public string AuthorQuery => GetQueryTitle(Author.Name);

        public static string GetQueryTitle(string title)
        {
            Ensure.That(title, () => title).IsNotNullOrWhiteSpace();

            // Most VA books are listed as VA, not Various Authors
            // TODO: Needed in Readarr??
            if (title == "Various Authors")
            {
                title = "VA";
            }

            var cleanTitle = BeginningThe.Replace(title, string.Empty);

            cleanTitle = cleanTitle.Replace(" & ", " ");
            cleanTitle = Initials.Replace(cleanTitle, m => m.Value.Replace(".", string.Empty));
            cleanTitle = cleanTitle.Replace(".", " ");
            cleanTitle = NonWord.Replace(cleanTitle, "+");

            //remove any repeating +s
            cleanTitle = Regex.Replace(cleanTitle, @"\+{2,}", "+");
            cleanTitle = cleanTitle.RemoveAccent();
            cleanTitle = cleanTitle.Trim('+', ' ');

            return cleanTitle.Length == 0 ? title : cleanTitle;
        }
    }
}
