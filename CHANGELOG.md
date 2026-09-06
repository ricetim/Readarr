# Changelog

**readarr-rresurrected** is a working fork of [Readarr](https://github.com/Readarr/Readarr),
the ebook and audiobook manager that the Servarr team retired in early 2025 when its metadata
source became unusable. The application itself was sound — only the data pipeline behind it had
failed — so this fork replaces that pipeline with a self-hosted metadata service bundled into
the same container, and adds native support for private trackers. Existing Readarr databases
migrate in place.

All notable changes to the fork are recorded here.

> **This file is the source of the in-app release notes.** On release, CI converts it into
> the update feed published to GitHub Pages, which Readarr reads to show "What's New" in
> System → Updates and in the modal after an update.
>
> Keep the format:
> - One `## [version] - YYYY-MM-DD` heading per release, newest first.
> - Only two subsection headings: `### New` and `### Fixed`. These map directly onto the two
>   buckets Readarr renders — anything else is dropped by the parser.
> - One bullet per entry, written for users rather than as a commit subject.

## [Unreleased]

### New

### Fixed

## [11.0.1] - 2026-09-06

### New

- **Basic authentication has been removed.** It sent your credentials on every request and had
  no way to log out; Forms authentication does the same job with a proper login page and a
  session. Anyone using Basic is switched to Forms automatically on first start, keeping the
  same username and password — no action needed.

## [11.0.0] - 2026-09-06

Major version bump: this release changes the database schema twice, removes release-group
handling throughout, and adds two significant indexer capabilities.

### New

- **Release details in interactive search.** Each MyAnonamouse result can be expanded to show
  narrator, file count, series, category, tags, and the full description, so you can tell
  editions apart without opening the tracker. Releases from other indexers are unchanged.
- **Bibliotik is now a native indexer.** Cookie-based authentication, no Prowlarr required.
- **Language filtering.** MyAnonamouse language is parsed onto every release, shown as a column
  in interactive search, and quality profiles gained an *Allowed Languages* setting so automatic
  grabbing can be restricted to the languages you actually read.
- **Manual grabs bypass the identification pipeline.** A release you picked by hand is imported
  as the book you chose, rather than being re-matched and possibly reassigned.
- **Folder structure can override file tags** when the filename and folder agree, which fixes
  imports for files whose embedded metadata is wrong.
- **Search Series button** on the author details page.
- **Prefer Larger Files** download setting.
- **Release group has been removed throughout** — parser, file naming, custom format and repack
  specifications, notifications, API resources, and the interactive import UI. It carried no
  meaning for books and produced misleading matches. Applied by database migration 042.

### Fixed

- **Security: tracker passkeys are no longer written to log files.** The log scrubber matched
  only letters and digits, so a passkey containing a hyphen or underscore — as MyAnonamouse
  issues — was recorded in clear text. **If you have run an earlier build, rotate your tracker
  passkey**, as it may appear in existing logs.
- **Security: log files and backups always require authentication.** With *Authentication
  Required* set to *Disabled for Local Addresses*, a reverse proxy that does not forward the
  client address makes every request look local, leaving `/logfile/` and `/backup/` readable
  without credentials. Those paths no longer use that bypass. Check that your proxy sets
  `X-Forwarded-For`, or set *Authentication Required* to *Enabled*.
- **Author search and refresh recover automatically from upstream errors** rather than failing
  until the container is restarted.
- **Authors with initials are found again.** MyAnonamouse stores initialed names with spaces
  ("C J Cherryh"), so searches for "C.J. Cherryh" previously returned nothing.
- **Release descriptions display as readable text.** Uploaders write descriptions in BBCode, in
  HTML pasted from publisher pages, or both; markup is now stripped rather than shown raw.
- **Author name is included in MyAnonamouse book searches**, which narrows results and stops
  unrelated books being returned for common titles.
- **Bibliotik authentication.** The session cookie is read and sent correctly; logins no longer
  silently fail.
- **Author search falls back** to a direct author query when Goodreads returns no suggestions.
- **Re-running a search no longer returns a stale empty result** from cache.
- **Rescan after a refresh is scoped to that author's folder** instead of every root folder,
  which makes refreshes dramatically faster on large libraries.
- **Hardlinks are respected in Auto import mode** rather than falling back to copying.
- **Books with subtitles after a dash** match more reliably during identification.
- **Manual grab history** records the release source correctly.
- **Completed downloads** no longer error when the associated book has been removed.
- **Quality profiles** gained an `AllowedLanguages` column via database migration 043.

## [10.0.0] - 2026-03-18

The first readarr-rresurrected release: a working fork of the retired Readarr, with a
self-hosted metadata service bundled into the same container.

### New

- **Bundled metadata service.** `bookinfo` runs alongside Readarr in one image, replacing the
  defunct Readarr cloud API. No second container and no configuration.
- **Progressive book loading.** Works appear in the UI as Goodreads pagination proceeds in the
  background, instead of waiting for a full author fetch.
- **MyAnonamouse as a native indexer**, with correct title and author parsing.
- **Series data** populated from per-work Goodreads URLs.
- **Amazon KCA passthrough** for direct Goodreads integration, added by database migration 041.

### Fixed

- Ancient publication dates (for example, year 79 AD) no longer crash author refresh.
- Books are never auto-deleted during a metadata refresh, since a partial remote response is
  not authoritative.
- Duplicate background pagination tasks per author are prevented.
- Author images populate from the Goodreads profile image field.
- `CreateEmptyAuthorFolders` is respected.
- PDF tag reading no longer crashes on a circular reference.

[Unreleased]: https://github.com/ricetim/readarr-rresurrected/compare/v11.0.1...HEAD
[11.0.1]: https://github.com/ricetim/readarr-rresurrected/releases/tag/v11.0.1
[11.0.0]: https://github.com/ricetim/readarr-rresurrected/releases/tag/v11.0.0
[10.0.0]: https://github.com/ricetim/readarr-rresurrected/releases/tag/v10.0.0
