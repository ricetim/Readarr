# MAM Language Tag Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Parse the MAM `"language"` field into `ReleaseInfo.Languages`, surface it in the manual grab UI, and add a per-profile `AllowedLanguages` filter to the decision engine.

**Architecture:** Three independent layers — (1) MAM parser maps numeric IDs to `Language` constants; (2) `ReleaseResource` exposes `Languages` so the UI can display it; (3) `QualityProfile.AllowedLanguages` + a new decision spec enforce the filter during automatic grabbing. Each layer is independently testable.

**Tech Stack:** .NET 6 / C# (NUnit, FluentAssertions, NBuilder), React / Redux / TypeScript (ESLint, Prettier), FluentMigrator for DB schema change.

---

## Chunk 1: Backend — MAM parsing and ReleaseResource

### Task 1: Add `Language` field to `MyAnonamouseTorrent` and parse it in the parser

**Files:**
- Modify: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs`
- Modify: `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs`
- Modify: `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs`
- Reference (no change): `src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouse.json` — already has `"language": "1"` on both entries

**Background**

`MyAnonamouseTorrent` is a plain DTO that Newtonsoft deserialises from the MAM JSON response. It currently has no `Language` property, so MAM's `"language"` field is silently dropped. The parser (`MyAnonamouseParser.ParseResponse()`) iterates over the deserialized list and constructs `MyAnonamouseInfo` (which extends `TorrentInfo` → `ReleaseInfo`). `ReleaseInfo.Languages` is a `List<Language>` already initialised to an empty list in the constructor.

**Important:** MAM language IDs are unrelated to Readarr's `Language.Id` values. The mapping must use named static properties (`Language.English`, `Language.Dutch`, etc.) — never `Language.FindById(mamId)`.

- [ ] **Step 1: Write the failing test**

Add two new test methods to `MyAnonamouseFixture`. The fixture JSON already has `"language": "1"` (English) on both entries.

```csharp
[Test]
public async Task should_parse_language_on_audiobook_release()
{
    var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

    Mocker.GetMock<IHttpClient>()
        .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
        .Returns<HttpRequest>(r => Task.FromResult(
            new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

    var releases = await Subject.FetchRecent();

    var release = (MyAnonamouseInfo)releases[0];
    release.Languages.Should().ContainSingle()
        .Which.Should().Be(NzbDrone.Core.Languages.Language.English);
}

[Test]
public async Task should_parse_language_on_ebook_release()
{
    var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

    Mocker.GetMock<IHttpClient>()
        .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
        .Returns<HttpRequest>(r => Task.FromResult(
            new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

    var releases = await Subject.FetchRecent();

    var release = (MyAnonamouseInfo)releases[1];
    release.Languages.Should().ContainSingle()
        .Which.Should().Be(NzbDrone.Core.Languages.Language.English);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouseFixture" \
  -p:Platform=Posix --no-build
```

Expected: the two new tests fail with `Expected collection to contain a single item, but found 0 item(s)`.

- [ ] **Step 3: Add `Language` property to `MyAnonamouseTorrent`**

In `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs`, add one property to `MyAnonamouseTorrent`:

```csharp
public string Language { get; set; }
```

Place it after the existing `Id` property. Newtonsoft will deserialize the `"language"` JSON key into it automatically.

- [ ] **Step 4: Add the language mapping dictionary and populate `Languages` in the parser**

In `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs`:

Add a `using NzbDrone.Core.Languages;` directive at the top (alongside existing usings).

Add this static field to the `MyAnonamouseParser` class (before `ParseResponse`):

```csharp
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
```

IDs 8 (Latin) and 14 (Other) are intentionally absent — unrecognised IDs leave `Languages` empty. The spec's mapping table says "unrecognised → `Language.Unknown`" but the spec's Key Invariants section says "Unknown treated as no language data — left out of Languages list." This plan follows the Key Invariants: an unknown language ID produces an empty `Languages` list, which is treated as "no data" and passes all language filters. This is safer than injecting `Language.Unknown` into the list.

In the `foreach (var torrent in jsonResponse.Resource.Data)` loop, after the `IndexerFlags` block and before the `var authorName =` line, add:

```csharp
var languages = new List<Language>();
if (torrent.Language != null &&
    MamLanguageMap.TryGetValue(torrent.Language, out var parsedLanguage))
{
    languages.Add(parsedLanguage);
}
```

Then in the `torrentInfos.Add(new MyAnonamouseInfo { ... })` initialiser, add:

```csharp
Languages = languages,
```

- [ ] **Step 5: Run tests to confirm they pass**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouseFixture" \
  -p:Platform=Posix --no-build
```

Expected: all tests pass (including the two pre-existing ones).

- [ ] **Step 6: Commit**

```bash
git add src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs \
        src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs \
        src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs
git commit -m "feat(mam): parse language field and populate ReleaseInfo.Languages"
```

---

### Task 2: Expose `Languages` in `ReleaseResource`

**Files:**
- Modify: `src/Readarr.Api.V1/Indexers/ReleaseResource.cs`

**Background**

`ReleaseResource` is the DTO the API sends to the frontend for the manual grab table. It currently has no `Languages` field, so even though `ReleaseInfo.Languages` is now populated, the UI never sees it. `LanguageResource` (`{ Id, Name }`) is the existing API shape used by the `/api/v1/language` endpoint.

`ReleaseResourceMapper` has two static methods:
- `ToResource(DownloadDecision)` — used when serving results to the UI
- `ToModel(ReleaseResource)` — used when the user manually grabs a displayed release; must round-trip `Languages` so the decision engine can re-evaluate it

No extension method helper for `Language → LanguageResource` exists yet; the conversion is done inline in `LanguageController`. Do the same inline here.

- [ ] **Step 1: Add `Languages` property to `ReleaseResource`**

In `src/Readarr.Api.V1/Indexers/ReleaseResource.cs`, add the using:

```csharp
using NzbDrone.Core.Languages;
using Readarr.Api.V1.Languages;
```

Add this property to `ReleaseResource` (after `IndexerFlags`):

```csharp
public List<LanguageResource> Languages { get; set; }
```

- [ ] **Step 2: Map `Languages` in `ToResource()`**

In `ReleaseResourceMapper.ToResource()`, add inside the `return new ReleaseResource { ... }` initialiser (after `IndexerFlags = (int)indexerFlags,`):

```csharp
Languages = releaseInfo.Languages
    .Select(l => new LanguageResource { Id = (int)l, Name = l.ToString() })
    .ToList(),
```

- [ ] **Step 3: Map `Languages` in `ToModel()`**

In `ReleaseResourceMapper.ToModel()`, after `model.PublishDate = ...`, add:

```csharp
model.Languages = resource.Languages?
    .Where(r => Language.All.Any(l => l.Id == r.Id))
    .Select(r => (Language)r.Id)
    .ToList() ?? new List<Language>();
```

The `.Where` guard prevents `ArgumentException` from `(Language)r.Id` if a client sends an unrecognised language ID. Only IDs in `Language.All` pass through.

- [ ] **Step 4: Build to confirm no compilation errors**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/Readarr.Api.V1/Indexers/ReleaseResource.cs
git commit -m "feat(api): expose Languages on ReleaseResource for manual grab UI"
```

---

## Chunk 2: Backend — QualityProfile, migration, and decision spec

### Task 3: Add `AllowedLanguages` to `QualityProfile` with DB migration

**Files:**
- Modify: `src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs`
- Create: `src/NzbDrone.Core/Datastore/Migration/043_quality_profile_allowed_languages.cs`
- Modify: `src/Readarr.Api.V1/Profiles/Quality/QualityProfileResource.cs`

**Background**

`QualityProfile` is a Dapper-mapped model. `List<Language>` is auto-registered by `TableMapping.RegisterEmbeddedConverter()` because `Language` implements `IEmbeddedDocument` — no manual handler registration is needed. The migration uses FluentMigrator and follows the pattern from `040_add_indexer_flags.cs`. The API resource mapper is in `QualityProfileResource.cs` in static `ProfileResourceMapper`.

The `QualityProfileSchemaController` needs no direct change — it calls `_qualityProfileService.GetDefaultProfile(string.Empty).ToResource()`, which will return `[]` for `AllowedLanguages` automatically once the model is initialised correctly.

- [ ] **Step 1: Add `AllowedLanguages` to `QualityProfile`**

In `src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs`, add `using NzbDrone.Core.Languages;` if not present. Add the property:

```csharp
public List<Language> AllowedLanguages { get; set; }
```

In the constructor, initialise it:

```csharp
public QualityProfile()
{
    FormatItems = new List<ProfileFormatItem>();
    AllowedLanguages = new List<Language>();
}
```

- [ ] **Step 2: Create migration 043**

Create `src/NzbDrone.Core/Datastore/Migration/043_quality_profile_allowed_languages.cs`:

```csharp
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(043)]
    public class quality_profile_allowed_languages : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QualityProfiles").AddColumn("AllowedLanguages").AsString().WithDefaultValue("[]");
        }
    }
}
```

- [ ] **Step 3: Expose `AllowedLanguages` in `QualityProfileResource`**

In `src/Readarr.Api.V1/Profiles/Quality/QualityProfileResource.cs`:

Add usings:
```csharp
using NzbDrone.Core.Languages;
using Readarr.Api.V1.Languages;
```

Add property to `QualityProfileResource`:
```csharp
public List<LanguageResource> AllowedLanguages { get; set; }
```

In `ProfileResourceMapper.ToResource(QualityProfile model)`, add to the return initialiser:
```csharp
AllowedLanguages = model.AllowedLanguages
    .Select(l => new LanguageResource { Id = (int)l, Name = l.ToString() })
    .ToList(),
```

In `ProfileResourceMapper.ToModel(QualityProfileResource resource)`, add to the return initialiser:
```csharp
AllowedLanguages = resource.AllowedLanguages?
    .Where(r => Language.All.Any(l => l.Id == r.Id))
    .Select(r => (Language)r.Id)
    .ToList() ?? new List<Language>(),
```

The `.Where` guard mirrors the pattern in `ReleaseResource.ToModel()` — it prevents `ArgumentException` from the explicit cast if a client sends an unrecognised language ID.

- [ ] **Step 4: Build to confirm no errors**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs \
        src/NzbDrone.Core/Datastore/Migration/043_quality_profile_allowed_languages.cs \
        src/Readarr.Api.V1/Profiles/Quality/QualityProfileResource.cs
git commit -m "feat(profiles): add AllowedLanguages to QualityProfile with migration 043"
```

---

### Task 4: Add `LanguageAllowedByProfileSpecification`

**Files:**
- Create: `src/NzbDrone.Core/DecisionEngine/Specifications/LanguageAllowedByProfileSpecification.cs`
- Create: `src/NzbDrone.Core.Test/DecisionEngineTests/LanguageAllowedByProfileSpecificationFixture.cs`

**Background**

The decision engine auto-discovers all `IDecisionEngineSpecification` implementations via DryIoc — no registration is needed. The spec follows the same pattern as `QualityAllowedByProfileSpecification`. It applies the three-way rule: accept if profile has no language preference, accept if release has no language data, otherwise reject if no intersection. Study `CustomFormatAllowedByProfileSpecificationFixture.cs` for the test scaffolding pattern.

- [ ] **Step 1: Write the failing tests**

Create `src/NzbDrone.Core.Test/DecisionEngineTests/LanguageAllowedByProfileSpecificationFixture.cs`:

```csharp
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class LanguageAllowedByProfileSpecificationFixture : CoreTest<LanguageAllowedByProfileSpecification>
    {
        private RemoteBook _remoteBook;

        [SetUp]
        public void Setup()
        {
            var fakeAuthor = Builder<Author>.CreateNew()
                .With(c => c.QualityProfile = new QualityProfile
                {
                    Cutoff = Quality.EPUB.Id,
                    AllowedLanguages = new List<Language>()
                })
                .Build();

            _remoteBook = new RemoteBook
            {
                Author = fakeAuthor,
                Release = new ReleaseInfo { Languages = new List<Language>() },
                ParsedBookInfo = new ParsedBookInfo
                {
                    Quality = new QualityModel(Quality.EPUB)
                }
            };
        }

        [Test]
        public void should_accept_when_profile_allowed_languages_is_empty()
        {
            // Empty = no preference = accept anything
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language>();
            _remoteBook.Release.Languages = new List<Language> { Language.German };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_release_languages_is_empty()
        {
            // Indexer provides no language data — don't punish it
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language> { Language.English };
            _remoteBook.Release.Languages = new List<Language>();

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_release_language_matches_profile()
        {
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language> { Language.English, Language.French };
            _remoteBook.Release.Languages = new List<Language> { Language.French };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_release_language_not_in_profile()
        {
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language> { Language.English };
            _remoteBook.Release.Languages = new List<Language> { Language.German };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_when_no_release_language_intersects_profile()
        {
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language> { Language.English, Language.French };
            _remoteBook.Release.Languages = new List<Language> { Language.German, Language.Spanish };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~LanguageAllowedByProfileSpecification" \
  -p:Platform=Posix --no-build
```

Expected: all 5 tests fail with a compile error (class doesn't exist yet).

- [ ] **Step 3: Implement the specification**

Create `src/NzbDrone.Core/DecisionEngine/Specifications/LanguageAllowedByProfileSpecification.cs`:

```csharp
using System.Linq;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class LanguageAllowedByProfileSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public LanguageAllowedByProfileSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var allowedLanguages = subject.Author.QualityProfile.Value.AllowedLanguages;
            var releaseLanguages = subject.Release.Languages;

            if (!allowedLanguages.Any())
            {
                _logger.Debug("Profile has no language restrictions, accepting");
                return Decision.Accept();
            }

            if (!releaseLanguages.Any())
            {
                _logger.Debug("Release has no language information, accepting");
                return Decision.Accept();
            }

            if (!releaseLanguages.Any(l => allowedLanguages.Contains(l)))
            {
                var names = string.Join(", ", releaseLanguages.Select(l => l.Name));
                _logger.Debug("Release language(s) [{0}] not in profile allowed languages", names);
                return Decision.Reject("Language {0} is not allowed by profile", names);
            }

            return Decision.Accept();
        }
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~LanguageAllowedByProfileSpecification" \
  -p:Platform=Posix --no-build
```

Expected: all 5 tests pass.

- [ ] **Step 5: Run the full MAM test suite to catch regressions**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~MyAnonamouse" \
  -p:Platform=Posix --no-build
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/NzbDrone.Core/DecisionEngine/Specifications/LanguageAllowedByProfileSpecification.cs \
        src/NzbDrone.Core.Test/DecisionEngineTests/LanguageAllowedByProfileSpecificationFixture.cs
git commit -m "feat(decision): add LanguageAllowedByProfileSpecification"
```

---

## Chunk 3: Frontend

### Task 5: Add Language column to the interactive search release table

**Files:**
- Modify: `frontend/src/InteractiveSearch/InteractiveSearch.js` — add column definition
- Modify: `frontend/src/InteractiveSearch/InteractiveSearchRow.js` — render language cell

**Background**

The interactive search table is driven by a `columns` array in `InteractiveSearch.js`. Each column entry maps to a `<TableRowCell>` in `InteractiveSearchRow.js`. Props flow from the API release objects (spread via `{...item}`) into `InteractiveSearchRow`. The API now returns `languages: [{ id, name }]` on each release.

The language cell should show the first language's name, or a dash if the array is empty. Insert the new column between `indexerFlags` and `rejections`.

- [ ] **Step 1: Add the column definition in `InteractiveSearch.js`**

In the `columns` array, insert a new entry after the `indexerFlags` entry (line ~65) and before `rejections`:

```js
{
  name: 'language',
  label: 'Language',
  isSortable: false,
  isVisible: true
},
```

- [ ] **Step 2: Add the `languages` prop and render the cell in `InteractiveSearchRow.js`**

In the `render()` method's destructured props block, add `languages = []` alongside the existing props:

```js
languages = [],
```

Add a new `<TableRowCell>` after the `indexerFlags` cell (around line 207) and before the `rejected` cell:

```jsx
<TableRowCell className={styles.language}>
  {languages.length > 0 ? languages[0].name : '-'}
</TableRowCell>
```

Also add a `.language` CSS class to `InteractiveSearchRow.css` (even a minimal one) so `styles.language` resolves. For a width hint matching the other narrow columns, add:

```css
.language {
  width: 70px;
}
```

Update `InteractiveSearchRow.css.d.ts` to include the declaration:

```ts
export const language: string;
```

Add `InteractiveSearchRow.css` and `InteractiveSearchRow.css.d.ts` to the files list for this task.

Add `languages` to the `propTypes` definition:

```js
languages: PropTypes.arrayOf(PropTypes.shape({
  id: PropTypes.number.isRequired,
  name: PropTypes.string.isRequired
})),
```

Add a `defaultProps` entry:

```js
languages: [],
```

- [ ] **Step 3: Lint to confirm no JS errors**

```bash
yarn lint
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/InteractiveSearch/InteractiveSearch.js \
        frontend/src/InteractiveSearch/InteractiveSearchRow.js
git commit -m "feat(ui): add Language column to interactive search release table"
```

---

### Task 6: Add language multi-select to quality profile editor

**Files:**
- Create: `frontend/src/Components/Form/LanguageSelectInput.tsx`
- Modify: `frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContent.js`
- Modify: `frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContentConnector.js`

**Background**

The quality profile modal uses Redux form state. The form item is managed in the reducer; `item.allowedLanguages` will be a form field containing the current value array. `EditQualityProfileModalContentConnector.js` maps Redux state to the component's `item` prop.

`LanguageSelectInput` follows the pattern of `IndexerFlagsSelectInput.tsx` — it wraps `EnhancedSelectInput` and reads from `state.settings.languages.items` (already populated by `FETCH_LANGUAGES` / `/api/v1/language`). The key difference: the value is an array of `{ id, name }` objects rather than a bitmask.

You will need to verify that `fetchLanguages` is dispatched in the connector before the modal renders. Check `EditQualityProfileModalContentConnector.js` — if it does not already call `fetchLanguages`, add it to `mapDispatchToProps` and call it in `componentDidMount` (or the equivalent lifecycle).

- [ ] **Step 1: Add `languages` to `SettingsAppState.ts`, then create `LanguageSelectInput.tsx`**

**1a — Update `frontend/src/App/State/SettingsAppState.ts`**

Open the file and look for how other settings slices are typed (e.g., `indexerFlags: IndexerFlagSettingsAppState`). Add a `languages` slice of type `AppSectionState<Language>` where `Language` matches the API response shape `{ id: number; name: string; nameLower: string }`. Follow the same pattern used for other simple list settings.

At minimum, add:
```ts
languages: AppSectionState<{ id: number; name: string; nameLower: string }>;
```

**1b — Create `frontend/src/Components/Form/LanguageSelectInput.tsx`**

```tsx
import React, { useCallback } from 'react';
import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import EnhancedSelectInput from './EnhancedSelectInput';

interface Language {
  id: number;
  name: string;
}

const selectLanguageValues = (selectedLanguages: Language[]) =>
  createSelector(
    (state: AppState) => state.settings.languages,
    (languages) => {
      const value = selectedLanguages.map(({ id }) => id);
      const values = languages.items.map(({ id, name }: Language) => ({
        key: id,
        value: name,
      }));
      return { value, values };
    }
  );

interface LanguageSelectInputProps {
  name: string;
  value: Language[];
  onChange(payload: { name: string; value: Language[] }): void;
}

function LanguageSelectInput(props: LanguageSelectInputProps) {
  const { value = [], onChange } = props;
  const { value: selectedIds, values } = useSelector(
    selectLanguageValues(value)
  );

  const onChangeWrapper = useCallback(
    ({ name, value: selectedIdList }: { name: string; value: number[] }) => {
      const languages = selectedIdList.map((id) => ({
        id,
        name: values.find((v) => v.key === id)?.value ?? '',
      }));
      onChange({ name, value: languages });
    },
    [onChange, values]
  );

  return (
    <EnhancedSelectInput
      {...props}
      value={selectedIds}
      values={values}
      onChange={onChangeWrapper}
    />
  );
}

export default LanguageSelectInput;
```

- [ ] **Step 2: Add the language field to `EditQualityProfileModalContent.js`**

Add to the imports at the top:

```js
import LanguageSelectInput from 'Components/Form/LanguageSelectInput';
```

In the `render()` method's destructured `item` block, add:

```js
allowedLanguages,
```

After the `cutoffFormatScore` `FormGroup` block (around line 237, before the closing `</div>` of `formGroupWrapper`), add:

```jsx
<FormGroup size={sizes.EXTRA_SMALL}>
  <FormLabel size={sizes.SMALL}>
    {translate('AllowedLanguages')}
  </FormLabel>

  <LanguageSelectInput
    name="allowedLanguages"
    value={allowedLanguages.value}
    onChange={onInputChange}
  />
</FormGroup>
```

- [ ] **Step 3: Wire `fetchLanguages` into the connector**

Open `frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContentConnector.js`.

Add the import at the top:
```js
import { fetchLanguages } from 'Store/Actions/Settings/languages';
```

Add to `mapDispatchToProps`:
```js
onFetchLanguages: fetchLanguages,
```

The connector already has a `componentDidMount` that calls `this.props.fetchQualityProfileSchema()`. Add `this.props.onFetchLanguages()` **inside that existing `componentDidMount`** — do not create a second one (class components only run the last `componentDidMount` if two are defined):

```js
componentDidMount() {
  this.props.fetchQualityProfileSchema();
  this.props.onFetchLanguages();   // ← add this line
}
```

- [ ] **Step 4: Lint to confirm no JS/TS errors**

```bash
yarn lint
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/Components/Form/LanguageSelectInput.tsx \
        frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContent.js \
        frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContentConnector.js
git commit -m "feat(ui): add AllowedLanguages multi-select to quality profile editor"
```

---

## Final verification

- [ ] **Run full backend test suite**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  -p:Platform=Posix --no-build
```

Expected: all tests pass.

- [ ] **Run frontend lint**

```bash
yarn lint
```

Expected: no errors.
