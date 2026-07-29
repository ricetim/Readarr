# Release Details in Interactive Search — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface MyAnonamouse's narrator, file count, description, series, tags, and category in an expandable row in the interactive search dialog.

**Architecture:** A nullable `ReleaseDetails` sub-object hangs off `ReleaseInfo`, marked `[JsonIgnore]` so it never reaches the `PendingReleases` table. The MAM parser populates it from fields already present in the search response, plus a `description` flag added to the request. The API maps it to a `ReleaseDetailsResource`, and the search table renders a chevron that expands a detail panel — only for releases that have details.

**Tech Stack:** .NET 6 / C# (NzbDrone.Core, Readarr.Api.V1), NUnit + FluentAssertions + Moq, React 17 + CSS Modules.

**Spec:** `docs/superpowers/specs/2026-07-29-release-details-interactive-search-design.md`

---

## Conventions for this codebase

Read these before starting — they cause build failures if ignored.

- **StyleCop is enforced and warnings are errors.** 4-space indent, `using` directives *outside* the namespace, `System.*` usings first.
- **Always build from the solution**, never a single project — `stylecop.json` resolves via `SolutionDir`, and building a project alone produces spurious SA1200 errors.
- **Test command** (build first, then `--no-build`):
  ```bash
  dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
  dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
    --filter "FullyQualifiedName~<FixtureName>" -p:Platform=Posix --no-build
  ```
- **Frontend lint:** `yarn lint`. File names must match the exported name; imports are sort-enforced (`simple-import-sort`); 2-space indent, single quotes.
- **`.css.d.ts` files are generated** by the webpack build ("Please do not change this file!"). Create the `.css`, run `yarn build`, then commit the generated `.d.ts`.

## File Structure

**Create:**
- `src/NzbDrone.Core/Parser/Model/ReleaseDetails.cs` — the display-only detail bag
- `src/NzbDrone.Core/Indexers/MyAnonamouse/BbCodeCleaner.cs` — BBCode → plain text
- `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/BbCodeCleanerFixture.cs`
- `src/Readarr.Api.V1/Indexers/ReleaseDetailsResource.cs`
- `frontend/src/InteractiveSearch/ReleaseDetails.js`
- `frontend/src/InteractiveSearch/ReleaseDetails.css`

**Modify:**
- `src/NzbDrone.Core/Parser/Model/ReleaseInfo.cs` — one `[JsonIgnore]` property
- `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs` — two DTO fields
- `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseRequestGenerator.cs` — `description` flag in `BuildRequest`
- `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs` — populate `Details`
- `src/Readarr.Api.V1/Indexers/ReleaseResource.cs` — `Details` property + mapping
- `src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouse.json` — add `description`
- `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs` — assertions
- `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseRequestGeneratorFixture.cs` — flag assertion
- `frontend/src/InteractiveSearch/InteractiveSearch.js` — expander column
- `frontend/src/InteractiveSearch/InteractiveSearchRow.js` — expander state + detail row
- `frontend/src/InteractiveSearch/InteractiveSearchRow.css` — expander cell style
- `src/NzbDrone.Core/Localization/Core/en.json` — new keys

**Note on the existing fixture:** `MyAnonamouse.json` *already* contains `numfiles`, `narrator_info`, `series_info`, `tags`, and `catname`. Entry 1 has populated values; entry 2 has `"narrator_info": "{}"` and `"series_info": "{}"` — the empty-dict edge case is already covered by the data. Only `description` needs adding.

---

## Chunk 1: Core model and BBCode cleaner

### Task 1: Add the `ReleaseDetails` model

**Files:**
- Create: `src/NzbDrone.Core/Parser/Model/ReleaseDetails.cs`
- Modify: `src/NzbDrone.Core/Parser/Model/ReleaseInfo.cs`

- [ ] **Step 1: Create the model**

`src/NzbDrone.Core/Parser/Model/ReleaseDetails.cs`:

```csharp
using System.Collections.Generic;

namespace NzbDrone.Core.Parser.Model
{
    /// <summary>
    /// Display-only metadata about a release, shown in the interactive search dialog.
    /// Populated only by indexers that return it; null otherwise.
    /// </summary>
    public class ReleaseDetails
    {
        public List<string> Narrators { get; set; }
        public int? FileCount { get; set; }
        public string Description { get; set; }
        public string Series { get; set; }
        public string Tags { get; set; }
        public string Category { get; set; }
    }
}
```

- [ ] **Step 2: Add the property to `ReleaseInfo`**

In `src/NzbDrone.Core/Parser/Model/ReleaseInfo.cs`, directly beneath the existing `IndexerFlags` property, add:

```csharp
        // Display-only; [JsonIgnore] keeps it out of the PendingReleases table,
        // matching how IndexerFlags is handled. ReleaseResourceMapper maps it
        // explicitly from the in-memory object during search.
        [JsonIgnore]
        public ReleaseDetails Details { get; set; }
```

The file already has `using System.Text.Json.Serialization;` — no new using needed.

- [ ] **Step 3: Build**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore
```
Expected: succeeds with no warnings.

- [ ] **Step 4: Commit**

```bash
git add src/NzbDrone.Core/Parser/Model/ReleaseDetails.cs src/NzbDrone.Core/Parser/Model/ReleaseInfo.cs
git commit -m "feat(release): add ReleaseDetails sub-object to ReleaseInfo"
```

---

### Task 2: BBCode cleaner (TDD)

MAM descriptions are tracker-controlled BBCode. We strip to plain text server-side rather than rendering markup — see spec §4 for why.

**Files:**
- Create: `src/NzbDrone.Core/Indexers/MyAnonamouse/BbCodeCleaner.cs`
- Test: `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/BbCodeCleanerFixture.cs`

- [ ] **Step 1: Write the failing tests**

`src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/BbCodeCleanerFixture.cs`:

```csharp
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.MyAnonamouse;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonamouseTests
{
    [TestFixture]
    public class BbCodeCleanerFixture
    {
        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("   ", null)]
        public void should_return_null_for_empty_input(string input, string expected)
        {
            BbCodeCleaner.Strip(input).Should().Be(expected);
        }

        [Test]
        public void should_strip_simple_tags()
        {
            BbCodeCleaner.Strip("[b]Bold[/b] and [i]italic[/i]")
                .Should().Be("Bold and italic");
        }

        [Test]
        public void should_strip_tags_with_attributes()
        {
            BbCodeCleaner.Strip("[size=4][b]Title[/b][/size]")
                .Should().Be("Title");
        }

        [Test]
        public void should_keep_url_text_but_drop_the_tag()
        {
            BbCodeCleaner.Strip("See [url=https://example.com]the site[/url] now")
                .Should().Be("See the site now");
        }

        [Test]
        public void should_remove_img_tags_including_their_content()
        {
            BbCodeCleaner.Strip("Before [img]https://example.com/cover.jpg[/img] after")
                .Should().Be("Before  after");
        }

        [Test]
        public void should_preserve_line_breaks()
        {
            BbCodeCleaner.Strip("Line one\r\n[b]Line two[/b]")
                .Should().Be("Line one\nLine two");
        }

        [Test]
        public void should_collapse_excessive_blank_lines()
        {
            BbCodeCleaner.Strip("One\n\n\n\n\nTwo")
                .Should().Be("One\n\nTwo");
        }

        [Test]
        public void should_decode_html_entities()
        {
            BbCodeCleaner.Strip("Tom &amp; Jerry &#8212; a tale")
                .Should().Be("Tom & Jerry — a tale");
        }

        [Test]
        public void should_strip_list_markup()
        {
            BbCodeCleaner.Strip("[list][*]First[*]Second[/list]")
                .Should().Be("FirstSecond");
        }

        [Test]
        public void should_return_null_when_only_markup_remains()
        {
            BbCodeCleaner.Strip("[img]https://example.com/x.jpg[/img]")
                .Should().BeNull();
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore
```
Expected: **build failure** — `BbCodeCleaner` does not exist. That is the failing state for a compiled language.

- [ ] **Step 3: Implement**

`src/NzbDrone.Core/Indexers/MyAnonamouse/BbCodeCleaner.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~BbCodeCleanerFixture" -p:Platform=Posix --no-build
```
Expected: 12 tests pass (3 TestCases + 9 Tests).

- [ ] **Step 5: Commit**

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/BbCodeCleaner.cs \
        src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/BbCodeCleanerFixture.cs
git commit -m "feat(mam): add BBCode-to-plain-text cleaner for release descriptions"
```

---

## Chunk 2: MAM indexer

### Task 3: Add DTO fields

**Files:**
- Modify: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs`

- [ ] **Step 1: Add the two missing properties**

In `MyAnonamouseTorrent`, after `Filetype`, add:

```csharp
        public string Numfiles { get; set; }
        public string Description { get; set; }
```

**Critical:** property names must match the JSON keys (`numfiles`, `description`) case-insensitively. A previous bug in this indexer — a `Name` property against a `title` key — silently yielded nulls for every release. `Narrator_Info`, `Series_Info`, `Tags`, and `Catname` are already declared and need no change.

- [ ] **Step 2: Build**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore
```
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs
git commit -m "feat(mam): add numfiles and description to torrent DTO"
```

---

### Task 4: Request the description field (TDD)

**Files:**
- Modify: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseRequestGenerator.cs`
- Test: `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseRequestGeneratorFixture.cs`

There are **four** request bodies (recent, book search, ISBN search, author search), all funnelled through the private `BuildRequest`. Add the flag there — one change instead of four, and no way to forget one later.

- [ ] **Step 1: Write the failing test**

Read the existing fixture first to match its setup and naming. Add:

```csharp
        [Test]
        public void should_request_description_field()
        {
            var request = Subject.GetRecentRequests().GetAllTiers().First().First();
            var body = request.HttpRequest.ContentSummary;

            body.Should().Contain("\"description\"");
        }
```

If `ContentSummary` is unavailable on `HttpRequest` in this version, use
`System.Text.Encoding.UTF8.GetString(request.HttpRequest.ContentData)` instead. Verify which
is available before writing the assertion — do not guess.

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouseRequestGeneratorFixture" -p:Platform=Posix --no-build
```
Expected: FAIL — body does not contain `"description"`.

- [ ] **Step 3: Implement**

In `BuildRequest`, before serialising:

```csharp
        private IndexerRequest BuildRequest(Dictionary<string, object> body)
        {
            // MAM only returns descriptions when this flag is present on the request.
            // It is request-level, not per-release: all results carry it, or none do.
            body["description"] = string.Empty;

            var httpRequest = new HttpRequestBuilder(SearchUrl)
                .Accept(HttpAccept.Json)
                .Build();
            // ... rest unchanged
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Same command as Step 2. Expected: PASS, and all pre-existing request-generator tests still green.

- [ ] **Step 5: Verify against the live API** ⚠️ **Open risk from the spec**

MAM's docs show `&description` bare in form-encoded requests but do not specify the JSON
equivalent. Confirm a real search returns a populated `description` field:

```bash
curl -s -X POST 'https://www.myanonamouse.net/tor/js/loadSearchJSONbasic.php' \
  -H 'Content-Type: application/json' \
  -b "mam_id=<YOUR_COOKIE>" \
  -d '{"tor":{"main_cat":[13],"searchType":"all","sortType":"dateDesc","startNumber":0},"perpage":2,"description":""}' \
  | python3 -m json.tool | head -40
```

If `description` is absent from the response, try `true` instead of `""`. If neither works,
that parameter must be form-encoded — fall back to appending `?description` to `SearchUrl`
and note the deviation in the commit message.

- [ ] **Step 6: Commit**

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseRequestGenerator.cs \
        src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseRequestGeneratorFixture.cs
git commit -m "feat(mam): request description field on all search requests"
```

---

### Task 5: Populate `ReleaseDetails` in the parser (TDD)

**Files:**
- Modify: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs`
- Modify: `src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouse.json`
- Test: `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs`

- [ ] **Step 1: Add `description` to the fixture**

In `MyAnonamouse.json`, add to the **first** `data` entry only (leaving entry 2 without one
exercises the absent-description path):

```json
  "description": "[size=4][b]Love at Stake - Books 1-16[/b][/size]\r\n\r\nWelcome to the world of modern day vampires.[img]https://example.com/cover.jpg[/img]",
```

- [ ] **Step 2: Write the failing tests**

Add to `MyAnonamouseFixture.cs`:

```csharp
        [Test]
        public async Task should_parse_release_details()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();
            var details = releases[0].Details;

            details.Should().NotBeNull();
            details.Narrators.Should().BeEquivalentTo("Abby Craden");
            details.FileCount.Should().Be(149);
            details.Series.Should().Be("Love at Stake (01-16)");
            details.Tags.Should().Be("unabridged mp3");
            details.Category.Should().Be("Audiobooks - Urban Fantasy");
            details.Description.Should().Be("Love at Stake - Books 1-16\n\nWelcome to the world of modern day vampires.");
        }

        [Test]
        public async Task should_handle_empty_narrator_and_series_objects()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            // Entry 2 has "narrator_info": "{}", "series_info": "{}" and no description,
            // but still has numfiles/tags/catname, so Details is present but sparse.
            var details = releases[1].Details;

            details.Should().NotBeNull();
            details.Narrators.Should().BeEmpty();
            details.Series.Should().BeNull();
            details.Description.Should().BeNull();
            details.FileCount.Should().Be(1);
        }
```

**Note on ordering:** the parser returns results sorted by `PublishDate` descending. Entry 1
(2023-04-01) sorts before entry 2 (2023-03-15), so index 0/1 match the fixture order here.
The existing `should_parse_recent_feed_from_mam` test already relies on this.

- [ ] **Step 3: Run to verify they fail**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouseFixture" -p:Platform=Posix --no-build
```
Expected: FAIL — `Details` is null.

- [ ] **Step 4: Implement**

In `MyAnonamouseParser.cs`, replace `ParseAuthorInfo` with a general helper and add a series
parser. `ParseAuthorInfo` becomes a thin wrapper so existing behaviour is untouched:

```csharp
        private static string ParseAuthorInfo(string authorInfo)
        {
            return ParseNamePairs(authorInfo).FirstOrDefault() ?? string.Empty;
        }

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
            var description = BbCodeCleaner.Strip(torrent.Description);
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
```

Then in the `foreach`, add `Details = BuildDetails(torrent),` to the `MyAnonamouseInfo`
initialiser (after `Languages = languages`).

`System.Linq`, `System.Collections.Generic`, `System.Text.Json`, and
`NzbDrone.Common.Extensions` are all already imported.

- [ ] **Step 5: Run tests to verify they pass**

Same command as Step 3. Expected: PASS, **and all 17 pre-existing MAM tests still green**.

- [ ] **Step 6: Commit**

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs \
        src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouse.json \
        src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs
git commit -m "feat(mam): populate ReleaseDetails from narrator, series, tags and description"
```

---

## Chunk 3: API layer

### Task 6: Expose `Details` on `ReleaseResource`

**Files:**
- Create: `src/Readarr.Api.V1/Indexers/ReleaseDetailsResource.cs`
- Modify: `src/Readarr.Api.V1/Indexers/ReleaseResource.cs`

- [ ] **Step 1: Create the resource**

```csharp
using System.Collections.Generic;

namespace Readarr.Api.V1.Indexers
{
    public class ReleaseDetailsResource
    {
        public List<string> Narrators { get; set; }
        public int? FileCount { get; set; }
        public string Description { get; set; }
        public string Series { get; set; }
        public string Tags { get; set; }
        public string Category { get; set; }
    }
}
```

- [ ] **Step 2: Add the property**

In `ReleaseResource`, after `Languages`:

```csharp
        public ReleaseDetailsResource Details { get; set; }
```

- [ ] **Step 3: Map it in `ToResource`**

After the `Languages = ...` mapping:

```csharp
                Details = releaseInfo.Details == null ? null : new ReleaseDetailsResource
                {
                    Narrators = releaseInfo.Details.Narrators,
                    FileCount = releaseInfo.Details.FileCount,
                    Description = releaseInfo.Details.Description,
                    Series = releaseInfo.Details.Series,
                    Tags = releaseInfo.Details.Tags,
                    Category = releaseInfo.Details.Category
                },
```

Leave `ToModel` untouched — `Details` is display-only and the grab path does not need it.

- [ ] **Step 4: Build and run the full indexer test suite**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~IndexerTests" -p:Platform=Posix --no-build
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Readarr.Api.V1/Indexers/
git commit -m "feat(api): expose release details on ReleaseResource"
```

---

## Chunk 4: Frontend

### Task 7: Translation keys

**Files:**
- Modify: `src/NzbDrone.Core/Localization/Core/en.json`

- [ ] **Step 1: Add keys in alphabetical position**

```json
  "Category": "Category",
  "Files": "Files",
  "Narrator": "Narrator",
  "ReleaseDetails": "Release Details",
  "ShowLess": "Show Less",
  "ShowMore": "Show More",
```

`Description`, `Series`, and `Tags` may already exist — **grep before adding** to avoid a
duplicate key:

```bash
grep -n '"Description"\|"Series"\|"Tags"' src/NzbDrone.Core/Localization/Core/en.json
```

- [ ] **Step 2: Commit**

```bash
git add src/NzbDrone.Core/Localization/Core/en.json
git commit -m "feat(i18n): add release detail labels"
```

---

### Task 8: `ReleaseDetails` component

**Files:**
- Create: `frontend/src/InteractiveSearch/ReleaseDetails.js`
- Create: `frontend/src/InteractiveSearch/ReleaseDetails.css`

- [ ] **Step 1: Create the CSS**

```css
.details {
  padding: 12px 20px;
  background-color: var(--tableRowHoverBackgroundColor);
}

.grid {
  display: grid;
  grid-template-columns: 90px 1fr;
  gap: 4px 12px;
}

.label {
  color: var(--dimColor);
  font-weight: bold;
}

.description {
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px solid var(--borderColor);
  white-space: pre-wrap;
  word-break: break-word;
}

.clamped {
  display: -webkit-box;
  overflow: hidden;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
}

.toggle {
  margin-top: 4px;
}
```

- [ ] **Step 2: Create the component**

```jsx
import PropTypes from 'prop-types';
import React, { useCallback, useState } from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import styles from './ReleaseDetails.css';

function ReleaseDetails(props) {
  const {
    narrators,
    fileCount,
    description,
    series,
    tags,
    category
  } = props;

  const [isDescriptionExpanded, setIsDescriptionExpanded] = useState(false);

  const onToggleDescription = useCallback(() => {
    setIsDescriptionExpanded((value) => !value);
  }, []);

  const rows = [];

  if (narrators && narrators.length > 0) {
    rows.push([translate('Narrator'), narrators.join(', ')]);
  }

  if (fileCount != null) {
    rows.push([translate('Files'), `${fileCount}`]);
  }

  if (series) {
    rows.push([translate('Series'), series]);
  }

  if (category) {
    rows.push([translate('Category'), category]);
  }

  if (tags) {
    rows.push([translate('Tags'), tags]);
  }

  return (
    <div className={styles.details}>
      <div className={styles.grid}>
        {
          rows.map(([label, value]) => {
            return [
              <div key={`${label}-label`} className={styles.label}>
                {label}
              </div>,
              <div key={`${label}-value`}>
                {value}
              </div>
            ];
          })
        }
      </div>

      {
        description ?
          <div className={styles.description}>
            <div className={isDescriptionExpanded ? undefined : styles.clamped}>
              {description}
            </div>

            <Link
              className={styles.toggle}
              onPress={onToggleDescription}
            >
              {isDescriptionExpanded ? translate('ShowLess') : translate('ShowMore')}
            </Link>
          </div> :
          null
      }
    </div>
  );
}

ReleaseDetails.propTypes = {
  narrators: PropTypes.arrayOf(PropTypes.string),
  fileCount: PropTypes.number,
  description: PropTypes.string,
  series: PropTypes.string,
  tags: PropTypes.string,
  category: PropTypes.string
};

export default ReleaseDetails;
```

- [ ] **Step 3: Build to generate the `.css.d.ts`, then lint**

```bash
yarn build && yarn lint
```
Expected: build succeeds, `ReleaseDetails.css.d.ts` appears, lint clean.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/InteractiveSearch/ReleaseDetails.js \
        frontend/src/InteractiveSearch/ReleaseDetails.css \
        frontend/src/InteractiveSearch/ReleaseDetails.css.d.ts
git commit -m "feat(ui): add ReleaseDetails panel component"
```

---

### Task 9: Wire the expander into the search table

**Files:**
- Modify: `frontend/src/InteractiveSearch/InteractiveSearch.js`
- Modify: `frontend/src/InteractiveSearch/InteractiveSearchRow.js`
- Modify: `frontend/src/InteractiveSearch/InteractiveSearchRow.css`

- [ ] **Step 1: Add the expander column**

In `InteractiveSearch.js`, prepend to the `columns` array (before `protocol`):

```javascript
  {
    name: 'expander',
    label: '',
    isSortable: false,
    isVisible: true
  },
```

- [ ] **Step 2: Add the expander cell style**

In `InteractiveSearchRow.css`:

```css
.expander {
  composes: cell;

  width: 30px;
  cursor: pointer;
}

.detailsRow {
  background-color: var(--tableRowHoverBackgroundColor);
}
```

- [ ] **Step 3: Render the chevron and detail row**

In `InteractiveSearchRow.js`:

Add `details` to the destructured props and to `propTypes`:

```javascript
  details: PropTypes.shape({
    narrators: PropTypes.arrayOf(PropTypes.string),
    fileCount: PropTypes.number,
    description: PropTypes.string,
    series: PropTypes.string,
    tags: PropTypes.string,
    category: PropTypes.string
  }),
```

Add `isExpanded: false` to the constructor's `this.state`, and a handler beside the
existing grab handlers:

```javascript
  onExpandPress = () => {
    this.setState({ isExpanded: !this.state.isExpanded });
  };
```

Wrap the return in a fragment and add the expander cell as the **first** `TableRowCell`:

```jsx
    const hasDetails = !!details;

    return (
      <>
        <TableRow>
          <TableRowCell className={styles.expander}>
            {
              hasDetails ?
                <Icon
                  name={this.state.isExpanded ? icons.COLLAPSE : icons.EXPAND}
                  onClick={this.onExpandPress}
                  title={this.state.isExpanded ? translate('ShowLess') : translate('ReleaseDetails')}
                /> :
                null
            }
          </TableRowCell>

          {/* ...all existing cells unchanged... */}
        </TableRow>

        {
          hasDetails && this.state.isExpanded ?
            <TableRow className={styles.detailsRow}>
              <TableRowCell colSpan={13}>
                <ReleaseDetails {...details} />
              </TableRowCell>
            </TableRow> :
            null
        }
      </>
    );
```

Move the existing `<ConfirmModal>` inside the first `<TableRow>` where it already lives —
do not relocate it.

Add the import (respecting `simple-import-sort` — it belongs with the other relative
imports, after `./Peers`):

```javascript
import ReleaseDetails from './ReleaseDetails';
```

**`colSpan` must equal the total column count — use `13`.** The `columns` array currently has
12 entries (protocol, age, title, indexer, size, peers, qualityWeight, customFormatScore,
indexerFlags, language, rejections, releaseWeight); the expander makes 13. The rendered row
matches: 12 existing `TableRowCell`s (the last being the download button, which the
`releaseWeight` column heads) plus the new expander cell.

Re-count in the file before committing — a wrong `colSpan` breaks the table layout silently
rather than erroring.

- [ ] **Step 4: Lint and build**

```bash
yarn lint && yarn build
```
Expected: both clean.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/InteractiveSearch/
git commit -m "feat(ui): add expandable release details row to interactive search"
```

---

## Chunk 5: Verification

### Task 10: End-to-end verification

- [ ] **Step 1: Full backend test run**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~IndexerTests" -p:Platform=Posix --no-build
```
Expected: all green, including the 17 pre-existing MAM tests.

- [ ] **Step 2: Full frontend checks**

```bash
yarn lint && yarn stylelint-linux && yarn build
```

- [ ] **Step 3: Verify against the real tracker**

Rebuild the Docker image (it bundles the frontend, which a plain `dotnet build` does not),
run a manual search for a known audiobook, and confirm:

- the chevron appears on MAM releases and **not** on releases from other indexers
- expanding shows narrator, file count, series, category, tags
- the description renders as plain text with no visible `[b]` or `[img]` markup
- collapsing and re-expanding works, and Show More/Show Less toggles the clamp

```bash
docker compose up -d --build
```

- [ ] **Step 4: Confirm the PendingReleases table stayed clean**

The whole point of `[JsonIgnore]`. After a delayed grab has created a pending release:

```bash
sqlite3 /config/readarr.db "SELECT Release FROM PendingReleases LIMIT 1;" | python3 -m json.tool | grep -i -c description
```
Expected: `0` — no description persisted.

- [ ] **Step 5: Update the spec status**

Change `**Status:** Approved` to `**Status:** Implemented` in
`docs/superpowers/specs/2026-07-29-release-details-interactive-search-design.md`, then:

```bash
git add docs/superpowers/specs/2026-07-29-release-details-interactive-search-design.md
git commit -m "docs: mark release details spec as implemented"
```

---

## Deferred

- **Bibliotik details.** Requires a per-torrent detail-page fetch. `ReleaseDetails` is the
  extension point — populate it in `BibliotikParser` behind a setting, and the API and UI
  need no changes.
- **Sorting/filtering on the new fields.** Would mean adding them to `filterBuilderProps` in
  `frontend/src/Store/Actions/releaseActions.js` and making the columns real. Not requested.
