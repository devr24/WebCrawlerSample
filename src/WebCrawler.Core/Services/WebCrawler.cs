using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebCrawler.Core.Models;

namespace WebCrawler.Core.Services
{
    /// <summary>
    /// Service used to crawl web pages starting from a root URL.
    /// </summary>
    public class WebCrawler
    {
        private readonly ConcurrentDictionary<string, CrawledPage> _pagesVisited = new ConcurrentDictionary<string, CrawledPage>();
        private readonly IDownloader _downloader;
        private readonly IHtmlParser _parser;
        private readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(5);

        public event EventHandler<Uri> CrawlStarted;
        public event EventHandler<CrawledPage> PageCrawled;
        public event EventHandler<CrawlResult> CrawlCompleted;

        public WebCrawler(IDownloader downloader, IHtmlParser parser)
        {
            _downloader = downloader;
            _parser = parser;
        }

        private static string GetPageKey(Uri uri)
        {
            var builder = new UriBuilder(uri) { Fragment = string.Empty };
            return builder.Uri.ToString();
        }

        // Crawl start method.
        public async Task<CrawlResult> RunAsync(string startUrl, int maxDepth = 1, bool downloadFiles = false, string downloadFolder = null, int maxDownloadBytes = 307_200, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var page))
                throw new ArgumentException("Uri is not valid", nameof(startUrl));

            _pagesVisited.Clear(); // reset.
            CrawlStarted?.Invoke(this, page);

            // Measure time to run.
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            if (downloadFiles && string.IsNullOrWhiteSpace(downloadFolder))
            {
                downloadFolder = $"run-{DateTime.UtcNow:yyyyMMddHHmmss}";
            }

            if (downloadFiles)
            {
                System.IO.Directory.CreateDirectory(downloadFolder);
            }

            await CrawlPages(page, maxDepth, downloadFiles, downloadFolder, maxDownloadBytes, cancellationToken: cancellationToken);

            watch.Stop();
            var result = new CrawlResult(page, maxDepth, GetOrderedPages(), watch.Elapsed);
            CrawlCompleted?.Invoke(this, result);
            return result;
        }

        private async Task CrawlPages(Uri startPage, int maxDepth, bool downloadFiles, string downloadFolder, int maxDownloadBytes, CancellationToken cancellationToken = default)
        {
            var currentLevel = new List<Uri> { startPage };
            _pagesVisited.TryAdd(GetPageKey(startPage), null);
            var depth = 1;

            while (currentLevel.Count > 0 && depth <= maxDepth)
            {
                var tasks = currentLevel.Select(p => CrawlPage(p, startPage, depth, downloadFiles, downloadFolder, maxDownloadBytes, cancellationToken)).ToList();
                var results = await Task.WhenAll(tasks);

                var nextLevel = new List<Uri>();
                foreach (var links in results)
                {
                    if (links == null)
                        continue;

                    foreach (var link in links)
                    {
                        if (_pagesVisited.TryAdd(GetPageKey(link), null))
                            nextLevel.Add(link);
                    }
                }

                depth++;
                currentLevel = nextLevel;
            }
        }

        private async Task<List<Uri>> CrawlPage(Uri currentPage, Uri rootPage, int depth, bool downloadFiles, string downloadFolder, int maxDownloadBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _downloadSemaphore.WaitAsync(cancellationToken);
            DownloadResult downloadResult = null;
            try
            {
                downloadResult = await _downloader.GetContent(currentPage, maxDownloadBytes, cancellationToken);
            }
            finally
            {
                _downloadSemaphore.Release();
            }

            List<string> links = null;
            if (downloadResult?.Content != null)
            {
                links = _parser.FindLinks(downloadResult.Content, currentPage);
                if (links != null)
                {
                    links = links.Where(l =>
                    {
                        if (!Uri.TryCreate(l, UriKind.Absolute, out var linkUri))
                            return true;

                        var key = GetPageKey(linkUri);
                        return !_pagesVisited.ContainsKey(key);
                    }).ToList();
                }
            }

            var isCloudflareProtected = downloadResult?.Content != null &&
                downloadResult.Content.IndexOf("protected by cloudflare", StringComparison.OrdinalIgnoreCase) >= 0;

            if (downloadFiles && downloadResult?.Data != null && !isCloudflareProtected)
            {
                var fileName = GenerateFileName(currentPage, downloadResult.IsHtml);
                var filePath = System.IO.Path.Combine(downloadFolder, fileName);
                await System.IO.File.WriteAllBytesAsync(filePath, downloadResult.Data, cancellationToken);
            }

            var pageKey = GetPageKey(currentPage);
            var crawledPage = new CrawledPage(currentPage, depth, links, downloadResult?.Error);
            _pagesVisited.AddOrUpdate(pageKey, crawledPage, (k, v) => crawledPage);

            PageCrawled?.Invoke(this, crawledPage);

            if (links == null || depth >= int.MaxValue)
                return null;

            var result = new List<Uri>();
            foreach (var link in links)
            {
                if (Uri.TryCreate(link, UriKind.Absolute, out var linkUri) && linkUri.Host == rootPage.Host)
                    result.Add(linkUri);
            }

            return result;
        }

        private Dictionary<string, CrawledPage> GetOrderedPages()
        {
            return _pagesVisited.OrderBy(c => c.Value.FirstVisitedDepth)
                .ThenBy(n => n.Key)
                .ToDictionary(k => k.Key, k => k.Value);
        }

        private static string GenerateFileName(Uri uri, bool isHtml)
        {
            // remove the host portion of the URL to generate cleaner file names
            var path = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrWhiteSpace(path))
                path = "root";

            var invalid = System.IO.Path.GetInvalidFileNameChars();
            foreach (var ch in invalid)
                path = path.Replace(ch, '_');
            path = path.Replace('/', '_').Replace('\\', '_').Replace(':', '_').Replace('?', '_').Replace('&', '_').Replace('=', '_');

            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                var q = uri.Query.Trim('?');
                foreach (var ch in invalid)
                    q = q.Replace(ch, '_');
                q = q.Replace('/', '_').Replace('\\', '_').Replace(':', '_').Replace('?', '_').Replace('&', '_').Replace('=', '_');
                path += "_" + q;
            }

            if (isHtml && !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                path += ".html";
            else if (!System.IO.Path.HasExtension(path))
            {
                var ext = System.IO.Path.GetExtension(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(ext))
                    path += ext;
            }

            return path;
        }
    }
}
