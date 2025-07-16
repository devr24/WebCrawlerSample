using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WebCrawler.Core.Models;
using HtmlAgilityPack;

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
        private readonly ConcurrentQueue<RetryItem> _retryQueue = new();
        private const int Max429Retries = 3;

        private record RetryItem(Uri Page, int Depth, int Attempt);
        private record CrawlPageResult(Uri Page, List<Uri> Links, bool Retry429);

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

        private static HashSet<string> BuildIgnoreSet(IEnumerable<string> ignoreLinks, Uri baseUri)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ignoreLinks == null)
                return set;

            foreach (var link in ignoreLinks)
            {
                if (string.IsNullOrWhiteSpace(link))
                    continue;

                if (Uri.TryCreate(link, UriKind.Absolute, out var abs))
                    set.Add(GetPageKey(abs));
                else if (Uri.TryCreate(baseUri, link, out var rel))
                    set.Add(GetPageKey(rel));
            }

            return set;
        }

        // Crawl start method.
        public async Task<CrawlResult> RunAsync(string startUrl, int maxDepth = 1, bool downloadFiles = false, string downloadFolder = null, int maxDownloadBytes = 307_200, bool cleanContent = false, IEnumerable<string> ignoreLinks = null, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var page))
                throw new ArgumentException("Uri is not valid", nameof(startUrl));

            var ignoreSet = BuildIgnoreSet(ignoreLinks, page);

            _pagesVisited.Clear(); // reset.
            CrawlStarted?.Invoke(this, page);

            // Measure time to run.
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            if (string.IsNullOrWhiteSpace(downloadFolder))
            {
                downloadFolder = $"run-{DateTime.UtcNow:yyyyMMddHHmmss}";
            }

            if (downloadFiles)
            {
                System.IO.Directory.CreateDirectory(downloadFolder);
            }

            await CrawlPages(page, maxDepth, downloadFiles, downloadFolder, maxDownloadBytes, cleanContent, ignoreSet, cancellationToken: cancellationToken);

            watch.Stop();
            var result = new CrawlResult(page, maxDepth, GetOrderedPages(), watch.Elapsed);
            CrawlCompleted?.Invoke(this, result);
            return result;
        }

        private async Task CrawlPages(Uri startPage, int maxDepth, bool downloadFiles, string downloadFolder, int maxDownloadBytes, bool cleanContent, HashSet<string> ignoreSet, CancellationToken cancellationToken = default)
        {
            var currentLevel = new List<Uri> { startPage };
            _pagesVisited.TryAdd(GetPageKey(startPage), null);
            var depth = 1;

            while ((currentLevel.Count > 0 || !_retryQueue.IsEmpty) && depth <= maxDepth)
            {
                var tasks = currentLevel.Select(p => CrawlPage(p, startPage, depth, downloadFiles, downloadFolder, maxDownloadBytes, cleanContent, 1, ignoreSet, cancellationToken)).ToList();
                var results = await Task.WhenAll(tasks);

                var nextLevel = new List<Uri>();
                foreach (var result in results)
                {
                    if (result == null)
                        continue;

                    if (result.Retry429)
                    {
                        _retryQueue.Enqueue(new RetryItem(result.Page, depth, 1));
                        continue;
                    }

                    foreach (var link in result.Links ?? Enumerable.Empty<Uri>())
                    {
                        if (_pagesVisited.TryAdd(GetPageKey(link), null))
                            nextLevel.Add(link);
                    }
                }

                while (_retryQueue.TryDequeue(out var item))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * item.Attempt), cancellationToken);
                    var res = await CrawlPage(item.Page, startPage, item.Depth, downloadFiles, downloadFolder, maxDownloadBytes, cleanContent, item.Attempt + 1, ignoreSet, cancellationToken);
                    if (res == null)
                        continue;

                    if (res.Retry429)
                    {
                        if (item.Attempt + 1 < Max429Retries)
                            _retryQueue.Enqueue(new RetryItem(item.Page, item.Depth, item.Attempt + 1));
                        else
                            await CrawlPage(item.Page, startPage, item.Depth, downloadFiles, downloadFolder, maxDownloadBytes, cleanContent, Max429Retries, ignoreSet, cancellationToken); // final attempt records error
                        continue;
                    }

                    foreach (var link in res.Links ?? Enumerable.Empty<Uri>())
                    {
                        var key = GetPageKey(link);
                        if (ignoreSet.Contains(key))
                            continue;
                        if (_pagesVisited.TryAdd(key, null))
                            nextLevel.Add(link);
                    }
                }

                depth++;
                currentLevel = nextLevel;
            }
        }

        private async Task<CrawlPageResult> CrawlPage(Uri currentPage, Uri rootPage, int depth, bool downloadFiles, string downloadFolder, int maxDownloadBytes, bool cleanContent, int attempt, HashSet<string> ignoreSet, CancellationToken cancellationToken)
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

            if (downloadResult?.StatusCode == HttpStatusCode.TooManyRequests && attempt < Max429Retries)
            {
                return new CrawlPageResult(currentPage, null, true);
            }

            var totalLinks = 0;
            List<string> links = null;
            if (downloadResult?.Content != null)
            {
                links = _parser.FindLinks(downloadResult.Content, currentPage);
                if (links != null)
                {
                    totalLinks = links.Count;
                    links = links.Where(l =>
                    {
                        if (!Uri.TryCreate(l, UriKind.Absolute, out var linkUri))
                            return true;

                        var key = GetPageKey(linkUri);
                        if (ignoreSet.Contains(key))
                            return false;

                        return !_pagesVisited.ContainsKey(key);
                    }).ToList();
                }
            }

            var isCloudflareProtected = downloadResult?.Content != null &&
                downloadResult.Content.IndexOf("protected by cloudflare", StringComparison.OrdinalIgnoreCase) >= 0;

            var isPdf = currentPage.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            if ((downloadFiles || isPdf) && downloadResult?.Data != null && !isCloudflareProtected)
            {
                if (!System.IO.Directory.Exists(downloadFolder))
                    System.IO.Directory.CreateDirectory(downloadFolder);
                var fileName = GenerateFileName(currentPage, downloadResult.IsHtml);
                var filePath = System.IO.Path.Combine(downloadFolder, fileName);
                var data = downloadResult.Data;
                if (cleanContent && downloadResult.IsHtml && downloadResult.Content != null)
                {
                    var cleaned = CleanHtml(downloadResult.Content);
                    data = System.Text.Encoding.UTF8.GetBytes(cleaned);
                }
                await System.IO.File.WriteAllBytesAsync(filePath, data, cancellationToken);
            }

            var pageKey = GetPageKey(currentPage);
            var crawledPage = new CrawledPage(currentPage, depth, totalLinks, links, downloadResult?.Error);
            _pagesVisited.AddOrUpdate(pageKey, crawledPage, (k, v) => crawledPage);

            PageCrawled?.Invoke(this, crawledPage);

            if (links == null || depth >= int.MaxValue)
                return new CrawlPageResult(currentPage, null, false);

            var result = new List<Uri>();
            foreach (var link in links)
            {
                if (Uri.TryCreate(link, UriKind.Absolute, out var linkUri) && linkUri.Host == rootPage.Host)
                {
                    var key = GetPageKey(linkUri);
                    if (ignoreSet.Contains(key))
                        continue;
                    result.Add(linkUri);
                }
            }

            return new CrawlPageResult(currentPage, result, false);
        }

        private Dictionary<string, CrawledPage> GetOrderedPages()
        {
            return _pagesVisited.OrderBy(c => c.Value?.FirstVisitedDepth)
                .ThenBy(n => n.Key)
                .ToDictionary(k => k.Key, k => k.Value);
        }

        private static string CleanHtml(string html)
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);
            var removeNodes = doc.DocumentNode.SelectNodes("//header|//footer|//nav|//script|//style");
            if (removeNodes != null)
            {
                foreach (var n in removeNodes)
                    n.Remove();
            }
            return doc.DocumentNode.InnerText;
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
                else
                {
                    path += ".pdf";
                }
            }

            return path;
        }
    }
}
