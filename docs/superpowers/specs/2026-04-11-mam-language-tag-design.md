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

**Model change:** `MyAnonamouseTorrent` gains a `Language` string property (JSON key `"language"`).

**Parser change:** `MyAnonamouseParser` maps the ID via the dictionary and sets `Languages` on the `MyAnonamouseInfo` object. If the result is `Unknown` or the field is absent, `Languages` stays empty (no regression for unknown languages).

---

## Section 2: Surfacing Language in `ReleaseResource`

`ReleaseResource` gains:

```csharp
public List<LanguageResource> Languages { get; set; }
```

`ReleaseResourceMapper.ToResource()` maps it from `releaseInfo.Languages` using the existing `ToResource()` extension. Empty list serialises as `[]` — no behaviour change for indexers that don't set languages.

---

## Section 3: QualityProfile + DB Migration

`QualityProfile` gains:

```csharp
public List<Language> AllowedLanguages { get; set; }
```

Default: empty list = **any language allowed** (backwards-compatible).

**Decision rule** (three-way unknown handling):

- Profile `AllowedLanguages` is empty → accept (no preference configured)
- Release `Languages` is empty → accept (indexer doesn't provide language data)
- Both non-empty → accept only if at least one release language is in the allowed list; otherwise reject

**Migration 043** adds an `AllowedLanguages` column to `QualityProfiles` as a JSON array, defaulting to `'[]'`. Same pattern as other embedded list columns in that table.

`QualityProfileResource` exposes `AllowedLanguages` as `List<LanguageResource>`, mapped in both directions. `QualityProfileSchemaController` returns an empty list as the schema default.

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
- No new components

### Quality profile editor

- New language multi-select field bound to `allowedLanguages`
- Reuses existing `LanguageSelectInput` component (already used in metadata profile editor)
- Empty selection displayed as placeholder "Any" — means no language filter
- Sent as array of `{ id, name }` objects in PUT/POST body

---

## Files to Create

| File | Purpose |
|------|---------|
| `src/NzbDrone.Core/DecisionEngine/Specifications/LanguageAllowedByProfileSpecification.cs` | New decision spec |
| `src/NzbDrone.Core/Datastore/Migration/043_quality_profile_allowed_languages.cs` | DB migration |

## Files to Modify

| File | Change |
|------|--------|
| `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseInfo.cs` | Add `Language` to `MyAnonamouseTorrent` |
| `src/NzbDrone.Core/Indexers/MyAnonamouse/MyAnonamouseParser.cs` | Map language ID, populate `Languages` |
| `src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs` | Add `AllowedLanguages` |
| `src/NzbDrone.Core/Profiles/Qualities/QualityProfileService.cs` | Default empty list |
| `src/Readarr.Api.V1/Indexers/ReleaseResource.cs` | Add `Languages` field + mapping |
| `src/Readarr.Api.V1/Profiles/Quality/QualityProfileResource.cs` | Add `AllowedLanguages` |
| `src/Readarr.Api.V1/Profiles/Quality/QualityProfileSchemaController.cs` | Schema default |
| `frontend/src/InteractiveSearch/InteractiveSearchRow.js` | Language column |
| `frontend/src/Settings/Profiles/Quality/QualityProfileItemEditor.js` (or equivalent) | Language multi-select |
| `src/NzbDrone.Core.Test/IndexerTests/MyAnonamouseTests/MyAnonamouseFixture.cs` | Language parsing assertions |
| `src/NzbDrone.Core.Test/Files/Indexers/MyAnonamouse/MyAnonamouse.json` | Language field already present |

---

## Key Invariants

- Empty `AllowedLanguages` on a profile = allow all (safe default, no migration needed for data)
- Empty `Languages` on a release = pass through (don't punish indexers that don't report language)
- MAM `Unknown` language treated as "no language data" — left out of `Languages` list
