using System.Collections.Generic;

namespace NzbDrone.Core.Parser.Model
{
    /// <summary>
    /// Display-only metadata about a release, shown in the interactive search dialog.
    /// Populated only by indexers that return it; null otherwise.
    /// </summary>
    public class ReleaseDetails
    {
        public List<string> Narrators { get; set; }
        public int? FileCount { get; set; }
        public string Description { get; set; }
        public string Series { get; set; }
        public string Tags { get; set; }
        public string Category { get; set; }
    }
}
