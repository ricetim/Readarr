# MyAnonamouse Native Indexer Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a native MyAnonamouse indexer to Readarr that authenticates via session cookie and queries the MAM JSON search API for book/audiobook search and RSS-style monitoring.

**Architecture:** Six files under `src/NzbDrone.Core/Indexers/MyAnonamouse/`, modeled on the existing Gazelle indexer. Extends `HttpIndexerBase<MyAnonamouseSettings>`. Auth is a single `mam_id` session cookie injected on every request — no login flow. RSS monitoring polls the search API sorted by date using `startDate` to filter to new-only results.

**Tech Stack:** C# / .NET 6, NUnit + Moq (tests), Dapper (not used here), FluentValidation, StyleCop (enforced at build time — warnings = errors).

---

## Codebase orientation

Before starting, read these files to understand conventions:
- `src/NzbDrone.Core/Indexers/Gazelle/Gazelle.cs` — the indexer class to model
- `src/NzbDrone.Core/Indexers/Gazelle/GazelleRequestGenerator.cs` — request building pattern
- `src/NzbDrone.Core/Indexers/Gazelle/GazelleParser.cs` — response parsing pattern
- `src/NzbDrone.Core/Indexers/Gazelle/GazelleSettings.cs` — settings with `[FieldDefinition]`
- `src/NzbDrone.Core/Indexers/Gazelle/GazelleInfo.cs` — DTOs + `TorrentInfo` subclass
- `src/NzbDrone.Core.Test/IndexerTests/GazelleTests/GazelleFixture.cs` — test pattern

**StyleCop rules enforced in this project (violations = build errors):**
- 4-space indentation (not tabs)
- `using` directives must be outside the namespace block
- System usings must come first, then third-party, then project
- File must end with a newline
- Opening braces on new lines (Allman style) — **wait, actually check GazelleParser.cs which uses K&R with opening braces same-line for methods** — follow the existing pattern in indexer files

**Build command (run from repo root):**
```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix
```

**Test command (run from repo root):**
```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouse" \
  -p:Platform=Posix
```

---

## Task 1: Create the fixture JSON file

**Files:**
- Create: `src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouse.json`

This fixture represents a real MAM API response. Tests will mock `IHttpClient` to return this.

**Step 1: Create the directory and fixture file**

```bash
mkdir -p src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse
```

Create `src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouse.json`:

```json
{
    "data": [
        {
            "id": "273200",
            "language": "1",
            "main_cat": "13",
            "category": "108",
            "catname": "Audiobooks - Urban Fantasy",
            "size": "6324306932",
            "numfiles": "149",
            "vip": "0",
            "free": "1",
            "fl_vip": "0",
            "name": "Love at Stake series",
            "tags": "unabridged mp3",
            "author_info": "{\"8234\": \"Kerrelyn Sparks\"}",
            "narrator_info": "{\"1\": \"Abby Craden\"}",
            "series_info": "{\"67\": [\"Love at Stake\", \"01-16\"]}",
            "filetype": "mp3",
            "added": "2023-04-01 12:00:00",
            "seeders": "15",
            "leechers": "3",
            "times_completed": "42",
            "bookmarked": null,
            "dl": "someHashValue"
        },
        {
            "id": "310000",
            "language": "1",
            "main_cat": "14",
            "category": "14",
            "catname": "Ebooks - Fantasy",
            "size": "2048000",
            "numfiles": "1",
            "vip": "0",
            "free": "0",
            "fl_vip": "0",
            "name": "The Name of the Wind",
            "tags": "epub fantasy",
            "author_info": "{\"999\": \"Patrick Rothfuss\"}",
            "narrator_info": "{}",
            "series_info": "{}",
            "filetype": "epub",
            "added": "2023-03-15 08:30:00",
            "seeders": "8",
            "leechers": "1",
            "times_completed": "120",
            "bookmarked": null,
            "dl": "anotherHashValue"
        }
    ],
    "total": 2,
    "total_found": 2
}
```

**Step 2: Commit**

```bash
git add src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouse.json
git commit -m "test: add MyAnonamouse JSON fixture file"
```

---

## Task 2: Create DTOs (`MyAnonamouseInfo.cs`)

**Files:**
- Create: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs`

No tests needed — these are pure data classes.

**Step 1: Create the file**

```csharp
using System.Collections.Generic;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouseTorrent
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Added { get; set; }
        public string Size { get; set; }
        public string Seeders { get; set; }
        public string Leechers { get; set; }
        public string Free { get; set; }
        public string Fl_Vip { get; set; }
        public string Main_Cat { get; set; }
        public string Category { get; set; }
        public string Catname { get; set; }
        public string Author_Info { get; set; }
        public string Narrator_Info { get; set; }
        public string Series_Info { get; set; }
        public string Tags { get; set; }
        public string Filetype { get; set; }
        public string Dl { get; set; }
        public string Times_Completed { get; set; }
    }

    public class MyAnonamouseResponse
    {
        public List<MyAnonamouseTorrent> Data { get; set; }
        public int Total { get; set; }
        public int Total_Found { get; set; }
        public string Error { get; set; }
    }

    public class MyAnonamouseInfo : TorrentInfo
    {
    }
}
```

**Step 2: Verify it compiles**

```bash
dotnet build src/NzbDrone.Core/Readarr.Core.csproj -p:Platform=Posix
```

Expected: Build succeeded with 0 errors.

**Step 3: Commit**

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs
git commit -m "feat: add MyAnonamouse response DTOs"
```

---

## Task 3: Create settings (`MyAnonamouseSettings.cs`)

**Files:**
- Create: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseSettings.cs`

**Step 1: Create the file**

```csharp
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouseSettingsValidator : AbstractValidator<MyAnonamouseSettings>
    {
        public MyAnonamouseSettingsValidator()
        {
            RuleFor(c => c.Cookie).NotEmpty();

            RuleFor(c => c.SeedCriteria).SetValidator(_ => new SeedCriteriaSettingsValidator());
        }
    }

    public class MyAnonamouseSettings : ITorrentIndexerSettings
    {
        private static readonly MyAnonamouseSettingsValidator Validator = new MyAnonamouseSettingsValidator();

        public MyAnonamouseSettings()
        {
            MinimumSeeders = IndexerDefaults.MINIMUM_SEEDERS;
        }

        [FieldDefinition(0, Label = "Session Cookie", Privacy = PrivacyLevel.ApiKey, HelpText = "Your mam_id cookie value from a logged-in browser session. Find it in your browser's developer tools under Application > Cookies > myanonamouse.net.")]
        public string Cookie { get; set; }

        [FieldDefinition(1, Label = "Search Type", Type = FieldType.Select, SelectOptions = typeof(MyAnonamouseSearchType), HelpText = "Whether to include torrents with no active seeders.", Advanced = true)]
        public int SearchType { get; set; }

        [FieldDefinition(2, Type = FieldType.Number, Label = "Early Download Limit", Unit = "days", HelpText = "Time before release date Readarr will download from this indexer, empty is no limit", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        [FieldDefinition(3, Type = FieldType.Textbox, Label = "Minimum Seeders", HelpText = "Minimum number of seeders required.", Advanced = true)]
        public int MinimumSeeders { get; set; }

        [FieldDefinition(4)]
        public SeedCriteriaSettings SeedCriteria { get; set; } = new SeedCriteriaSettings();

        [FieldDefinition(5, Type = FieldType.Checkbox, Label = "Reject Blocklisted Torrent Hashes While Grabbing", HelpText = "If a torrent is blocked by hash it may not properly be rejected during RSS/Search for some indexers, enabling this will allow it to be rejected after the torrent is grabbed, but before it is sent to the client.", Advanced = true)]
        public bool RejectBlocklistedTorrentHashesWhileGrabbing { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }

    public enum MyAnonamouseSearchType
    {
        [FieldOption(Label = "Active (Has Seeders)")]
        Active = 0,

        [FieldOption(Label = "All")]
        All = 1,

        [FieldOption(Label = "Inactive (No Seeders)")]
        Inactive = 2,
    }
}
```

**Step 2: Verify it compiles**

```bash
dotnet build src/NzbDrone.Core/Readarr.Core.csproj -p:Platform=Posix
```

Expected: Build succeeded with 0 errors.

> **Note:** `FieldType.Select` + `SelectOptions` follows the pattern used in other settings files. If the enum attribute doesn't match exactly, check `src/NzbDrone.Core/Annotations/FieldDefinitionAttribute.cs` for the correct attribute name.

**Step 3: Commit**

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseSettings.cs
git commit -m "feat: add MyAnonamouseSettings with cookie auth field"
```

---

## Task 4: Create parser with tests (`MyAnonamouseParser.cs`)

**Files:**
- Create: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs`
- Create: `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs`

### Step 1: Write the failing test

Create `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs`:

```csharp
using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.MyAnonamouse;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonamouseTests
{
    [TestFixture]
    public class MyAnonamouseFixture : CoreTest<MyAnonamouse>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new IndexerDefinition()
            {
                Name = "MyAnonamouse",
                Settings = new MyAnonamouseSettings
                {
                    Cookie = "test_cookie_value"
                }
            };
        }

        [Test]
        public async Task should_parse_recent_feed_from_mam()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(
                    new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            releases.Should().HaveCount(2);
            releases[0].Should().BeOfType<MyAnonamouseInfo>();

            var first = (MyAnonamouseInfo)releases[0];
            first.Title.Should().Be("Love at Stake series");
            first.Author.Should().Be("Kerrelyn Sparks");
            first.Guid.Should().Be("MAM-273200");
            first.DownloadUrl.Should().Be("https://www.myanonamouse.net/tor/download.php?tid=273200");
            first.InfoUrl.Should().Be("https://www.myanonamouse.net/t/273200");
            first.Size.Should().Be(6324306932L);
            first.Seeders.Should().Be(15);
            first.Peers.Should().Be(18);
            first.PublishDate.Should().Be(DateTime.Parse("2023-04-01 12:00:00").ToUniversalTime());
            first.DownloadProtocol.Should().Be(DownloadProtocol.Torrent);
            first.IndexerFlags.Should().HaveFlag(IndexerFlags.Freeleech);
        }

        [Test]
        public async Task should_parse_ebook_entry()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(
                    new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            var second = (MyAnonamouseInfo)releases[1];
            second.Title.Should().Be("The Name of the Wind");
            second.Author.Should().Be("Patrick Rothfuss");
            second.Guid.Should().Be("MAM-310000");
            second.IndexerFlags.Should().NotHaveFlag(IndexerFlags.Freeleech);
        }
    }
}
```

### Step 2: Run the test — verify it fails to compile

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouseFixture" \
  -p:Platform=Posix
```

Expected: **Build error** — `MyAnonamouse` class not found yet.

### Step 3: Create the parser

Create `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs`:

```csharp
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
        private const string BaseUrl = "https://www.myanonamouse.net";

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var torrentInfos = new List<ReleaseInfo>();

            if (indexerResponse.HttpResponse.StatusCode != HttpStatusCode.OK)
            {
                throw new IndexerException(indexerResponse,
                    $"Unexpected response status {indexerResponse.HttpResponse.StatusCode} from API request");
            }

            var jsonResponse = new HttpResponse<MyAnonamouseResponse>(indexerResponse.HttpResponse);

            if (jsonResponse.Resource == null)
            {
                return torrentInfos;
            }

            if (!string.IsNullOrWhiteSpace(jsonResponse.Resource.Error))
            {
                throw new IndexerException(indexerResponse,
                    $"MyAnonamouse API error: {jsonResponse.Resource.Error}");
            }

            if (jsonResponse.Resource.Data == null)
            {
                return torrentInfos;
            }

            foreach (var torrent in jsonResponse.Resource.Data)
            {
                var id = torrent.Id;
                var author = ParseFirstValue(torrent.Author_Info);

                var info = new MyAnonamouseInfo
                {
                    Guid = $"MAM-{id}",
                    Title = WebUtility.HtmlDecode(torrent.Name),
                    Author = author,
                    Size = ParseLong(torrent.Size),
                    DownloadUrl = $"{BaseUrl}/tor/download.php?tid={id}",
                    InfoUrl = $"{BaseUrl}/t/{id}",
                    PublishDate = ParseDate(torrent.Added),
                    Seeders = ParseInt(torrent.Seeders),
                    Peers = ParseInt(torrent.Seeders) + ParseInt(torrent.Leechers),
                    DownloadProtocol = DownloadProtocol.Torrent,
                    IndexerFlags = GetIndexerFlags(torrent),
                };

                torrentInfos.Add(info);
            }

            return torrentInfos.OrderByDescending(o => o.PublishDate).ToArray();
        }

        private static string ParseFirstValue(string jsonDict)
        {
            if (string.IsNullOrWhiteSpace(jsonDict))
            {
                return string.Empty;
            }

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonDict);
                return dict?.Values.FirstOrDefault() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static long ParseLong(string value)
        {
            return long.TryParse(value, out var result) ? result : 0;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, out var result) ? result : 0;
        }

        private static DateTime ParseDate(string value)
        {
            return DateTime.TryParse(value, out var result) ? result.ToUniversalTime() : DateTime.UtcNow;
        }

        private static IndexerFlags GetIndexerFlags(MyAnonamouseTorrent torrent)
        {
            IndexerFlags flags = 0;

            if (torrent.Free == "1" || torrent.Fl_Vip == "1")
            {
                flags |= IndexerFlags.Freeleech;
            }

            return flags;
        }
    }
}
```

### Step 4: Run tests again — they should still fail (no `MyAnonamouse` class yet)

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouseFixture" \
  -p:Platform=Posix
```

Expected: Build error — `MyAnonamouse` and `MyAnonamouseRequestGenerator` not found yet.

### Step 5: Commit the parser

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs
git add src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs
git commit -m "feat: add MyAnonamouseParser with unit tests"
```

---

## Task 5: Create the request generator (`MyAnonamouseRequestGenerator.cs`)

**Files:**
- Create: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseRequestGenerator.cs`

**Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouseRequestGenerator : IIndexerRequestGenerator
    {
        private const string SearchUrl = "https://www.myanonamouse.net/tor/js/loadSearchJSONbasic.php";
        private static readonly int[] BookCategories = { 13, 14 };

        public MyAnonamouseSettings Settings { get; set; }
        public DateTime? LastRssSyncDate { get; set; }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var body = new Dictionary<string, object>
            {
                {
                    "tor", new Dictionary<string, object>
                    {
                        { "main_cat", BookCategories },
                        { "sortType", "dateDesc" },
                        { "searchType", "all" },
                        { "startNumber", 0 },
                    }
                },
                { "perpage", 100 },
            };

            if (LastRssSyncDate.HasValue)
            {
                var torDict = (Dictionary<string, object>)body["tor"];
                torDict["startDate"] = LastRssSyncDate.Value.ToString("yyyy-MM-dd HH:mm:ss");
            }

            pageableRequests.Add(BuildRequest(body));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var body = BuildSearchBody(searchCriteria.BookQuery, new[] { "title", "author" });
            pageableRequests.Add(BuildRequest(body));

            if (!string.IsNullOrWhiteSpace(searchCriteria.BookIsbn))
            {
                var isbnBody = BuildBaseBody();
                isbnBody["isbn"] = searchCriteria.BookIsbn;
                pageableRequests.Add(BuildRequest(isbnBody));
            }

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var body = BuildSearchBody(searchCriteria.AuthorQuery, new[] { "author" });
            pageableRequests.Add(BuildRequest(body));

            return pageableRequests;
        }

        private Dictionary<string, object> BuildBaseBody()
        {
            var searchTypeName = Settings.SearchType switch
            {
                1 => "all",
                2 => "inactive",
                _ => "active",
            };

            return new Dictionary<string, object>
            {
                {
                    "tor", new Dictionary<string, object>
                    {
                        { "main_cat", BookCategories },
                        { "searchType", searchTypeName },
                        { "sortType", "default" },
                        { "startNumber", 0 },
                    }
                },
                { "perpage", 100 },
            };
        }

        private Dictionary<string, object> BuildSearchBody(string text, string[] searchIn)
        {
            var body = BuildBaseBody();
            var torDict = (Dictionary<string, object>)body["tor"];
            torDict["text"] = text;
            torDict["srchIn"] = searchIn;
            return body;
        }

        private IEnumerable<IndexerRequest> BuildRequest(Dictionary<string, object> body)
        {
            var request = new HttpRequestBuilder(SearchUrl)
            {
                Method = HttpMethod.Post,
                LogResponseContent = true,
            }
            .SetHeader("Content-Type", "application/json")
            .SetCookie("mam_id", Settings.Cookie)
            .Accept(HttpAccept.Json)
            .Build();

            request.SetContent(body.ToJson());
            request.ContentSummary = body.ToJson(Newtonsoft.Json.Formatting.None);

            yield return new IndexerRequest(request);
        }
    }
}
```

> **Note on serialization:** Readarr uses two JSON libraries — `System.Text.Json` for deserialization (via `HttpResponse<T>`) and `Newtonsoft.Json` via `NzbDrone.Common.Serializer.Json` (`.ToJson()` extension method) for serialization. Use `.ToJson()` for building the request body.

**Step 2: Verify it compiles**

```bash
dotnet build src/NzbDrone.Core/Readarr.Core.csproj -p:Platform=Posix
```

If there are issues with `SetCookie`, check `HttpRequestBuilder` — look at `src/NzbDrone.Common/Http/HttpRequestBuilder.cs` for the correct method name for adding cookies.

**Step 3: Commit**

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseRequestGenerator.cs
git commit -m "feat: add MyAnonamouseRequestGenerator for search and RSS polling"
```

---

## Task 6: Create the indexer class (`MyAnonamouse.cs`)

**Files:**
- Create: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouse.cs`

**Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouse : HttpIndexerBase<MyAnonamouseSettings>
    {
        public override string Name => "MyAnonamouse";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override bool SupportsRss => true;
        public override bool SupportsSearch => true;
        public override int PageSize => 100;

        public MyAnonamouse(IHttpClient httpClient,
                            IIndexerStatusService indexerStatusService,
                            IConfigService configService,
                            IParsingService parsingService,
                            Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            var lastSync = _indexerStatusService.GetLastRssSyncReleaseInfo(Definition.Id);

            return new MyAnonamouseRequestGenerator
            {
                Settings = Settings,
                LastRssSyncDate = lastSync?.PublishDate,
            };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new MyAnonamouseParser();
        }

        protected override async Task Test(List<ValidationFailure> failures)
        {
            await base.Test(failures);
        }
    }
}
```

**Step 2: Verify it compiles**

```bash
dotnet build src/NzbDrone.Core/Readarr.Core.csproj -p:Platform=Posix
```

Expected: Build succeeded. If `_indexerStatusService` is not accessible, check `HttpIndexerBase` — the field may be in the base class `IndexerBase` instead. Look at `src/NzbDrone.Core/Indexers/IndexerBase.cs` for the protected field name.

**Step 3: Run all MAM tests — they should now pass**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouseFixture" \
  -p:Platform=Posix
```

Expected: **2 tests pass.**

**Step 4: Commit**

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouse.cs
git commit -m "feat: add MyAnonamouse indexer — native MAM API integration"
```

---

## Task 7: Add an error-response test

**Files:**
- Modify: `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs`
- Create: `src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouseError.json`

### Step 1: Create error fixture

Create `src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouseError.json`:

```json
{
    "error": "Invalid cookie / Not logged in."
}
```

### Step 2: Add failing test

Add this test to `MyAnonamouseFixture.cs` inside the `[TestFixture]` class:

```csharp
[Test]
public async Task should_throw_on_auth_error()
{
    var errorFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouseError.json");

    Mocker.GetMock<IHttpClient>()
        .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
        .Returns<HttpRequest>(r => Task.FromResult(
            new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, errorFeed)));

    Func<Task> act = () => Subject.FetchRecent();
    await act.Should().ThrowAsync<Indexers.Exceptions.IndexerException>()
        .WithMessage("*Invalid cookie*");
}
```

### Step 3: Run to verify it fails

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~should_throw_on_auth_error" \
  -p:Platform=Posix
```

Expected: FAIL — the test throws but it might be wrapped or not thrown. Adjust parser if needed.

### Step 4: Verify the parser throws `IndexerException` on error response (already implemented in Task 4 — just confirm)

The parser already checks `jsonResponse.Resource.Error`. If the test still fails, ensure the error JSON deserializes correctly — the field name `Error` must match the JSON key `error` (case-insensitive via `System.Text.Json`'s default behavior or `JsonPropertyName` attribute).

If needed, add `[JsonPropertyName("error")]` to `MyAnonamouseResponse.Error`.

### Step 5: Run all tests — all 3 should pass

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouseFixture" \
  -p:Platform=Posix
```

Expected: **3 tests pass.**

### Step 6: Commit

```bash
git add src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouseError.json
git add src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs
git commit -m "test: add auth error test for MyAnonamouse parser"
```

---

## Task 8: Run full build and verify StyleCop compliance

**Step 1: Run the full build**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix
```

Expected: 0 errors, 0 warnings. StyleCop errors look like:
- `SA1200: Using directive should appear within a namespace declaration` — move `using` inside namespace
- `SA1309: Field '_foo' should not begin with an underscore` — rename private fields
- `SA1633: File should have header` — not required (disabled in stylecop.json)

Fix any StyleCop violations until the build is clean.

**Step 2: Run all core tests**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj -p:Platform=Posix
```

Expected: All existing tests still pass + 3 new MAM tests pass.

**Step 3: Final commit if any fixes were needed**

```bash
git add -p  # review changes
git commit -m "fix: resolve StyleCop violations in MyAnonamouse indexer"
```

---

## Task 9: Verify the indexer appears in the Readarr UI

This task requires running Readarr. If you have a local instance:

1. Build the full release: `./build.sh --backend`
2. Run Readarr and navigate to Settings > Indexers > Add Indexer
3. Verify "MyAnonamouse" appears in the list
4. Enter a valid `mam_id` cookie and click Test
5. Verify the test succeeds (green checkmark)

If you don't have a running instance, skip this task — the automated tests are sufficient to validate correctness.

---

## Appendix: Common issues

**`HttpRequestBuilder.SetCookie` not found:** Check `src/NzbDrone.Common/Http/HttpRequestBuilder.cs` for the correct cookie-setting method. It may be `.AddCookie(name, value)` or `.SetCookies(dict)`.

**`_indexerStatusService` not accessible in `MyAnonamouse.cs`:** The field is defined in `IndexerBase<T>` as `protected readonly IIndexerStatusService _indexerStatusService`. Check the base class — it should be accessible.

**`System.Text.Json` deserialization not working for snake_case fields:** MAM returns fields like `author_info`, `main_cat`, etc. By default, `System.Text.Json` is case-insensitive for deserialization when using `HttpResponse<T>`. If properties aren't populating, add `[JsonPropertyName("author_info")]` attributes to the DTO properties.

**StyleCop `SA1200` (using outside namespace):** All `using` directives must be outside the namespace block — this is the opposite of the default Roslyn convention. See existing files for the correct pattern.

**Request body serialization:** Use `.ToJson()` from `NzbDrone.Common.Serializer` (Newtonsoft) for the POST body, not `System.Text.Json.JsonSerializer.Serialize()`. Import `using NzbDrone.Common.Serializer;` at the top of the request generator file.
