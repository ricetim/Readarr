using System.Collections.Generic;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouseTorrent
    {
        public string Id { get; set; }
        public string Language { get; set; }
        public string Title { get; set; }
        public string Added { get; set; }
        public string Size { get; set; }
        public string Seeders { get; set; }
        public string Leechers { get; set; }
        public string Free { get; set; }
        public string Fl_Vip { get; set; }
        public string Main_Cat { get; set; }
        public string Category { get; set; }
        public string Catname { get; set; }
        public string Author_Info { get; set; }
        public string Narrator_Info { get; set; }
        public string Series_Info { get; set; }
        public string Tags { get; set; }
        public string Filetype { get; set; }
        public string Numfiles { get; set; }
        public string Description { get; set; }
        public string Dl { get; set; }
        public string Times_Completed { get; set; }
    }

    public class MyAnonamouseResponse
    {
        public List<MyAnonamouseTorrent> Data { get; set; }
        public int Total { get; set; }
        public int Total_Found { get; set; }
        public string Error { get; set; }
    }

    public class MyAnonamouseInfo : TorrentInfo
    {
    }
}
