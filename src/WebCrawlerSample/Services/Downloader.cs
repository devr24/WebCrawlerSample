using Polly;
using Polly.Retry;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WebCrawlerSample.Models;

namespace WebCrawlerSample.Services
{
    public class Downloader : IDownloader
    {
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IHttpClientFactory _clientFactory;

        public Downloader(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;

            _retryPolicy = Policy.Handle<HttpRequestException>()
                .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(300)); // Retry 3 times, with 300 millisecond delay.
        }
        
        public async Task<DownloadResult> GetContent(Uri site, CancellationToken cancellationToken)
        {
            try
            {
                var client = _clientFactory.CreateClient("crawler");
                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var response = await client.GetAsync(site, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        return new DownloadResult(null, null, null, $"Status code {(int)response.StatusCode}");

                    var mediaType = response.Content.Headers.ContentType?.MediaType;

                    // Skip download if content length is greater than 300 KB
                    if (response.Content.Headers.ContentLength.HasValue &&
                        response.Content.Headers.ContentLength.Value > 307_200)
                        return new DownloadResult(null, null, mediaType, "Content too large");

                    var data = await response.Content.ReadAsByteArrayAsync();

                    string content = null;
                    if (mediaType == "text/html")
                        content = await response.Content.ReadAsStringAsync();

                    if (content == null)
                        return new DownloadResult(null, data, mediaType, "Content not HTML");

                    return new DownloadResult(content, data, mediaType);
                });
            }
            catch (Exception ex)
            {
                return new DownloadResult(null, null, null, ex.Message);
            }
        }
    }

    public interface IDownloader
    {
        Task<DownloadResult> GetContent(Uri site, CancellationToken cancellationToken);
    }
}
