# MAM Language Tag — Design Spec

**Date:** 2026-04-11
**Status:** Approved

## Overview

MyAnonamouse returns a numeric `"language"` field on every torrent. Readarr currently ignores it. This feature parses it, surfaces it in the manual grab UI, and adds a language filter to quality profiles so automatic grabbing can be restricted by language.

## Goals

1. Parse the MAM `language` field into `ReleaseInfo.Languages`
2. Display language in the manual grab release table
3. Allow per-author language filtering via `QualityProfile.AllowedLanguages`

## Non-Goals

- Language filtering for non-MAM indexers that don't provide language data (they pass through unchanged)
- A standalone `LanguageProfile` type

---

## Section 1: MAM Language Mapping

MAM uses numeric string IDs. The parser holds a static dictionary mapping them to Readarr's `Language` enum:

| MAM ID | Language |
|--------|----------|
| 1 | English |
| 2 | French |
| 3 | German |
| 4 | Spanish |
| 5 | Italian |
| 6 | Dutch |
| 7 | Swedish |
| 8 | Latin → Unknown |
| 9 | Greek |
| 10 | Russian |
| 11 | Chinese |
| 12 | Japanese |
| 13 | Korean |
| 14 | Other → Unknown |
| 15 | Vietnamese |
| 16 | Polish |
| 17 | Portuguese |
| 18 | Portuguese (Brazil) |
| 19 | Finnish |

Any unrecognised ID maps to `Language.Unknown`.

**Important:** MAM's numeric IDs are entirely separate from Readarr's `Language.Id` values — they do not share a numbering scheme. The implementation builds a `static readonly Dictionary<string, Language>` keyed on MAM's string IDs, mapping to Readarr's named static constants (`Language.English`, `Language.Dutch`, etc.) — never via `Language.FindById(mamId)`.

**Model change:** `MyAnonamouseTorrent` gains a `Language` string property (JSON key `"language"`).

**Parser change:** `MyAnonamouseParser` maps the ID via the dictionary and sets `Languages` on the `MyAnonamouseInfo` object. If the result is `Unknown` or the field is absent, `Languages` stays empty (no regression for unknown languages).

---

## Section 2: Surfacing Language in `ReleaseResource`

`ReleaseResource` gains:

```csharp
public List<LanguageResource> Languages { get; set; }
```

`LanguageResource` already exists in `Readarr.Api.V1/Languages/LanguageResource.cs` (it's what the `/api/v1/language` endpoint returns), so we reuse it rather than inventing a new shape.

Both directions of `ReleaseResourceMapper` must be updated:

- `ToResource()`: `Languages = releaseInfo.Languages.ToResource()` — surfaces language for the manual grab UI.
- `ToModel()`: `Languages = resource.Languages.ToModel()` — round-trips language back when the user manually grabs a displayed release, so the language filter spec can evaluate it correctly.

If `Languages` is empty, the field serialises as `[]` — no behaviour change for indexers that don't set languages.

---

## Section 3: QualityProfile + DB Migration

`QualityProfile` gains:

```csharp
public List<Language> AllowedLanguages { get; set; }
```

The constructor must initialise it: `AllowedLanguages = new List<Language>();`

Default: empty list = **any language allowed** (backwards-compatible).

**Decision rule** (three-way unknown handling):

- Profile `AllowedLanguages` is empty → accept (no preference configured)
- Release `Languages` is empty → accept (indexer doesn't provide language data)
- Both non-empty → accept only if at least one release language is in the allowed list; otherwise reject

**Migration 043** adds an `AllowedLanguages` column to `QualityProfiles` as a JSON array, defaulting to `'[]'`. Same pattern as other embedded list columns in that table. `List<Language>` is auto-registered by `TableMapping.cs` via `RegisterEmbeddedConverter()` — no explicit type handler needed.

**Note:** Verify migration 043 is not claimed by any other in-flight work (see `docs/superpowers/plans/`) before numbering.

`QualityProfileResource` exposes `AllowedLanguages` as `List<LanguageResource>`, mapped in both directions in `QualityProfileResource.cs`. The `QualityProfileSchemaController` needs no direct change — it calls `GetDefaultProfile().ToResource()`, which will return `[]` automatically once the constructor initialises the list.

---

## Section 4: Decision Engine Specification

New class `LanguageAllowedByProfileSpecification : IDecisionEngineSpecification`:

- `Priority`: `SpecificationPriority.Default`
- `RejectionType`: `RejectionType.Permanent`
- Reads `subject.Author.QualityProfile.Value.AllowedLanguages` and `subject.Release.Languages`
- Applies the three-way rule from Section 3
- Rejection message: `"Language {names} is not allowed by profile"`
- Auto-discovered by DryIoc — no manual registration

---

## Section 5: Frontend

### Manual grab release table

- New `Language` column in `InteractiveSearchRow.js`
- Renders the first language name from `languages[]`, or a dash if empty
- No new components needed

### Quality profile editor

- New language multi-select field bound to `allowedLanguages` in `EditQualityProfileModalContent.js`
- A new `LanguageSelectInput.tsx` component must be created (no existing equivalent) — modelled after `IndexerFlagsSelectInput.tsx`, using `EnhancedSelectInput` and reading from `state.settings.languages.items` (already fetched via `FETCH_LANGUAGES` / `/api/v1/language`)
- Empty selection displayed as placeholder "Any" — means no language filter
- Sent as array of `{ id, name }` objects in PUT/POST body

---

## Files to Create

| File | Purpose |
|------|---------|
| `src/NzbDrone.Core/DecisionEngine/Specifications/LanguageAllowedByProfileSpecification.cs` | New decision spec |
| `src/NzbDrone.Core/Datastore/Migration/043_quality_profile_allowed_languages.cs` | DB migration |
| `frontend/src/Components/Form/LanguageSelectInput.tsx` | New multi-select component for languages |

## Files to Modify

| File | Change |
|------|--------|
| `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs` | Add `Language` to `MyAnonamouseTorrent` |
| `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs` | Map language ID via static dict, populate `Languages` |
| `src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs` | Add `AllowedLanguages`; init in constructor |
| `src/Readarr.Api.V1/Indexers/ReleaseResource.cs` | Add `Languages` field; map in both `ToResource()` and `ToModel()` |
| `src/Readarr.Api.V1/Profiles/Quality/QualityProfileResource.cs` | Add `AllowedLanguages`; map in both directions |
| `frontend/src/InteractiveSearch/InteractiveSearchRow.js` | Add Language column |
| `frontend/src/Settings/Profiles/Quality/EditQualityProfileModalContent.js` | Add language multi-select using `LanguageSelectInput` |
| `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs` | Add language parsing assertions |

---

## Key Invariants

- Empty `AllowedLanguages` on a profile = allow all (safe default, no migration needed for data)
- Empty `Languages` on a release = pass through (don't punish indexers that don't report language)
- MAM `Unknown` language treated as "no language data" — left out of `Languages` list
- MAM ID → `Language` mapping uses named static constants, never `Language.FindById(mamId)`
