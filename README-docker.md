# readarr-rresurrected

> *A fully working revival of Readarr — metadata included.*

**readarr-rresurrected** resurrects the [Readarr](https://github.com/Readarr/Readarr) ebook and audiobook manager after its retirement in early 2025. The original project was abandoned when its Goodreads metadata API became unusable, leaving thousands of users with a broken application and no official path forward.

This image bundles a self-hosted metadata service (`bookinfo`) directly inside the container alongside Readarr. Both run under `supervisord` in a single Alpine image (~251 MB). No second container, no external API dependency, no configuration — it just works.

**GitHub:** [github.com/ricetim/readarr-rresurrected](https://github.com/ricetim/readarr-rresurrected) — bug reports and feature requests welcome.

---

## Quick Start

```bash
docker run -d \
  --name readarr \
  -p 8787:8787 \
  -v readarr-config:/config \
  --user 1000:1000 \
  ricetim/readarr-rresurrected:latest
```

Open **http://localhost:8787**

---

## Docker Compose

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

---

## What's Inside

### Readarr
The full Readarr application: monitors RSS feeds for new books, grabs releases, integrates with download clients (qBittorrent, SABnzbd, NZBGet, Deluge, etc.) and Calibre. Quality profiles, author monitoring, automatic upgrades — everything the original offered.

### bookinfo (bundled metadata service)
A Python/FastAPI service that fetches author and book data directly from Goodreads' internal API, replacing the cloud endpoint that was shut down. It:

- Returns a fast partial response immediately so searches and adds are responsive
- Paginates remaining works in the background — books stream into the UI progressively with a live progress indicator
- Optionally supplements ebook edition data via Google Books (requires API key)
- Binds to `127.0.0.1:28202` inside the container only — not exposed externally

Readarr is pre-configured to use it at `localhost:28202`. No setup needed.

### Native private-tracker indexers
Two private trackers are supported directly, so neither needs Prowlarr:

- **MyAnonamouse** — several bugs in the upstream implementation are fixed, making it functional out of the box. Releases carry language, and any result in the interactive search dialog can be expanded to show narrator, file count, series, category, tags and the full description.
- **Bibliotik** — cookie-based authentication; paste your session cookie and it works.

Quality profiles also gained an **Allowed Languages** setting, so automatic grabbing can be restricted to the languages you actually read.

---

## Volumes

| Path | Description |
|------|-------------|
| `/config` | All persistent data: database, logs, `config.xml`. Always mount this. |

---

## Ports

| Port | Description |
|------|-------------|
| `8787` | Readarr web UI and REST API |

---

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `GOOGLE_BOOKS_API_KEY` | *(empty)* | Optional. Improves ebook edition data for books with incomplete Goodreads records. |
| `READARR_API_KEY` | *(empty)* | Optional. Your Readarr API key — enables bookinfo to trigger a Readarr refresh automatically when background pagination finishes. |
| `READARR_URL` | *(empty)* | Optional. Set to `http://localhost:8787` when using the auto-refresh webhook. |
| `BOOKINFO_GR_RATE` | `3` | Goodreads requests **per second**. Lower it if you see rate-limit errors. |
| `BOOKINFO_BATCH_SIZE` | `20` | Works fetched per batch during background pagination. |
| `BOOKINFO_LOG_DIR` | `/logs` | Where `bookinfo` writes its own log files. Set to a path under `/config` to keep them; if the directory cannot be created, file logging is skipped and output goes to the container log instead. |
| `BOOKINFO_LOG_KEEP` | `10` | How many `bookinfo` log files to retain. |
| `READARR_METADATA_URL` | `http://localhost:28202/{route}` | Advanced. Point Readarr at a different metadata service, such as a separate rreading-glasses instance. Must keep the `{route}` placeholder. |

---

## Migrating from an Existing Installation

Compatible with data volumes from `ghcr.io/faustvii/readarr` and `hotio/readarr`. One schema migration runs automatically on first start (adds a `Kca` column to `AuthorMetadata`).

1. Stop your existing container
2. Mount the same `/config` volume with this image
3. Start — migration runs automatically, all history and settings are preserved

---

## Tags

| Tag | Description |
|-----|-------------|
| `latest` | Most recent build from the `develop` branch. Moves with every push. |
| `11.0.0` | A specific release. Pin this if you want to control when you upgrade. |
| `11.0` | Latest patch within a minor release. |

---

## Credits

- **[The Servarr Team](https://github.com/Servarr)** and all [Readarr contributors](https://github.com/Readarr/Readarr/graphs/contributors) — built the entire application
- **[@blampe / rreading-glasses](https://github.com/blampe/rreading-glasses)** — proved the Goodreads scraping approach was viable; the name *rresurrected* is a nod to the double-r naming convention
- **[@faustvii](https://github.com/faustvii/readarr)** — maintained the most widely used community Docker image after upstream images stopped being updated

---

## Changelog

Release notes are kept in [CHANGELOG.md](https://github.com/ricetim/readarr-rresurrected/blob/develop/CHANGELOG.md), and the same notes appear in the app under **System → Updates**.

---

## Issues & Contributing

[github.com/ricetim/readarr-rresurrected/issues](https://github.com/ricetim/readarr-rresurrected/issues)

## License

[GNU GPL v3](https://github.com/ricetim/readarr-rresurrected/blob/develop/LICENSE.md)
