# Ebooks / Audiobooks Tab Split — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single "Books" tab on the author detail page with two tabs — "Ebooks" and "Audiobooks" — each showing only books that have an edition of the matching type. Books with both types appear in both tabs.

**Architecture:** Add `HasEbookEdition` and `HasAudiobookEdition` boolean fields to `BookResource` (computed from already-loaded editions in `BookController`'s author query path). Frontend reads these flags to split the book list into two tabs in `AuthorDetails.js`. **Note:** The original spec said editions already flow through the API response — they do not (confirmed by live API inspection). The server-computed flag approach is the pragmatic equivalent: same outcome, no extra DB queries (editions are already loaded for the `authorId` query path used by the author page).

**Scope note:** The `HasEbookEdition`/`HasAudiobookEdition` flags are only guaranteed to be populated on the `authorId` query path (`GET /api/v1/book?authorId=X`). Other query paths (`bookIds`, `titleSlug`) do not load editions and will return `false` for both flags. This is acceptable — the author detail page is the only consumer of these tabs.

**Tech Stack:** .NET 6 (C#), React 17 (JS), Redux

---

## File Map

| File | Action | Purpose |
|------|--------|---------|
| `src/Readarr.Api.V1/Books/BookResource.cs` | Modify | Add `HasEbookEdition` and `HasAudiobookEdition` to resource and mapper |
| `frontend/src/Author/Details/bookTypeUtils.js` | Create | Classification utility (mirrors backend format lists; used for client-side badge counts) |
| `frontend/src/Author/Details/AuthorDetails.js` | Modify | Replace Books tab with Ebooks + Audiobooks tabs |
| `frontend/src/Author/Details/AuthorDetailsSeason.js` | Modify | Accept `bookType` prop, filter `items` before rendering |
| `frontend/src/Author/Details/AuthorDetailsSeasonConnector.js` | Modify | Pass `bookType` through propTypes |

---

## Chunk 1: Backend — add edition type flags to BookResource

### Task 1: Add `HasEbookEdition` / `HasAudiobookEdition` to BookResource

**Files:**
- Modify: `src/Readarr.Api.V1/Books/BookResource.cs`

The `BookController.GetBooks()` path for `authorId` explicitly loads all editions before calling `ToResource()` (see `BookController.cs` lines 101–114: `GetEditionsByAuthor()` is called and editions are assigned to each book). The flags are computed from those editions using the same format string lists as `DistanceCalculator`.

Ebook edition: `IsEbook == true` OR `Format` matches (case-insensitive): `Kindle Edition`, `Nook`, `ebook`, `EPUB`, `PDF`

Audiobook edition: `Format` matches (case-insensitive): `Audiobook`, `Audio CD`, `Audio Cassette`, `Audible Audio`, `CD-ROM`, `MP3 CD`

- [ ] **Step 1: Add the two boolean fields to `BookResource`**

In the `BookResource` class, add:

```csharp
public bool HasEbookEdition { get; set; }
public bool HasAudiobookEdition { get; set; }
```

- [ ] **Step 2: Add format lists and compute the flags in `BookResourceMapper`**

In the `BookResourceMapper` static class, add the format sets as private static fields:

```csharp
private static readonly HashSet<string> EbookFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Kindle Edition", "Nook", "ebook", "EPUB", "PDF"
};

private static readonly HashSet<string> AudiobookFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Audiobook", "Audio CD", "Audio Cassette", "Audible Audio", "CD-ROM", "MP3 CD"
};
```

Then in the `return new BookResource { ... }` block inside `ToResource()`, add:

```csharp
HasEbookEdition = model.Editions?.Value?.Any(e =>
    e.IsEbook || EbookFormats.Contains(e.Format ?? string.Empty)) ?? false,
HasAudiobookEdition = model.Editions?.Value?.Any(e =>
    AudiobookFormats.Contains(e.Format ?? string.Empty)) ?? false,
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore -v quiet
```

Expected: `0 Error(s)`

- [ ] **Step 4: Smoke test — confirm fields appear in API response**

```bash
rm -f /tmp/readarr-data/readarr.pid
nohup /home/tim/projects/Readarr/_output/net6.0/Readarr --nobrowser --data=/tmp/readarr-data > /tmp/readarr-stdout.log 2>&1 &
sleep 5

curl -s "http://localhost:8787/api/v1/book?authorId=1" \
  -H "X-Api-Key: 3d3f10566ed741c490eb21e194bb061f" \
  | python3 -c "
import sys, json
books = json.load(sys.stdin)
for b in books[:3]:
    print(f\"{b['title']}: ebook={b.get('hasEbookEdition')}, audio={b.get('hasAudiobookEdition')}\")
"
```

Expected: books show `hasEbookEdition` and `hasAudiobookEdition` fields (camelCase in JSON).

- [ ] **Step 5: Commit**

```bash
git add src/Readarr.Api.V1/Books/BookResource.cs
git commit -m "feat: add HasEbookEdition and HasAudiobookEdition to BookResource"
```

---

## Chunk 2: Frontend — tab split

### Task 2: Create `bookTypeUtils.js` classification utility

**Files:**
- Create: `frontend/src/Author/Details/bookTypeUtils.js`

This utility classifies a book using the server-supplied flags (`hasEbookEdition`, `hasAudiobookEdition`) from the API. The format string arrays are kept here for any future client-side use (e.g. badge counts computed before the API responds).

- [ ] **Step 1: Create the utility file**

```javascript
// frontend/src/Author/Details/bookTypeUtils.js

const ebookFormats = new Set([
  'kindle edition', 'nook', 'ebook', 'epub', 'pdf'
]);

const audiobookFormats = new Set([
  'audiobook', 'audio cd', 'audio cassette', 'audible audio', 'cd-rom', 'mp3 cd'
]);

export function isEbookEdition(edition) {
  if (edition.isEbook) {
    return true;
  }
  return ebookFormats.has((edition.format || '').toLowerCase());
}

export function isAudiobookEdition(edition) {
  return audiobookFormats.has((edition.format || '').toLowerCase());
}

/**
 * Classify a book using server-computed flags (preferred) or edition array (fallback).
 * Returns { isEbook: bool, isAudiobook: bool }
 */
export function classifyBook(book) {
  if (book.hasEbookEdition !== undefined || book.hasAudiobookEdition !== undefined) {
    return {
      isEbook: book.hasEbookEdition === true,
      isAudiobook: book.hasAudiobookEdition === true
    };
  }

  // Fallback: classify from editions array if flags are missing
  const editions = book.editions || [];
  return {
    isEbook: editions.some(isEbookEdition),
    isAudiobook: editions.some(isAudiobookEdition)
  };
}
```

- [ ] **Step 2: Commit**

```bash
git add frontend/src/Author/Details/bookTypeUtils.js
git commit -m "feat: add bookTypeUtils classification utility for ebook/audiobook tabs"
```

---

### Task 3: Update `AuthorDetailsSeason.js` to filter by book type

**Files:**
- Modify: `frontend/src/Author/Details/AuthorDetailsSeason.js`
- Modify: `frontend/src/Author/Details/AuthorDetailsSeasonConnector.js`

`AuthorDetailsSeason` receives `items` (all books for the author) from its connector. Add a `bookType` prop (`'ebook'` | `'audiobook'`) that filters `items` before rendering. Omitting `bookType` renders all books (preserves current behaviour).

**Note on two simultaneous connector instances:** `AuthorDetails` will render two `AuthorDetailsSeasonConnector` instances (one per tab). Both call `setAuthorDetailsId({ authorId: id })` in `componentDidMount`. Since both use the same `authorId`, the Redux state slice is shared — both panels show the same books list and re-render together. This is correct and intentional.

- [ ] **Step 1: Add `bookType` prop and filtering to `AuthorDetailsSeason.js`**

Add import at the top:

```javascript
import { classifyBook } from './bookTypeUtils';
```

In the `render()` method, add filtering after destructuring `items` from `this.props`:

```javascript
const {
  items,
  bookType,
  isEditorActive,
  columns,
  sortKey,
  sortDirection,
  onSortPress,
  onTableOptionChange,
  selectedState
} = this.props;

const filteredItems = bookType
  ? items.filter((book) => {
      const { isEbook, isAudiobook } = classifyBook(book);
      return bookType === 'ebook' ? isEbook : isAudiobook;
    })
  : items;
```

Replace `items.map(...)` with `filteredItems.map(...)` in the `<TableBody>` render.

- [ ] **Step 2: Add `bookType` to propTypes**

```javascript
AuthorDetailsSeason.propTypes = {
  bookType: PropTypes.oneOf(['ebook', 'audiobook']),
  // ... existing propTypes unchanged
};
```

- [ ] **Step 3: Add `bookType` to propTypes in `AuthorDetailsSeasonConnector.js`**

The connector spreads `{...this.props}` onto `AuthorDetailsSeason`, so `bookType` flows through automatically. Just add it to propTypes:

```javascript
AuthorDetailsSeasonConnector.propTypes = {
  bookType: PropTypes.oneOf(['ebook', 'audiobook']),
  authorId: PropTypes.number.isRequired,
  // ... existing propTypes unchanged
};
```

- [ ] **Step 4: Commit**

```bash
git add \
  frontend/src/Author/Details/AuthorDetailsSeason.js \
  frontend/src/Author/Details/AuthorDetailsSeasonConnector.js
git commit -m "feat: add bookType filtering prop to AuthorDetailsSeason"
```

---

### Task 4: Replace Books tab with Ebooks + Audiobooks tabs in `AuthorDetails.js`

**Files:**
- Modify: `frontend/src/Author/Details/AuthorDetails.js`

**Current tab layout (index → tab):**

| Index | Tab |
|-------|-----|
| 0 | Books |
| 1 | Series |
| 2 | History |
| 3 | Search ← `selectedTabIndex === 3` filter icon logic |
| 4 | Files |

**New tab layout after this change:**

| Index | Tab |
|-------|-----|
| 0 | Ebooks |
| 1 | Audiobooks |
| 2 | Series |
| 3 | History |
| 4 | Search ← update to `selectedTabIndex === 4` |
| 5 | Files |

**Pre-existing quirk to be aware of (do not fix in this task):** `AuthorDetails.js` initializes state as `selectedTabIndex` but the `<Tabs>` component is bound to `this.state.tabIndex` (which is `undefined`). This causes the Tabs component to run in uncontrolled mode — tabs work visually but the state variable isn't driving it. This is a pre-existing bug unrelated to this change; do not fix it here to avoid scope creep.

- [ ] **Step 1: Add import for `classifyBook` at the top of `AuthorDetails.js`**

```javascript
import { classifyBook } from './bookTypeUtils';
```

- [ ] **Step 2: Compute ebook and audiobook counts for tab badges**

Find where `totalBookCount` is computed in the render method (it uses the same books collection). Add counts alongside it:

```javascript
const ebookCount = items ? items.filter((b) => classifyBook(b).isEbook).length : 0;
const audiobookCount = items ? items.filter((b) => classifyBook(b).isAudiobook).length : 0;
```

Use whatever variable holds the books array in the render (check how `totalBookCount` is derived — mirror that pattern).

- [ ] **Step 3: Replace the Books tab with two tabs in `<TabList>`**

Find the first `<Tab>` block (~line 443) that renders `{translate('BooksTotal', [totalBookCount])}`. Replace it with two tabs:

```jsx
<Tab
  className={styles.tab}
  selectedClassName={styles.selectedTab}
>
  {`Ebooks (${ebookCount})`}
</Tab>

<Tab
  className={styles.tab}
  selectedClassName={styles.selectedTab}
>
  {`Audiobooks (${audiobookCount})`}
</Tab>
```

- [ ] **Step 4: Replace the Books TabPanel with two panels**

Find the first `<TabPanel>` (which renders `<AuthorDetailsSeasonConnector>`). Replace with two panels:

```jsx
<TabPanel>
  <AuthorDetailsSeasonConnector
    authorId={id}
    bookType="ebook"
    isExpanded={true}
    selectedState={selectedState}
    onExpandPress={this.onExpandPress}
    setSelectedState={this.setSelectedState}
    onSelectedChange={this.onSelectedChange}
    isEditorActive={isEditorActive}
  />
</TabPanel>

<TabPanel>
  <AuthorDetailsSeasonConnector
    authorId={id}
    bookType="audiobook"
    isExpanded={true}
    selectedState={selectedState}
    onExpandPress={this.onExpandPress}
    setSelectedState={this.setSelectedState}
    onSelectedChange={this.onSelectedChange}
    isEditorActive={isEditorActive}
  />
</TabPanel>
```

- [ ] **Step 5: Update the Search tab filter icon index**

Find `selectedTabIndex === 3` (the condition that shows the interactive search filter icon). Change it to:

```javascript
selectedTabIndex === 4 &&
```

- [ ] **Step 6: Build the frontend**

```bash
~/.local/bin/yarn build 2>&1 | tail -10
```

Expected: `webpack compiled successfully`

- [ ] **Step 7: Verify in browser**

Restart Readarr, open an author detail page. Confirm:
- Two tabs: "Ebooks (N)" and "Audiobooks (N)" in place of the old Books tab
- Ebooks tab shows only ebook-format books
- Audiobooks tab shows only audiobook-format books
- A book with both types appears in both tabs
- Books with neither type (paperback-only) are hidden from both tabs
- Series, History, Search, Files tabs still work

- [ ] **Step 8: Commit**

```bash
git add frontend/src/Author/Details/AuthorDetails.js
git commit -m "feat: replace Books tab with Ebooks and Audiobooks tabs on author detail page"
```
