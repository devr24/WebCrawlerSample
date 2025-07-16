namespace WebCrawler.Core.Models
{
    public class DownloadResult
    {
        public string Content { get; }
        public byte[] Data { get; }
        public string MediaType { get; }
        public string Error { get; }
        public System.Net.HttpStatusCode? StatusCode { get; }

        public DownloadResult(string content, byte[] data, string mediaType, string error = null,
            System.Net.HttpStatusCode? statusCode = null)
        {
            Content = content;
            Data = data;
            MediaType = mediaType;
            Error = error;
            StatusCode = statusCode;
        }

        public bool IsHtml => MediaType?.StartsWith("text/html") == true;
    }
}
