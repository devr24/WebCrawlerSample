using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using System.Net.Http;
using WebCrawlerSample.Models;
using WebCrawlerSample.Services;

namespace WebCrawlerSample
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            // default arguments before grabbing from args.
            var baseUrl = "https://www.crawler-test.com/";
            var downloadFiles = false;
            var maxDepth = 3;

            if (args.Length > 0) baseUrl = args[0];
            if (args.Length > 1) bool.TryParse(args[1], out downloadFiles);
            if (args.Length > 2) maxDepth = Convert.ToInt32(args[2]);

            // Setup dependencies for the crawler.
            var services = new ServiceCollection();

            var retryPolicy = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(300));

            services.AddHttpClient("crawler", c =>
            {
                c.Timeout = TimeSpan.FromSeconds(5);
            }).AddPolicyHandler(retryPolicy);

            services.AddSingleton<IDownloader, Downloader>();
            services.AddSingleton<IHtmlParser, HtmlParser>();

            var provider = services.BuildServiceProvider();

            var downloader = provider.GetRequiredService<IDownloader>();
            var parser = provider.GetRequiredService<IHtmlParser>();

            // Initialise the crawler and hook into the crawled event.
            var crawler = new WebCrawler(downloader, parser);
            crawler.PageCrawled += (obj, page) => Console.WriteLine(FormatOutput(page));

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"Crawling {baseUrl} to depth {maxDepth}\n");

            // Run the crawler!
            var result = await crawler.RunAsync(baseUrl, maxDepth, downloadFiles, null, cts.Token);

            Console.WriteLine($"Max depth: {result.MaxDepth}");
            Console.WriteLine($"Total links visited: {result.Links.Keys.Count}");
            Console.WriteLine("Total crawl execution time: {0:00}:{1:00}.{2:00}", result.RunTime.TotalMinutes, result.RunTime.Seconds, result.RunTime.Milliseconds / 10);
        }

        public static string FormatOutput(CrawledPage page)
        {
            string linksDisplay;

            if (page.PageLinks == null)
            {
                var reason = string.IsNullOrEmpty(page.Error) ? string.Empty : $" [{page.Error}]";
                linksDisplay = $"Could not download content{reason}";
            }
            else if (page.PageLinks.Count == 0)
                linksDisplay = "No links found";
            else
                linksDisplay = $"{string.Join("\n", page.PageLinks)}\n[{page.PageLinks.Count} links]";

            return $"Visited Page: {page.PageUri} ({page.FirstVisitedDepth})\n------------------\n{linksDisplay}\n";

        }
    }
}
