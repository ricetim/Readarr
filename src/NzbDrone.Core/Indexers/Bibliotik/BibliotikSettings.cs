using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Bibliotik
{
    public class BibliotikSettingsValidator : AbstractValidator<BibliotikSettings>
    {
        public BibliotikSettingsValidator()
        {
            RuleFor(c => c.Cookie).NotEmpty();
            RuleFor(c => c.SeedCriteria).SetValidator(_ => new SeedCriteriaSettingsValidator());
        }
    }

    public class BibliotikSettings : ITorrentIndexerSettings
    {
        private static readonly BibliotikSettingsValidator Validator = new BibliotikSettingsValidator();

        public BibliotikSettings()
        {
            BaseUrl = "https://bibliotik.me";
            MinimumSeeders = IndexerDefaults.MINIMUM_SEEDERS;
        }

        public string BaseUrl { get; set; }

        [FieldDefinition(0, Label = "Session Cookie", Privacy = PrivacyLevel.ApiKey, HelpText = "Your Bibliotik session cookie. In Chrome/Edge: DevTools (F12) → Application → Cookies → https://bibliotik.me → copy the Value of the 'id' cookie. Paste the value exactly as shown — do not decode it.")]
        public string Cookie { get; set; }

        [FieldDefinition(1, Type = FieldType.Number, Label = "Early Download Limit", Unit = "days", HelpText = "Time before release date Readarr will download from this indexer, empty is no limit", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        [FieldDefinition(2, Type = FieldType.Textbox, Label = "Minimum Seeders", HelpText = "Minimum number of seeders required.", Advanced = true)]
        public int MinimumSeeders { get; set; }

        [FieldDefinition(3)]
        public SeedCriteriaSettings SeedCriteria { get; set; } = new SeedCriteriaSettings();

        [FieldDefinition(4, Type = FieldType.Checkbox, Label = "Reject Blocklisted Torrent Hashes While Grabbing", HelpText = "If a torrent is blocked by hash it may not properly be rejected during RSS/Search for some indexers, enabling this will allow it to be rejected after the torrent is grabbed, but before it is sent to the client.", Advanced = true)]
        public bool RejectBlocklistedTorrentHashesWhileGrabbing { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
