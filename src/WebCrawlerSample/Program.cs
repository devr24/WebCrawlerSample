﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using System.Net.Http;
using WebCrawler.Core.Models;
using WebCrawler.Core.Services;
using Crawler = WebCrawler.Core.Services.WebCrawler;

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

            services.AddSingleton<IDownloader, PlaywrightDownloader>();
            services.AddSingleton<IHtmlParser, HtmlParser>();

            var provider = services.BuildServiceProvider();

            var downloader = provider.GetRequiredService<IDownloader>();
            var parser = provider.GetRequiredService<IHtmlParser>();

            // Initialise the crawler and hook into crawler events for logging.
            var crawler = new Crawler(downloader, parser);
            crawler.CrawlStarted += (s, uri) => Console.WriteLine($"Crawling {uri} to depth {maxDepth}\n");
            crawler.PageCrawled += (obj, page) => Console.WriteLine(FormatOutput(page));
            crawler.CrawlCompleted += (s, result) =>
            {
                Console.WriteLine($"Max depth: {result.MaxDepth}");
                Console.WriteLine($"Total links visited: {result.Links.Keys.Count}");
                Console.WriteLine("Total crawl execution time: {0:00}:{1:00}.{2:00}",
                    result.RunTime.TotalMinutes, result.RunTime.Seconds, result.RunTime.Milliseconds / 10);
            };

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            // Run the crawler!
            await crawler.RunAsync(baseUrl, maxDepth, downloadFiles, null, cancellationToken: cts.Token);
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
