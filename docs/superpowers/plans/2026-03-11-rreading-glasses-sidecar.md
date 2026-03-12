# rreading-glasses Sidecar Integration — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bundle rreading-glasses into the Readarr Docker container as a sidecar process, hardcode its URL in `MetadataRequestBuilder`, and absorb the Python bookinfo-proxy's null-work sanitization into `BookInfoProxy.cs`.

**Architecture:** rreading-glasses (Go binary) starts before Readarr via `entrypoint.sh` on `localhost:28202`. `MetadataRequestBuilder` is simplified to always use that URL. `BookInfoProxy.cs` filters works with empty Books or Authors after deserialization, replacing the external Python proxy.

**Note:** The Ebooks/Audiobooks tab split is a separate plan: `2026-03-11-ebook-audiobook-tabs.md`. These two plans are independent and can be executed in either order.

**Tech Stack:** .NET 6 (C#), Go binary (pre-built release artifact), Alpine Linux (Docker runtime), bash (entrypoint)

---

## File Map

| File | Action | Purpose |
|------|--------|---------|
| `Dockerfile` | Modify | Download rreading-glasses binary in a fetch stage; copy to runtime image |
| `docker/entrypoint.sh` | Modify | Start rreading-glasses before Readarr |
| `src/NzbDrone.Core/MetadataSource/MetadataRequestBuilder.cs` | Modify | Hardcode `http://localhost:28202` — remove configurable URL logic and constructor dependencies |
| `src/NzbDrone.Core/Configuration/IConfigService.cs` | Modify | Remove `MetadataSource` property from interface |
| `src/NzbDrone.Core/Configuration/ConfigService.cs` | Modify | Remove `MetadataSource` property implementation |
| `src/Readarr.Api.V1/Config/DevelopmentConfigResource.cs` | Modify | Remove `MetadataSource` field |
| `src/Readarr.Api.V1/Config/DevelopmentConfigController.cs` | Modify | Remove `MetadataSource` validation |
| `src/NzbDrone.Core.Test/MetadataSource/MetadataRequestBuilderFixture.cs` | Delete | Tests for configurable URL — both test cases become invalid after hardcoding |
| `src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs` | Modify | Add `SanitizeWorks()` and call it after deserializing author responses |

---

## Chunk 1: Harden BookInfoProxy with work sanitization

### Task 1: Add `SanitizeWorks` to BookInfoProxy

**Files:**
- Modify: `src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs`

The Python proxy filtered `resource.Works` to only keep entries where both `Books` (non-empty list) and `Authors` (non-empty list) are present. We replicate this in two places in `BookInfoProxy.cs`:
1. `PollAuthorUncached` — after deserializing `AuthorResource` (~line 641)
2. `MapBulkBook` — before iterating `resource.Works` (~line 524)

Note on `WorkResource.Authors`: the field is initialized as `= new List<AuthorResource>()` at declaration, but after `System.Text.Json` deserialization with a missing JSON field, it will be null (initializers don't apply post-deserialization). So the null check is meaningful for the absent-field case; an empty `[]` array in JSON will be non-null but zero-count — both are filtered correctly.

- [ ] **Step 1: Add `SanitizeWorks` private static method**

Add this method near the other private helpers in `BookInfoProxy.cs`:

```csharp
private static List<WorkResource> SanitizeWorks(List<WorkResource> works, Logger logger)
{
    if (works == null)
    {
        return works;
    }

    var before = works.Count;
    var sanitized = works
        .Where(w => w.Books != null && w.Books.Count > 0 &&
                    w.Authors != null && w.Authors.Count > 0)
        .ToList();

    var dropped = before - sanitized.Count;
    if (dropped > 0)
    {
        logger.Debug($"SanitizeWorks: dropped {dropped} works with null/empty Books or Authors");
    }

    return sanitized;
}
```

- [ ] **Step 2: Call `SanitizeWorks` in `PollAuthorUncached`**

Find the block after `resource = JsonSerializer.Deserialize<AuthorResource>(...)` (~line 641). The existing code reads:

```csharp
if (resource.Works != null)
{
    resource.Works ??= new List<WorkResource>();   // this line is dead code — Works is non-null inside the if
    resource.Series ??= new List<SeriesResource>();
    break;
}
```

Add sanitization as the first line inside the `if` block:

```csharp
if (resource.Works != null)
{
    resource.Works = SanitizeWorks(resource.Works, _logger);  // ADD THIS
    resource.Works ??= new List<WorkResource>();
    resource.Series ??= new List<SeriesResource>();
    break;
}
```

- [ ] **Step 3: Call `SanitizeWorks` in `MapBulkBook`**

Find `MapBulkBook` (~line 512). Change the `foreach` to iterate a sanitized copy:

```csharp
var sanitizedWorks = SanitizeWorks(resource.Works, _logger);  // ADD THIS LINE
foreach (var work in sanitizedWorks)                           // CHANGE: was resource.Works
{
```

- [ ] **Step 4: Build and verify no compile errors**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore -v quiet
```

Expected: `0 Error(s)`

- [ ] **Step 5: Run existing BookInfo tests**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~BookInfo" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

- [ ] **Step 6: Commit**

```bash
git add src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs
git commit -m "feat: absorb bookinfo-proxy work sanitization into BookInfoProxy"
```

---

## Chunk 2: Hardcode metadata URL

### Task 2: Remove configurable MetadataSource, hardcode localhost URL

**Files:**
- Modify: `src/NzbDrone.Core/MetadataSource/MetadataRequestBuilder.cs`
- Modify: `src/NzbDrone.Core/Configuration/IConfigService.cs`
- Modify: `src/NzbDrone.Core/Configuration/ConfigService.cs`
- Modify: `src/Readarr.Api.V1/Config/DevelopmentConfigResource.cs`
- Modify: `src/Readarr.Api.V1/Config/DevelopmentConfigController.cs`
- Delete: `src/NzbDrone.Core.Test/MetadataSource/MetadataRequestBuilderFixture.cs`

- [ ] **Step 1: Simplify `MetadataRequestBuilder.cs`**

Replace the entire file — remove `IConfigService` and `IReadarrCloudRequestBuilder` dependencies:

```csharp
using NzbDrone.Common.Http;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataRequestBuilder
    {
        IHttpRequestBuilderFactory GetRequestBuilder();
    }

    public class MetadataRequestBuilder : IMetadataRequestBuilder
    {
        private const string MetadataUrl = "http://localhost:28202/{route}";

        public IHttpRequestBuilderFactory GetRequestBuilder()
        {
            return new HttpRequestBuilder(MetadataUrl).KeepAlive().CreateFactory();
        }
    }
}
```

- [ ] **Step 2: Remove `MetadataSource` from `IConfigService.cs`**

Find and delete the property declaration from the interface:

```csharp
// DELETE:
string MetadataSource { get; set; }
```

- [ ] **Step 3: Remove `MetadataSource` from `ConfigService.cs`**

Find and delete the property implementation (~line 265):

```csharp
// DELETE:
public string MetadataSource
{
    get { return GetValue("MetadataSource", ""); }
    set { SetValue("MetadataSource", value); }
}
```

- [ ] **Step 4: Remove `MetadataSource` from `DevelopmentConfigResource.cs`**

Delete the field and its mapping in both `ToResource` and `ReadFromRequest` directions:

```csharp
// DELETE: public string MetadataSource { get; set; }
// DELETE: MetadataSource = configService.MetadataSource,
// DELETE: configService.MetadataSource = resource.MetadataSource;  (if present)
```

- [ ] **Step 5: Remove validation from `DevelopmentConfigController.cs`**

Delete the validation rule:

```csharp
// DELETE:
SharedValidator.RuleFor(c => c.MetadataSource).IsValidUrl().When(c => !c.MetadataSource.IsNullOrWhiteSpace());
```

- [ ] **Step 6: Delete `MetadataRequestBuilderFixture.cs`**

Both test cases (`should_use_user_definied_if_not_blank` and `should_use_default_if_config_blank`) test behavior that no longer exists. Delete the file:

```bash
rm src/NzbDrone.Core.Test/MetadataSource/MetadataRequestBuilderFixture.cs
```

- [ ] **Step 7: Build and verify**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore -v quiet
```

Expected: `0 Error(s)`.

- [ ] **Step 8: Run full test suite to catch any remaining references**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: all pass (or same failures as baseline — no new failures).

- [ ] **Step 9: Smoke test**

```bash
rm -f /tmp/readarr-data/readarr.pid
nohup /home/tim/projects/Readarr/_output/net6.0/Readarr --nobrowser --data=/tmp/readarr-data > /tmp/readarr-stdout.log 2>&1 &
sleep 5
curl -s http://localhost:8787/api/v1/config/development -H "X-Api-Key: 3d3f10566ed741c490eb21e194bb061f"
```

Expected: response no longer has `metadataSource` field.

- [ ] **Step 10: Commit**

```bash
git add \
  src/NzbDrone.Core/MetadataSource/MetadataRequestBuilder.cs \
  src/NzbDrone.Core/Configuration/IConfigService.cs \
  src/NzbDrone.Core/Configuration/ConfigService.cs \
  src/Readarr.Api.V1/Config/DevelopmentConfigResource.cs \
  src/Readarr.Api.V1/Config/DevelopmentConfigController.cs
git rm src/NzbDrone.Core.Test/MetadataSource/MetadataRequestBuilderFixture.cs
git commit -m "feat: hardcode metadata URL to localhost:28202, remove configurable MetadataSource"
```

---

## Chunk 3: Bundle rreading-glasses in Docker

### Task 3: Add rreading-glasses to Dockerfile and entrypoint

**Files:**
- Modify: `Dockerfile`
- Modify: `docker/entrypoint.sh`

**Before starting:** Visit the rreading-glasses GitHub releases page to get the latest version tag and confirm the binary name for Alpine/musl (`linux-amd64` musl build or statically linked). The binary MUST be statically linked or built for musl libc — a glibc-linked binary will fail at runtime on Alpine with no useful error. Check by running `file rreading-glasses` on the downloaded binary; it should say `statically linked` or `musl`.

- [ ] **Step 1: Verify the binary is musl-compatible**

```bash
# Download the candidate binary locally and inspect it
RREADING_VERSION=<latest-tag>
curl -fsSL "https://github.com/<owner>/rreading-glasses/releases/download/${RREADING_VERSION}/rreading-glasses-linux-amd64" \
  -o /tmp/rreading-test
file /tmp/rreading-test
```

Expected output contains `statically linked` or `musl`. If it says `dynamically linked` against `libc.so.6` (glibc), find a musl variant in the release assets or use the Docker build approach below instead.

- [ ] **Step 2: Verify startup flags**

```bash
chmod +x /tmp/rreading-test
/tmp/rreading-test --help 2>&1 | head -20
```

Confirm the flags for data directory and port. Common patterns: `--data`, `--datadir`, `--config`; `--port`, `--listen`. Update the entrypoint in Step 4 to match actual flags.

- [ ] **Step 3: Add rreading-glasses fetch stage to Dockerfile**

Add a new stage after the frontend builder, before the runtime stage:

```dockerfile
# ── Stage 2b: fetch rreading-glasses ─────────────────────────────────────────
FROM alpine:3.22 AS rreading-glasses-fetcher
ARG RREADING_VERSION=<latest-tag>
RUN apk add --no-cache curl \
    && curl -fsSL \
         "https://github.com/<owner>/rreading-glasses/releases/download/${RREADING_VERSION}/rreading-glasses-linux-amd64" \
         -o /rreading-glasses \
    && chmod +x /rreading-glasses
```

- [ ] **Step 4: Copy binary into runtime stage**

In the runtime stage, after copying backend and frontend outputs:

```dockerfile
COPY --from=rreading-glasses-fetcher /rreading-glasses /app/bin/rreading-glasses
```

- [ ] **Step 5: Update `docker/entrypoint.sh`**

```bash
#!/usr/bin/env bash
set -e

# Start rreading-glasses metadata service
mkdir -p /config/rreading-glasses
/app/bin/rreading-glasses --data /config/rreading-glasses --port 28202 &

# Start Readarr
exec \
    /app/bin/Readarr \
        --nobrowser \
        --data=/config \
        "$@"
```

Update `--data` and `--port` if the actual flags differ (confirmed in Step 2).

- [ ] **Step 6: Build the Docker image**

```bash
docker compose build 2>&1 | tail -20
```

Expected: build completes without error.

- [ ] **Step 7: Start container and verify both processes run**

```bash
docker compose up -d
sleep 8
docker compose exec readarr ps aux | grep -E "rreading|Readarr"
docker compose logs readarr --tail=20
```

Expected: both `rreading-glasses` and `Readarr` appear in the process list. Readarr starts normally.

- [ ] **Step 8: Verify metadata works end-to-end**

```bash
curl -s "http://localhost:8787/api/v1/author/lookup?term=Katherine+Addison" \
  -H "X-Api-Key: 3d3f10566ed741c490eb21e194bb061f" \
  | python3 -c "import sys,json; r=json.load(sys.stdin); print(f'{len(r)} results'); print(r[0]['authorName'] if r else 'none')"
```

Expected: returns author results served by the bundled rreading-glasses instance.

- [ ] **Step 9: Commit**

```bash
git add Dockerfile docker/entrypoint.sh
git commit -m "feat: bundle rreading-glasses as sidecar in Docker container"
```

- [ ] **Step 10: Push to Docker Hub**

```bash
docker tag readarr-readarr:latest timhrice/readarr:latest
docker push timhrice/readarr:latest
```
