using Polly;
using Polly.Retry;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

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
        
        public async Task<string> GetContent(Uri site, CancellationToken cancellationToken)
        {
            try
            {
                // Retry policy could be better - simple example of fault handling.
                var client = _clientFactory.CreateClient("crawler");
                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var response = await client.GetAsync(site, cancellationToken);
                    return await response.Content.ReadAsStringAsync();
                });
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public interface IDownloader
    {
        Task<string> GetContent(Uri site, CancellationToken cancellationToken);
    }
}
