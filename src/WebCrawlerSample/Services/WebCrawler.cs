using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebCrawlerSample.Models;

namespace WebCrawlerSample.Services
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

        public event EventHandler<CrawledPage> PageCrawled; // event

        public WebCrawler(IDownloader downloader, IHtmlParser parser)
        {
            _downloader = downloader;
            _parser = parser;
        }

        // Crawl start method.
        public async Task<CrawlResult> RunAsync(string startUrl, int maxDepth = 1, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var page))
                throw new ArgumentException("Uri is not valid", nameof(startUrl));

            _pagesVisited.Clear(); // reset.

            // Measure time to run.
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            await CrawlPages(page, maxDepth, cancellationToken: cancellationToken);

            watch.Stop();
            return new CrawlResult(page, maxDepth, GetOrderedPages(), watch.Elapsed);
        }

        private async Task CrawlPages(Uri startPage, int maxDepth, CancellationToken cancellationToken = default)
        {
            var queue = new Queue<(Uri page, int depth)>();
            queue.Enqueue((startPage, 1));
            _pagesVisited.TryAdd(startPage.ToString(), null);

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (currentPage, depth) = queue.Dequeue();

                await _downloadSemaphore.WaitAsync(cancellationToken);
                string content = null;
                try
                {
                    content = await _downloader.GetContent(currentPage, cancellationToken);
                }
                finally
                {
                    _downloadSemaphore.Release();
                }

                List<string> links = null;
                if (content != null)
                    links = _parser.FindLinks(content, currentPage);

                var crawledPage = new CrawledPage(currentPage, depth, links);
                _pagesVisited.AddOrUpdate(currentPage.ToString(), crawledPage, (k, v) => crawledPage);

                PageCrawled?.Invoke(this, crawledPage);

                if (depth >= maxDepth || links == null)
                    continue;

                foreach (var link in links)
                {
                    if (Uri.TryCreate(link, UriKind.Absolute, out var linkUri) &&
                        linkUri.Host == startPage.Host &&
                        !_pagesVisited.ContainsKey(link))
                    {
                        _pagesVisited.TryAdd(link, null);
                        queue.Enqueue((linkUri, depth + 1));
                    }
                }
            }
        }

        private Dictionary<string, CrawledPage> GetOrderedPages()
        {
            return _pagesVisited.OrderBy(c => c.Value.FirstVisitedDepth)
                .ThenBy(n => n.Key)
                .ToDictionary(k => k.Key, k => k.Value);
        }
    }
}
