using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Bibliotik
{
    public class BibliotikParser : IParseIndexerResponse
    {
        private static readonly Regex RowRegex = new Regex(
            @"<tr\b[^>]*>(?<row>.*?)</tr>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex TitleLinkRegex = new Regex(
            @"class=""title""[^>]*>.*?<a[^>]+href=""(?<url>[^""]+)""[^>]*>(?<title>[^<]+)</a>",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex YearRegex = new Regex(
            @"class=""torYear""[^>]*>\s*\(?(?<year>\d{4})\)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FormatRegex = new Regex(
            @"class=""torFormat""[^>]*>(?<format>[^<]+)<",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AuthorRegex = new Regex(
            @"class=""(?:authorLink|editorLink)""[^>]*>(?<author>[^<]+)<",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DownloadUrlRegex = new Regex(
            @"title=""Download""[^>]*href=""(?<url>[^""]+)""|href=""(?<url>[^""]+)""[^>]*title=""Download""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DateRegex = new Regex(
            @"<time[^>]+datetime=""(?<date>[^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SizeRegex = new Regex(
            @"data-bytecount=""(?<size>\d+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SeedersRegex = new Regex(
            @"class=""seeders""[^>]*>(?<seeders>\d+)<",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LeechersRegex = new Regex(
            @"class=""leechers""[^>]*>(?<leechers>\d+)<",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TorrentIdRegex = new Regex(
            @"/torrents/(?<id>\d+)(?:/|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly string _baseUrl;

        public BibliotikParser(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var releases = new List<ReleaseInfo>();

            if (indexerResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new IndexerException(indexerResponse,
                    $"Unexpected response status {indexerResponse.HttpResponse.StatusCode} from Bibliotik");
            }

            var content = indexerResponse.HttpResponse.Content;

            if (!content.Contains("id=\"torrents_table\""))
            {
                throw new IndexerException(indexerResponse,
                    "Bibliotik: not logged in or unexpected page returned");
            }

            foreach (Match rowMatch in RowRegex.Matches(content))
            {
                var row = rowMatch.Groups["row"].Value;

                if (!row.Contains("class=\"title\""))
                {
                    continue;
                }

                var release = ParseRow(row);
                if (release != null)
                {
                    releases.Add(release);
                }
            }

            return releases;
        }

        private BibliotikInfo ParseRow(string row)
        {
            var titleMatch = TitleLinkRegex.Match(row);
            if (!titleMatch.Success)
            {
                return null;
            }

            var titlePath = titleMatch.Groups["url"].Value;
            var bookTitle = WebUtility.HtmlDecode(titleMatch.Groups["title"].Value.Trim());

            if (bookTitle.IsNullOrWhiteSpace())
            {
                return null;
            }

            var idMatch = TorrentIdRegex.Match(titlePath);
            if (!idMatch.Success)
            {
                return null;
            }

            var torrentId = idMatch.Groups["id"].Value;

            var authorMatch = AuthorRegex.Match(row);
            var author = authorMatch.Success
                ? WebUtility.HtmlDecode(authorMatch.Groups["author"].Value.Trim())
                : string.Empty;

            var yearMatch = YearRegex.Match(row);
            var year = yearMatch.Success ? yearMatch.Groups["year"].Value.Trim() : string.Empty;

            var formatMatch = FormatRegex.Match(row);
            var format = formatMatch.Success ? formatMatch.Groups["format"].Value.Trim().ToUpperInvariant() : string.Empty;

            var downloadUrlMatch = DownloadUrlRegex.Match(row);
            var downloadPath = downloadUrlMatch.Success
                ? downloadUrlMatch.Groups["url"].Value
                : $"/torrents/{torrentId}/download";

            var downloadUrl = downloadPath.StartsWith("http")
                ? downloadPath
                : _baseUrl + downloadPath;

            var infoUrl = titlePath.StartsWith("http")
                ? titlePath
                : _baseUrl + titlePath;

            var sizeMatch = SizeRegex.Match(row);
            var size = sizeMatch.Success && long.TryParse(sizeMatch.Groups["size"].Value, out var parsedSize)
                ? parsedSize
                : 0L;

            var dateMatch = DateRegex.Match(row);
            var publishDate = DateTime.UtcNow;
            if (dateMatch.Success)
            {
                DateTime.TryParse(
                    dateMatch.Groups["date"].Value,
                    null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out publishDate);
            }

            var seedersMatch = SeedersRegex.Match(row);
            var seeders = seedersMatch.Success && int.TryParse(seedersMatch.Groups["seeders"].Value, out var s) ? s : 0;

            var leechersMatch = LeechersRegex.Match(row);
            var leechers = leechersMatch.Success && int.TryParse(leechersMatch.Groups["leechers"].Value, out var l) ? l : 0;

            // Build title: "Author - Title (Year) [FORMAT]" — embeds author and format so Readarr's
            // parser can fuzzy-match author and detect quality from codec tokens.
            var titleParts = new System.Text.StringBuilder();
            if (author.IsNotNullOrWhiteSpace())
            {
                titleParts.Append(author).Append(" - ");
            }

            titleParts.Append(bookTitle);

            if (year.IsNotNullOrWhiteSpace())
            {
                titleParts.Append(" (").Append(year).Append(')');
            }

            if (format.IsNotNullOrWhiteSpace())
            {
                titleParts.Append(" [").Append(format).Append(']');
            }

            return new BibliotikInfo
            {
                Guid = $"Bibliotik-{torrentId}",
                Title = titleParts.ToString(),
                Author = author,
                DownloadUrl = downloadUrl,
                InfoUrl = infoUrl,
                Size = size,
                PublishDate = publishDate,
                Seeders = seeders,
                Peers = seeders + leechers,
                DownloadProtocol = DownloadProtocol.Torrent
            };
        }
    }
}
