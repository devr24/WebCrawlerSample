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

        // O(n) + O(n) + O(1) + O(n) = 3O(n) + O(1) => O(n)
        private async Task CrawlPages(Uri currentPage, int maxDepth, int currentDepth = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // O(n) - worst case
            _pagesVisited.TryAdd(currentPage.ToString(), null); // optimisation - add straight away to avoid revisiting.

            currentDepth++;

            // Get currentPage content.
            var content = await _downloader.GetContent(currentPage, cancellationToken);
            List<string> links = null;

            if (content != null) // O(n)
                links = _parser.FindLinks(content, currentPage); 

            var crawledPage = new CrawledPage(currentPage, currentDepth, links);

            // O(1)
            _pagesVisited.TryUpdate(currentPage.ToString(), crawledPage, null);

            PageCrawled?.Invoke(this, crawledPage); // raise crawled event!

            // If at limit of currentDepth, then go no further.
            if (currentDepth >= maxDepth) return;

            if (links != null)
            {
                // O(n)
                var tasks = links.Select(l => CrawlSubPage(l, currentPage, maxDepth, currentDepth, cancellationToken));
                await Task.WhenAll(tasks);
            }
        }

        private async Task CrawlSubPage(string linkToVisit, Uri currentPage, int maxDepth, int currentDepth, CancellationToken cancellationToken)
        {
            var isValidUri = Uri.TryCreate(linkToVisit, UriKind.Absolute, out var linkUri);

            if (isValidUri &&                             // only visit valid addresses.
                linkUri.Host == currentPage.Host &&       // only visit pages with same domain.
                !_pagesVisited.ContainsKey(linkToVisit))  // If already visited then don't revisit.
            {
                await CrawlPages(linkUri, maxDepth, currentDepth, cancellationToken);
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
