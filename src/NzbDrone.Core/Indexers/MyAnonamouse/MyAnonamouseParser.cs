using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouseParser : IParseIndexerResponse
    {
        private static readonly Dictionary<string, Language> MamLanguageMap =
            new Dictionary<string, Language>
            {
                { "1",  Language.English },
                { "2",  Language.French },
                { "3",  Language.German },
                { "4",  Language.Spanish },
                { "5",  Language.Italian },
                { "6",  Language.Dutch },
                { "7",  Language.Swedish },
                { "9",  Language.Greek },
                { "10", Language.Russian },
                { "11", Language.Chinese },
                { "12", Language.Japanese },
                { "13", Language.Korean },
                { "15", Language.Vietnamese },
                { "16", Language.Polish },
                { "17", Language.Portuguese },
                { "18", Language.PortugueseBR },
                { "19", Language.Finnish }
            };

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<ReleaseInfo>();

            if (indexerResponse.HttpResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new IndexerException(indexerResponse, $"Unexpected response status {indexerResponse.HttpResponse.StatusCode} code from API request");
            }

            var jsonResponse = new HttpResponse<MyAnonamouseResponse>(indexerResponse.HttpResponse);

            if (jsonResponse.Resource == null)
            {
                return torrentInfos;
            }

            if (!string.IsNullOrWhiteSpace(jsonResponse.Resource.Error))
            {
                if (jsonResponse.Resource.Error.StartsWith("Nothing returned"))
                {
                    return torrentInfos;
                }

                throw new IndexerException(indexerResponse, $"MyAnonamouse error: {jsonResponse.Resource.Error}");
            }

            if (jsonResponse.Resource.Data == null || !jsonResponse.Resource.Data.Any())
            {
                return torrentInfos;
            }

            foreach (var torrent in jsonResponse.Resource.Data)
            {
                var seeders = TryParseInt(torrent.Seeders);
                var leechers = TryParseInt(torrent.Leechers);

                IndexerFlags flags = 0;
                if (torrent.Free == "1" || torrent.Fl_Vip == "1")
                {
                    flags |= IndexerFlags.Freeleech;
                }

                var languages = new List<Language>();
                if (torrent.Language != null &&
                    MamLanguageMap.TryGetValue(torrent.Language, out var parsedLanguage))
                {
                    languages.Add(parsedLanguage);
                }

                var authorName = ParseAuthorInfo(torrent.Author_Info);
                var bookTitle = WebUtility.HtmlDecode(torrent.Title);
                var filetype = torrent.Filetype?.Trim().ToUpperInvariant();

                // Readarr's parser finds the author by fuzzy-matching against the Title string,
                // and quality by parsing codec tokens — both must be in the Title.
                var baseTitle = authorName.IsNotNullOrWhiteSpace()
                    ? $"{authorName} - {bookTitle}"
                    : bookTitle;
                var releaseTitle = filetype.IsNotNullOrWhiteSpace()
                    ? $"{baseTitle} [{filetype}]"
                    : baseTitle;

                torrentInfos.Add(new MyAnonamouseInfo
                {
                    Guid = $"MAM-{torrent.Id}",
                    Title = releaseTitle,
                    Author = authorName,
                    Codec = filetype,
                    Size = RssParser.ParseSize(torrent.Size, true),
                    DownloadUrl = $"https://www.myanonamouse.net/tor/download.php?tid={torrent.Id}",
                    InfoUrl = $"https://www.myanonamouse.net/t/{torrent.Id}",
                    PublishDate = DateTime.Parse(torrent.Added, null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                    Seeders = seeders,
                    Peers = (seeders ?? 0) + (leechers ?? 0),
                    DownloadProtocol = DownloadProtocol.Torrent,
                    IndexerFlags = flags,
                    Languages = languages,
                    Details = BuildDetails(torrent)
                });
            }

            return torrentInfos
                .OrderByDescending(o => o.PublishDate)
                .ToArray();
        }

        private static string ParseAuthorInfo(string authorInfo)
        {
            return ParseNamePairs(authorInfo).FirstOrDefault() ?? string.Empty;
        }

        // author_info and narrator_info share a shape: a JSON object encoded as a
        // string inside a JSON field, mapping ids to names.
        private static List<string> ParseNamePairs(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string>();
            }

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null && dict.Count > 0)
                {
                    return dict.Values.Where(v => v.IsNotNullOrWhiteSpace()).ToList();
                }
            }
            catch
            {
                // Malformed data degrades to no names rather than failing the whole search
            }

            return new List<string>();
        }

        // series_info differs from the name pairs: its values are lists of
        // [name, placement], e.g. {"67": ["Love at Stake", "01-16"]}.
        private static string ParseSeriesInfo(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                var first = dict?.Values.FirstOrDefault();

                if (first == null || first.Count == 0 || first[0].IsNullOrWhiteSpace())
                {
                    return null;
                }

                var placement = first.Count > 1 ? first[1] : null;

                return placement.IsNotNullOrWhiteSpace() ? $"{first[0]} ({placement})" : first[0];
            }
            catch
            {
                // Series info is decorative; never fail a search over it
            }

            return null;
        }

        private static ReleaseDetails BuildDetails(MyAnonamouseTorrent torrent)
        {
            var narrators = ParseNamePairs(torrent.Narrator_Info);
            var series = ParseSeriesInfo(torrent.Series_Info);
            var description = ReleaseDescriptionCleaner.Strip(torrent.Description);
            var fileCount = TryParseInt(torrent.Numfiles);
            var tags = torrent.Tags?.Trim();
            var category = torrent.Catname?.Trim();

            if (!narrators.Any() &&
                !fileCount.HasValue &&
                description.IsNullOrWhiteSpace() &&
                series.IsNullOrWhiteSpace() &&
                tags.IsNullOrWhiteSpace() &&
                category.IsNullOrWhiteSpace())
            {
                return null;
            }

            return new ReleaseDetails
            {
                Narrators = narrators,
                FileCount = fileCount,
                Description = description,
                Series = series,
                Tags = tags.IsNullOrWhiteSpace() ? null : tags,
                Category = category.IsNullOrWhiteSpace() ? null : category
            };
        }

        private static int? TryParseInt(string value)
        {
            if (int.TryParse(value, out var result))
            {
                return result;
            }

            return null;
        }
    }
}
