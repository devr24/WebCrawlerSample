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

        public async Task<DownloadResult> GetContent(Uri site, int maxDownloadBytes = 307_200, CancellationToken cancellationToken = default)
        {
            try
            {
                var browser = await _browserTask.ConfigureAwait(false);
                var page = await browser.NewPageAsync();
                IResponse response = null;
                int attempt = 0;
                do
                {
                    response = await page.GotoAsync(site.ToString(), new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.NetworkIdle,
                        Timeout = 30000
                    });

                    if (response != null && response.Status == 429)
                    {
                        var delay = TimeSpan.FromSeconds(1);
                        if (response.Headers.TryGetValue("retry-after", out var retryAfter) &&
                            int.TryParse(retryAfter, out var secs))
                            delay = TimeSpan.FromSeconds(secs);

                        await Task.Delay(delay, cancellationToken);
                        attempt++;
                    }
                    else
                        break;
                } while (attempt < 3);


                if (response == null || !response.Ok)
                {
                    await page.CloseAsync();
                    return new DownloadResult(null, null, null, $"Status code {response?.Status}");
                }

                string mediaType = null;
                if (response.Headers.TryGetValue("content-type", out var ct))
                    mediaType = ct;

                var content = await page.ContentAsync();
                await page.CloseAsync();

                if (content.Length > maxDownloadBytes)
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
