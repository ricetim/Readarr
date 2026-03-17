using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(041)]
    public class add_kca_to_author_metadata : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("AuthorMetadata").AddColumn("Kca").AsString().Nullable();
        }

        protected override void CacheDbUpgrade()
        {
            Delete.FromTable("HttpResponse").AllRows();
        }
    }
}
