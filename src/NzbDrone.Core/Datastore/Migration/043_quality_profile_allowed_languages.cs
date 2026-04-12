using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(043)]
    public class quality_profile_allowed_languages : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QualityProfiles").AddColumn("AllowedLanguages").AsString().WithDefaultValue("[]");
        }
    }
}
