using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(042)]
    public class remove_release_group : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Delete.Column("ReleaseGroup").FromTable("BookFiles");
        }
    }
}
