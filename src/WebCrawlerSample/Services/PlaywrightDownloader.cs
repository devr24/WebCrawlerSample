using Microsoft.Playwright;
using System;
using System.Threading;
using System.Threading.Tasks;
using WebCrawlerSample.Models;

namespace WebCrawlerSample.Services
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

        public async Task<DownloadResult> GetContent(Uri site, CancellationToken cancellationToken)
        {
            try
            {
                var browser = await _browserTask.ConfigureAwait(false);
                var page = await browser.NewPageAsync();
                var response = await page.GotoAsync(site.ToString(), new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 10000
                });

                string mediaType = null;
                if (response != null && response.Headers.TryGetValue("content-type", out var ct))
                    mediaType = ct;

                var content = await page.ContentAsync();
                await page.CloseAsync();

                if (content.Length > 307_200)
                    return new DownloadResult(null, null, mediaType, "Content too large");

                if (mediaType == null)
                    mediaType = "text/html";

                byte[] data = System.Text.Encoding.UTF8.GetBytes(content);

                if (!mediaType.StartsWith("text/html"))
                    return new DownloadResult(null, data, mediaType, "Content not HTML");

                return new DownloadResult(content, data, mediaType);
            }
            catch (PlaywrightException ex)
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
