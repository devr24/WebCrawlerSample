﻿using System;
using System.Threading.Tasks;
using WebCrawlerSample.Models;
using WebCrawlerSample.Services;

namespace WebCrawlerSample
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            // default site and depth before grabbing from args.
            var startingUrl = "https://www.crawler-test.com/";
            var maxDepth = 3;

            if (args.Length > 0) startingUrl = args[0];
            if (args.Length > 1) maxDepth = Convert.ToInt32(args[1]);

            // Setup dependencies for the crawler.
            IDownloader downloader = new Downloader();
            IHtmlParser parser = new HtmlParser();

            // Initialise the crawler and hook into the crawled event.
            var crawler = new WebCrawer(downloader, parser);
            crawler.PageCrawled += (obj, page) => Console.WriteLine(FormatOutput(page));

            Console.WriteLine($"Crawling {startingUrl} to depth {maxDepth}\n");

            // Run the crawler!
            var result = await crawler.RunAsync(startingUrl, maxDepth);

            Console.WriteLine($"Max depth: {result.MaxDepth}");
            Console.WriteLine($"Total links visited: {result.Links.Keys.Count}");
            Console.WriteLine("Total crawl execution time: {0:00}:{1:00}.{2:00}", result.RunTime.TotalMinutes, result.RunTime.Seconds, result.RunTime.Milliseconds / 10);
        }

        public static string FormatOutput(CrawledPage page)
        {
            string linksDisplay;

            if (page.PageLinks == null)
                linksDisplay = "Could not download content";
            else if (page.PageLinks.Count == 0)
                linksDisplay = "No links found";
            else
                linksDisplay = $"{string.Join("\n", page.PageLinks)}\n[{page.PageLinks.Count} links]";

            return $"Visited Page: {page.PageUri} ({page.FirstVisitedDepth})\n------------------\n{linksDisplay}\n";

        }
    }
}
