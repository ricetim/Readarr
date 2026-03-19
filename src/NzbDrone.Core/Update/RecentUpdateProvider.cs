using System;
using System.Collections.Generic;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update.History;

namespace NzbDrone.Core.Update
{
    public interface IRecentUpdateProvider
    {
        List<UpdatePackage> GetRecentUpdatePackages();
    }

    public class RecentUpdateProvider : IRecentUpdateProvider
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IUpdatePackageProvider _updatePackageProvider;
        private readonly IUpdateHistoryService _updateHistoryService;

        // Local changelog for this fork — shown in System > Updates and the post-restart modal.
        // The upstream Readarr cloud update API is no longer operational (project retired),
        // so we synthesise a single package entry representing the current build.
        private static readonly UpdatePackage LocalChangelog = new UpdatePackage
        {
            Version = new Version(10, 0, 0, 12827),
            ReleaseDate = new DateTime(2026, 3, 18, 0, 0, 0, DateTimeKind.Utc),
            Branch = "develop",
            Changes = new UpdateChanges
            {
                New = new List<string>
                {
                    "bookinfo: progressive book loading — works appear in UI as Goodreads paginates in the background",
                    "bookinfo: Series data populated from per-work Goodreads URLs",
                    "bookinfo: immediate 'Fetching book list' progress indicator fires on Refresh click",
                    "bookinfo: on_progress callback fires after each pagination page for live updates",
                    "MAM indexer: native support with correct title/author parsing and URL-safe query handling",
                    "BookInfoProxy: direct Goodreads integration via KCA passthrough (no LazyCache/CachedHttpClient)",
                    "AuthorMetadata: Kca column added (migration 041); HttpResponse cache cleared on upgrade",
                    "Author detail page: books always re-fetched on mount for up-to-date edition data"
                },
                Fixed = new List<string>
                {
                    "DateTime parsing for ancient publication dates (e.g. year 79 AD) no longer crashes author refresh",
                    "Never auto-delete books during metadata refresh — partial remote responses are not authoritative",
                    "Seed _pending_complete immediately to prevent redundant fast-path Goodreads fetches",
                    "Prevent duplicate background pagination tasks per author",
                    "IsPartial excluded from Dapper column mapping — eliminates SQLiteException on Authors table",
                    "Handle null author data in RefreshBookService.GetRemoteData",
                    "Populate author ImageUrl from GraphQL profileImageUrl field",
                    "Avoid circular-reference crash in EbookTagService.ReadPdf",
                    "CreateEmptyAuthorFolders setting now respected",
                    "Skip blocking bookinfo fetch at author-add time"
                }
            }
        };

        public RecentUpdateProvider(IConfigFileProvider configFileProvider,
                                    IUpdatePackageProvider updatePackageProvider,
                                    IUpdateHistoryService updateHistoryService)
        {
            _configFileProvider = configFileProvider;
            _updatePackageProvider = updatePackageProvider;
            _updateHistoryService = updateHistoryService;
        }

        public List<UpdatePackage> GetRecentUpdatePackages()
        {
            return new List<UpdatePackage> { LocalChangelog };
        }
    }
}
