using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebCrawlerSample.Models;

namespace WebCrawlerSample.Services
{
    public class WebCrawer
    {
        private readonly ConcurrentDictionary<string, CrawledPage> _pagesVisited = new ConcurrentDictionary<string, CrawledPage>();
        private readonly Downloader _downloader;
        private readonly HtmlParser _parser;

        public event EventHandler<CrawledPage> PageCrawled; // event

        public WebCrawer(Downloader downloader, HtmlParser parser)
        {
            _downloader = downloader;
            _parser = parser;
        }

        // Crawl start method.
        public async Task<CrawlResult> RunAsync(string startUrl, int maxDepth = 1)
        {
            if (!Uri.TryCreate(startUrl, UriKind.Absolute, out var page))
                throw new ArgumentException("Uri is not valid", nameof(startUrl));

            _pagesVisited.Clear(); // reset.

            // Measure time to run.
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();

            await CrawlPages(page, maxDepth);
            
            watch.Stop();
            return new CrawlResult(page, maxDepth, GetOrderedPages(), watch.Elapsed);
        }

        private async Task CrawlPages(Uri currentPage, int maxDepth, int currentDepth = 0)
        {
            _pagesVisited.TryAdd(currentPage.ToString(), null); // optimisation - add straight away to avoid revisiting.

            currentDepth++;

            // Get currentPage content.
            var content = await _downloader.GetContent(currentPage);
            List<string> links = null;

            if (content != null)
                links = _parser.FindLinks(content, currentPage); 

            var crawledPage = new CrawledPage(currentPage, currentDepth, links);

            _pagesVisited.TryUpdate(currentPage.ToString(), crawledPage, null);

            PageCrawled?.Invoke(this, crawledPage); // raise crawled event!

            // If at limit of currentDepth, then go no further.
            if (currentDepth >= maxDepth) return;

            if (links != null)
            {
                var tasks = links.Select(l => CrawlSubPage(l, currentPage, maxDepth, currentDepth));
                await Task.WhenAll(tasks);
            }
        }

        private async Task CrawlSubPage(string linkToVisit, Uri currentPage, int maxDepth, int currentDepth)
        {
            var isValidUri = Uri.TryCreate(linkToVisit, UriKind.Absolute, out var linkUri);

            if (isValidUri &&                             // only visit valid addresses.
                linkUri.Host == currentPage.Host &&       // only visit pages with same domain.
                !_pagesVisited.ContainsKey(linkToVisit))  // If already visited then don't revisit.
            {
                await CrawlPages(linkUri, maxDepth, currentDepth);
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
