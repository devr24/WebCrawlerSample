using Polly;
using Polly.Retry;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace WebCrawlerSample.Services
{
    public class Downloader
    {
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly HttpClient _client;

        public Downloader(HttpMessageHandler handler = null)
        {
            _client = (handler == null ?
                new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }, disposeHandler: false) :
                new HttpClient(handler, disposeHandler: false));

            _retryPolicy = Policy.Handle<HttpRequestException>()
                .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(300)); // Retry 3 times, with 300 millisecond delay.
        }

        public async Task<string> GetContent(Uri site)
        {
            try
            {
                // Retry policy could be better - simple example of fault handling.
                return await _retryPolicy.ExecuteAsync(async () =>
                {
                    var response = await _client.GetAsync(site);
                    return await response.Content.ReadAsStringAsync();
                });

            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
