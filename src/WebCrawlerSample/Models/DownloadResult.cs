namespace WebCrawlerSample.Models
{
    public class DownloadResult
    {
        public string Content { get; }
        public byte[] Data { get; }
        public string MediaType { get; }
        public string Error { get; }

        public DownloadResult(string content, byte[] data, string mediaType, string error = null)
        {
            Content = content;
            Data = data;
            MediaType = mediaType;
            Error = error;
        }

        public bool IsHtml => MediaType?.StartsWith("text/html") == true;
    }
}
