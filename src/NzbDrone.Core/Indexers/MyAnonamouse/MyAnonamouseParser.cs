using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouseParser : IParseIndexerResponse
    {
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
                throw new IndexerException(indexerResponse, $"MyAnonamouse authentication error: {jsonResponse.Resource.Error}");
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

                torrentInfos.Add(new MyAnonamouseInfo
                {
                    Guid = $"MAM-{torrent.Id}",
                    Title = WebUtility.HtmlDecode(torrent.Name),
                    Author = ParseAuthorInfo(torrent.Author_Info),
                    Size = TryParseLong(torrent.Size),
                    DownloadUrl = $"https://www.myanonamouse.net/tor/download.php?tid={torrent.Id}",
                    InfoUrl = $"https://www.myanonamouse.net/t/{torrent.Id}",
                    PublishDate = DateTime.Parse(torrent.Added, null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime(),
                    Seeders = seeders,
                    Peers = (seeders ?? 0) + (leechers ?? 0),
                    DownloadProtocol = DownloadProtocol.Torrent,
                    IndexerFlags = flags
                });
            }

            return torrentInfos
                .OrderByDescending(o => o.PublishDate)
                .ToArray();
        }

        private static string ParseAuthorInfo(string authorInfo)
        {
            if (string.IsNullOrWhiteSpace(authorInfo))
            {
                return string.Empty;
            }

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(authorInfo);
                if (dict != null && dict.Count > 0)
                {
                    return dict.Values.First();
                }
            }
            catch
            {
                // Return empty string on any parse failure
            }

            return string.Empty;
        }

        private static long TryParseLong(string value)
        {
            if (long.TryParse(value, out var result))
            {
                return result;
            }

            return 0;
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
