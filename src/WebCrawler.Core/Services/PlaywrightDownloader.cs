using Microsoft.Playwright;
using System;
using System.Threading;
using System.Threading.Tasks;
using WebCrawler.Core.Models;

namespace WebCrawler.Core.Services
{
    public class PlaywrightDownloader : IDownloader, IAsyncDisposable
    {
        private readonly Task<IBrowser> _browserTask;

        public PlaywrightDownloader()
        {
            _browserTask = InitializeAsync();
        }

        private static async Task<IBrowser> InitializeAsync()
        {
            var playwright = await Playwright.CreateAsync();
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }

        public async Task<DownloadResult> GetContent(Uri site, int maxDownloadBytes = 1_048_576, CancellationToken cancellationToken = default)
        {
            try
            {
                var browser = await _browserTask.ConfigureAwait(false);
                var page = await browser.NewPageAsync();
                var response = await page.GotoAsync(site.ToString(), new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });


                if (response == null || !response.Ok)
                {
                    await page.CloseAsync();
                    return new DownloadResult(null, null, null, $"Status code {response?.Status}",
                        response != null ? (System.Net.HttpStatusCode)response.Status : null);
                }

                string mediaType = null;
                if (response.Headers.TryGetValue("content-type", out var ct))
                    mediaType = ct;

                var content = await page.ContentAsync();
                await page.CloseAsync();

                if (content.Length > maxDownloadBytes)
                    return new DownloadResult(null, null, mediaType, "Content too large",
                        (System.Net.HttpStatusCode)response.Status);

                if (mediaType == null)
                    mediaType = "text/html";

                byte[] data = System.Text.Encoding.UTF8.GetBytes(content);

                if (!mediaType.StartsWith("text/html"))
                    return new DownloadResult(null, data, mediaType, "Content not HTML",
                        (System.Net.HttpStatusCode)response.Status);
                return new DownloadResult(content, data, mediaType,
                    statusCode: (System.Net.HttpStatusCode)response.Status);
            }
            catch (PlaywrightException ex)
            {
                return new DownloadResult(null, null, null, ex.Message);
            }
            catch (TimeoutException ex)
            {
                return new DownloadResult(null, null, null, ex.Message);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_browserTask.IsCompletedSuccessfully)
            {
                await (await _browserTask).DisposeAsync();
            }
        }
    }
}
