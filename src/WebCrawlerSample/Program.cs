using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebCrawler.Core.Models;
using WebCrawler.Core.Services;
using Crawler = WebCrawler.Core.Services.WebCrawler;

namespace WebCrawlerSample
{
    public static class Program
    {
        static async Task Main(string[] args)
        {
            var profilePath = args.Length > 0 ? args[0] : "RunProfile\\RunProfile.json";
            if (!File.Exists(profilePath))
            {
                Console.WriteLine($"Profile file '{profilePath}' not found.");
                return;
            }

            var profile = JsonSerializer.Deserialize<RunProfile>(File.ReadAllText(profilePath));
            if (profile == null || string.IsNullOrWhiteSpace(profile.Website))
            {
                Console.WriteLine("Invalid run profile.");
                return;
            }

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
            crawler.CrawlStarted += (s, uri) => Console.WriteLine($"Crawling {uri} to depth {profile.Depth}\n");
            crawler.PageCrawled += (obj, page) => Console.WriteLine(FormatOutput(page));
            crawler.CrawlCompleted += (s, result) =>
            {
                Console.WriteLine($"Max depth: {result.MaxDepth}");
                Console.WriteLine($"Total links found: {result.Links.Keys.Count}");
                Console.WriteLine("Total crawl execution time: {0:00}:{1:00}.{2:00}",
                    result.RunTime.TotalMinutes, result.RunTime.Seconds, result.RunTime.Milliseconds / 10);
            };

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var startPages = new List<string>();
            if (profile.UseSitemap)
            {
                var urls = await TryGetSitemapUrls(profile.Website, provider.GetRequiredService<IHttpClientFactory>(), cts.Token);
                if (urls != null && urls.Count > 0)
                    startPages.AddRange(urls);
            }
            if (startPages.Count == 0)
                startPages.Add(profile.Website);

            var downloadFolder = profile.Storage?.Path;
            var downloadFiles = profile.Storage != null;
            if (profile.Storage != null && profile.Storage.Type.Equals("blob", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(downloadFolder))
            {
                downloadFolder =  $"{Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())}_{profilePath.ToLowerInvariant().Replace("runprofile", "")}_{DateTime.UtcNow}";
            }

            foreach (var url in startPages)
            {
                await crawler.RunAsync(url, profile.Depth, downloadFiles, downloadFolder, cleanContent: profile.CleanContent, ignoreLinks: profile.IgnoreLinks, cancellationToken: cts.Token);
            }

            if (profile.Storage != null && profile.Storage.Type.Equals("blob", StringComparison.OrdinalIgnoreCase))
            {
                await UploadToBlobStorage(profile.Storage, downloadFolder, cts.Token);
            }
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
                linksDisplay = $"{string.Join("\n", page.PageLinks)}\n[{page.PageLinks.Count}/{page.TotalLinksFound} links]";

            return $"Visited Page: {page.PageUri} ({page.FirstVisitedDepth})\n------------------\n{linksDisplay}\n";

        }

        private static async Task<List<string>> TryGetSitemapUrls(string site, IHttpClientFactory factory, CancellationToken token)
        {
            try
            {
                var client = factory.CreateClient("crawler");
                var sitemapUri = new Uri(new Uri(site), "/sitemap.xml");
                var response = await client.GetAsync(sitemapUri, token);
                if (!response.IsSuccessStatusCode)
                    return null;
                var xml = await response.Content.ReadAsStringAsync(token);
                var doc = System.Xml.Linq.XDocument.Parse(xml);
                var urls = new List<string>();
                foreach (var loc in doc.Descendants("loc"))
                {
                    var val = loc.Value?.Trim();
                    if (!string.IsNullOrEmpty(val))
                        urls.Add(val);
                }
                return urls;
            }
            catch
            {
                return null;
            }
        }

        private static async Task UploadToBlobStorage(StorageOptions options, string folder, CancellationToken token)
        {
            var container = new BlobContainerClient(options.ConnectionString, options.Container);
            await container.CreateIfNotExistsAsync(cancellationToken: token);
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                BlobClient blob = container.GetBlobClient(name);
                await blob.UploadAsync(file, overwrite: true, cancellationToken: token);
            }
        }
    }
}
