using System.Collections.Generic;

namespace Readarr.Api.V1.Indexers
{
    public class ReleaseDetailsResource
    {
        public List<string> Narrators { get; set; }
        public int? FileCount { get; set; }
        public string Description { get; set; }
        public string Series { get; set; }
        public string Tags { get; set; }
        public string Category { get; set; }
    }
}
