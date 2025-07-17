using Polly;
using Polly.Retry;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WebCrawler.Core.Models;

namespace WebCrawler.Core.Services
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

        public async Task<DownloadResult> GetContent(Uri site, int maxDownloadBytes = 1_048_576, CancellationToken cancellationToken = default)
        {
            try
            {
                var client = _clientFactory.CreateClient("crawler");
                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var response = await client.GetAsync(site, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                        return new DownloadResult(null, null, null, $"Status code {(int)response.StatusCode}", response.StatusCode);

                    var mediaType = response.Content.Headers.ContentType?.MediaType;
                    if (string.IsNullOrEmpty(mediaType))
                        mediaType = "text/html";

                    // Skip download if content length is greater than configured max size
                    if (response.Content.Headers.ContentLength.HasValue &&
                        response.Content.Headers.ContentLength.Value > maxDownloadBytes)
                        return new DownloadResult(null, null, mediaType, "Content too large", response.StatusCode);

                    var data = await response.Content.ReadAsByteArrayAsync();

                    string content = null;
                    if (mediaType == "text/html")
                        content = await response.Content.ReadAsStringAsync();

                    if (content == null)
                        return new DownloadResult(null, data, mediaType, "Content not HTML", response.StatusCode);
                    return new DownloadResult(content, data, mediaType, statusCode: response.StatusCode);
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
        Task<DownloadResult> GetContent(Uri site, int maxDownloadBytes = 1_048_576, CancellationToken cancellationToken = default);
    }
}
