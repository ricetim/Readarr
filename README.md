# readarr-rresurrected

> *Because one "r" was never enough.*

**readarr-rresurrected** is a fully working fork of [Readarr](https://github.com/Readarr/Readarr) that brings the project back to life after its retirement in early 2025. It bundles a self-hosted metadata service directly inside the container, replacing the defunct cloud API with a solution that requires no external dependencies and no second container.

[![Docker Pulls](https://img.shields.io/docker/pulls/ricetim/readarr-rresurrected?logo=docker&label=pulls)](https://hub.docker.com/r/ricetim/readarr-rresurrected)
[![Image Size](https://img.shields.io/docker/image-size/ricetim/readarr-rresurrected/latest?logo=docker&label=image)](https://hub.docker.com/r/ricetim/readarr-rresurrected)
[![License](https://img.shields.io/badge/license-GPLv3-green)](LICENSE.md)
[![Issues](https://img.shields.io/github/issues/ricetim/readarr-rresurrected)](https://github.com/ricetim/readarr-rresurrected/issues)

**Docker image:** [`ricetim/readarr-rresurrected` on Docker Hub](https://hub.docker.com/r/ricetim/readarr-rresurrected)

---

## The Story

In early 2025, the Servarr team [announced Readarr's retirement](https://github.com/Readarr/Readarr):

> *"The retirement takes effect immediately... the project's metadata has become unusable, we no longer have the time to remake or repair it."*

The core problem: Readarr depended entirely on a Goodreads-backed cloud metadata API. When Goodreads shut down its public API, the data pipeline broke and the project had no path forward. Authors couldn't be searched, books couldn't be found, and existing libraries couldn't refresh.

The community didn't give up. [@blampe](https://github.com/blampe) built [rreading-glasses](https://github.com/blampe/rreading-glasses), a self-hostable metadata proxy that replicated the original API surface by scraping Goodreads directly. It kept Readarr alive for many users — but required running a second service, understanding how to configure the metadata URL, and keeping both containers in sync.

**readarr-rresurrected** takes this further: it bakes the metadata service directly into Readarr's container, pre-configures the connection, and ships a single image that just works.

---

## Indexers

Alongside the indexers Readarr already supported, this fork adds native support for two private
trackers, so neither needs Prowlarr:

- **MyAnonamouse** — several bugs in the upstream implementation are fixed, making it functional
  out of the box. Releases carry language, and the interactive search dialog can expand any
  result to show narrator, file count, series, category, tags and the full description.
- **Bibliotik** — cookie-based authentication; paste your session cookie and it works.

Quality profiles also gained an **Allowed Languages** setting, so automatic grabbing can be
restricted to the languages you actually read.

---

## Quick Start

### Docker Compose

```yaml
services:
  readarr:
    image: ricetim/readarr-rresurrected:latest
    container_name: readarr
    user: "1000:1000"
    ports:
      - "8787:8787"
    volumes:
      - readarr-config:/config
    environment:
      - GOOGLE_BOOKS_API_KEY=        # optional — improves ebook edition data
    restart: unless-stopped

volumes:
  readarr-config:
```

```bash
docker compose up -d
# Open http://localhost:8787
```

### Docker Run

```bash
docker run -d \
  --name readarr \
  -p 8787:8787 \
  -v readarr-config:/config \
  --user 1000:1000 \
  ricetim/readarr-rresurrected:latest
```

---

## Image Tags

| Tag | Description |
|-----|-------------|
| `latest` | Most recent build from `develop`. Moves with every push. |
| `11.0.0` | A specific release. Pin this if you want to control when you upgrade. |
| `11.0` | Latest patch within a minor release. |

---

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `GOOGLE_BOOKS_API_KEY` | *(empty)* | Optional. Improves ebook edition discovery for books with incomplete Goodreads records. [Get a key](https://developers.google.com/books/docs/v1/using#APIKey). |
| `READARR_API_KEY` | *(empty)* | Optional. Set to your Readarr API key to enable the bookinfo → Readarr refresh webhook (auto-refresh when background pagination completes). |
| `READARR_URL` | *(empty)* | Optional. Set to `http://localhost:8787` when using the webhook above. |
| `BOOKINFO_GR_RATE` | `3` | Goodreads requests **per second**. Lower it if you see rate-limit errors. |
| `BOOKINFO_BATCH_SIZE` | `20` | Works fetched per batch during background pagination. |
| `BOOKINFO_LOG_DIR` | `/logs` | Where `bookinfo` writes its own log files. Set to a path under `/config` to keep them; if the directory cannot be created, file logging is skipped and output goes to the container log instead. |
| `BOOKINFO_LOG_KEEP` | `10` | How many `bookinfo` log files to retain. |
| `READARR_METADATA_URL` | `http://localhost:28202/{route}` | Advanced. Point Readarr at a different metadata service, such as a separate rreading-glasses instance. Must keep the `{route}` placeholder. |

### Volumes

| Path | Description |
|------|-------------|
| `/config` | All persistent data: database, logs, `config.xml`. Always mount this. |

### Ports

| Port | Description |
|------|-------------|
| `8787` | Readarr web UI and API |

> `bookinfo` binds to `127.0.0.1:28202` inside the container and is not exposed externally.

---

## Building from Source

```bash
git clone https://github.com/ricetim/readarr-rresurrected.git
cd readarr-rresurrected
docker compose build
docker compose up -d
```

### Local Development (without Docker)

**Backend:**
```bash
dotnet restore src/Readarr.sln
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore
```

**Frontend:**
```bash
yarn install
yarn start   # dev server with hot reload
```

**bookinfo:**
```bash
cd bookinfo
pip install -r requirements.txt
uvicorn app:app --host 127.0.0.1 --port 28202 --workers 1
```

**Run Readarr:**
```bash
_output/net6.0/Readarr --nobrowser --data=/tmp/readarr-data
```

---

## Migrating from an Existing Readarr Installation

This fork is database-compatible with `ghcr.io/faustvii/readarr` and `hotio/readarr`. One migration runs automatically on first start (adds a `Kca` column to `AuthorMetadata` for Amazon KCA IDs).

1. Stop your existing Readarr container
2. Mount the same `/config` volume with this image
3. Start — migration runs automatically, all settings and history are preserved

> PostgreSQL support is untested in this fork. SQLite is recommended.

---

## Known Limitations

- Series data can be incomplete for some authors depending on how Goodreads represents the series relationship
- Large author catalogs (500+ books) may take several minutes to fully paginate due to Goodreads rate limits
- Scheduled refreshes re-fetch all works; incremental refresh (only new/changed works) is not yet implemented
- Google Books supplement requires a valid API key to fill in ebook editions missing from Goodreads

---

## Changelog

Release notes live in [CHANGELOG.md](CHANGELOG.md). The same notes appear in the app under
**System → Updates** and in the dialog shown after an update.

---

## Contributing & Issues

Found a bug? Have a feature request? [Open an issue](https://github.com/ricetim/readarr-rresurrected/issues) — contributions are welcome.

---

## Credits & Acknowledgements

This project stands on the shoulders of a lot of excellent prior work:

- **[The Servarr Team](https://github.com/Servarr)** and all [Readarr contributors](https://github.com/Readarr/Readarr/graphs/contributors) — built the entire application over 10,000+ commits spanning more than a decade
- **[@blampe](https://github.com/blampe)** — created [rreading-glasses](https://github.com/blampe/rreading-glasses), the community metadata proxy that proved the Goodreads scraping approach was viable and kept Readarr alive for many users while the upstream was in decline; the name *rresurrected* is a nod to the double-r naming convention he established
- **[@faustvii](https://github.com/faustvii)** — maintained [faustvii/readarr](https://github.com/faustvii/readarr), the most widely used community Docker image for Readarr after the official images stopped being updated
- **[The Goodreads GraphQL API](https://www.goodreads.com)** — the underlying data source that makes author and book metadata possible

---

## License

[GNU GPL v3](LICENSE.md) — © 2010–2022 The Servarr Team. Fork maintained by [@ricetim](https://github.com/ricetim).
