namespace WebCrawlerSample
{
    public class RunProfile
    {
        public string Website { get; set; }
        public bool UseSitemap { get; set; }
        public int Depth { get; set; } = 2;
        public System.Collections.Generic.List<string> IgnoreLinks { get; set; }
        public bool CleanContent { get; set; }
        public StorageOptions Storage { get; set; }
    }

    public class StorageOptions
    {
        public string Type { get; set; } // "Local" or "Blob"
        public string Path { get; set; }
        public string ConnectionString { get; set; }
        public string Container { get; set; }
    }
}
