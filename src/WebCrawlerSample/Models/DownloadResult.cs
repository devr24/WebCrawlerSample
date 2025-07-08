namespace WebCrawlerSample.Models
{
    public class DownloadResult
    {
        public string Content { get; }
        public byte[] Data { get; }
        public string MediaType { get; }

        public DownloadResult(string content, byte[] data, string mediaType)
        {
            Content = content;
            Data = data;
            MediaType = mediaType;
        }

        public bool IsHtml => MediaType?.StartsWith("text/html") == true;
    }
}
