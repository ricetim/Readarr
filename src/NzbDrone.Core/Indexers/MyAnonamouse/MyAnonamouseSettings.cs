using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouseSettingsValidator : AbstractValidator<MyAnonamouseSettings>
    {
        public MyAnonamouseSettingsValidator()
        {
            RuleFor(c => c.Cookie).NotEmpty();

            RuleFor(c => c.SeedCriteria).SetValidator(_ => new SeedCriteriaSettingsValidator());
        }
    }

    public class MyAnonamouseSettings : ITorrentIndexerSettings
    {
        private static readonly MyAnonamouseSettingsValidator Validator = new MyAnonamouseSettingsValidator();

        public MyAnonamouseSettings()
        {
            MinimumSeeders = IndexerDefaults.MINIMUM_SEEDERS;
        }

        public string BaseUrl { get; set; } = "https://www.myanonamouse.net";

        [FieldDefinition(0, Label = "Session Cookie", Privacy = PrivacyLevel.ApiKey, HelpText = "Your mam_id cookie value from a logged-in browser session.")]
        public string Cookie { get; set; }

        [FieldDefinition(1, Label = "Search Type", Type = FieldType.Select, SelectOptions = typeof(MyAnonamouseSearchType), HelpText = "Whether to include torrents with no active seeders.", Advanced = true)]
        public int SearchType { get; set; }

        [FieldDefinition(2, Type = FieldType.Number, Label = "Early Download Limit", Unit = "days", HelpText = "Time before release date Readarr will download from this indexer, empty is no limit", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        [FieldDefinition(3, Type = FieldType.Textbox, Label = "Minimum Seeders", HelpText = "Minimum number of seeders required.", Advanced = true)]
        public int MinimumSeeders { get; set; }

        [FieldDefinition(4)]
        public SeedCriteriaSettings SeedCriteria { get; set; } = new SeedCriteriaSettings();

        [FieldDefinition(5, Type = FieldType.Checkbox, Label = "Reject Blocklisted Torrent Hashes While Grabbing", HelpText = "If a torrent is blocked by hash it may not properly be rejected during RSS/Search for some indexers, enabling this will allow it to be rejected after the torrent is grabbed, but before it is sent to the client.", Advanced = true)]
        public bool RejectBlocklistedTorrentHashesWhileGrabbing { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }

    public enum MyAnonamouseSearchType
    {
        [FieldOption(Label = "Active (Has Seeders)")]
        Active = 0,

        [FieldOption(Label = "All")]
        All = 1,

        [FieldOption(Label = "Inactive (No Seeders)")]
        Inactive = 2,
    }
}
