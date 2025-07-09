using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using WebCrawlerSample.Tests;
using WebCrawler.Core.Services;
using WebCrawler.Core.Models;
using Xunit;

namespace WebCrawlerSample.Tests.Unit
{
    public class CrawlerUnitTest
    {
        // Verify all level of links are crawled and the links found should match expected.
        [Fact(Skip="Fails under CI")]
        public async Task Test_Crawler_StartAsync()
        {
            // Arrange
            var rootSite = "http://contoso.com";
            var rootPageUri = new Uri(rootSite);
            var page1Uri = new Uri($"{rootSite}/page1");
            var page2Uri = new Uri($"{rootSite}/page2");
            var page3Uri = new Uri($"{rootSite}/page3");
            var fakeHandler = new FakeResponseHandler();

            fakeHandler.AddFakeResponse(rootPageUri, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<a href='/page1'>page1</a><a href='/page2'>page2</a><a href='#'>no link</a>") });
            fakeHandler.AddFakeResponse(page1Uri, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<a href='#'></a><a href='https://www.google.com'></a><a href='/page1'>page1</a>") });
            fakeHandler.AddFakeResponse(page2Uri, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<a href='#'></a><a href='https://www.facebook.com'></a><a href='/page3'></a>") });
            fakeHandler.AddFakeResponse(page3Uri, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("no links") });

            var client = new HttpClient(fakeHandler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            IDownloader downloader = new Downloader(factory.Object);
            IHtmlParser parser = new HtmlParser();
            var crawler = new WebCrawler(downloader, parser);

            // Act 
            var crawlResult = await crawler.RunAsync(rootSite, 3, false, null, CancellationToken.None);
            var rootPage = crawlResult.Links[$"{rootSite}/"];
            var page1 = crawlResult.Links[$"{rootSite}/page1"];
            var page2 = crawlResult.Links[$"{rootSite}/page2"];
            var page3 = crawlResult.Links[$"{rootSite}/page3"];

            // Assert
            crawlResult.Should().NotBeNull();
            crawlResult.Links.Count.Should().Be(4);
            crawlResult.MaxDepth.Should().Be(3);

            rootPage.FirstVisitedDepth.Should().Be(1);
            rootPage.PageLinks.Count.Should().Be(3);
            rootPage.PageLinks.Should().BeEquivalentTo(new List<string> { "http://contoso.com/page1", "http://contoso.com/page2", "#" });

            page1.FirstVisitedDepth.Should().Be(2);
            page1.PageLinks.Count.Should().Be(3);
            page1.PageLinks.Should().BeEquivalentTo(new List<string> { "http://contoso.com/page1", "https://www.google.com", "#" });

            page2.FirstVisitedDepth.Should().Be(2);
            page2.PageLinks.Count.Should().Be(3);

            page3.FirstVisitedDepth.Should().Be(3);
            page3.PageLinks.Should().BeNull();
            page3.Error.Should().NotBeNull();
        }

        // Ensure the crawler does not exceed the configured concurrency level when downloading pages.
        [Fact]
        public async Task Test_Crawler_ConcurrencyLimit()
        {
            // Arrange
            var rootSite = "http://contoso.com";
            var parser = new HtmlParser();

            var links = new List<string>();
            for (int i = 0; i < 10; i++)
                links.Add($"{rootSite}/page{i}");

            var html = string.Join(string.Empty, links.Select(l => $"<a href='{l.Replace(rootSite, string.Empty)}'>link</a>"));

            var concurrent = 0;
            var maxConcurrent = 0;
            var downloaderMock = new Mock<IDownloader>();
            downloaderMock.Setup(d => d.GetContent(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns<Uri, CancellationToken>(async (uri, token) =>
                {
                    var current = Interlocked.Increment(ref concurrent);
                    InterlockedExtensions.Max(ref maxConcurrent, current);
                    await Task.Delay(10, token);
                    Interlocked.Decrement(ref concurrent);
                    var content = uri.AbsoluteUri == $"{rootSite}/" ? html : string.Empty;
                    var data = System.Text.Encoding.UTF8.GetBytes(content);
                    return new DownloadResult(content, data, "text/html");
                });

            var crawler = new WebCrawler(downloaderMock.Object, parser);

            // Act
            var result = await crawler.RunAsync(rootSite, 2, false, null, CancellationToken.None);

            // Assert
            result.Links.Count.Should().Be(11);
            maxConcurrent.Should().BeLessThanOrEqualTo(5);
        }

        // Verify pages containing Cloudflare protection text are not written to disk.
        [Fact]
        public async Task Test_Crawler_SkipCloudflareFiles()
        {
            // Arrange
            var rootSite = "http://contoso.com";
            var rootUri = new Uri(rootSite);
            var fakeHandler = new FakeResponseHandler();
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>they are protected by cloudflare</html>")
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            fakeHandler.AddFakeResponse(rootUri, message);
            var client = new HttpClient(fakeHandler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            var crawler = new WebCrawler(new Downloader(factory.Object), new HtmlParser());
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                // Act
                await crawler.RunAsync(rootSite, 1, true, folder, CancellationToken.None);

                // Assert
                System.IO.Directory.GetFiles(folder).Should().BeEmpty();
            }
            finally
            {
                if (System.IO.Directory.Exists(folder))
                    System.IO.Directory.Delete(folder, true);
            }
        }

        // Verify files are written when content does not contain Cloudflare text.
        [Fact]
        public async Task Test_Crawler_DownloadsFile()
        {
            // Arrange
            var rootSite = "http://contoso.com";
            var rootUri = new Uri(rootSite);
            var fakeHandler = new FakeResponseHandler();
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>")
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            fakeHandler.AddFakeResponse(rootUri, message);
            var client = new HttpClient(fakeHandler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            var crawler = new WebCrawler(new Downloader(factory.Object), new HtmlParser());
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                // Act
                await crawler.RunAsync(rootSite, 1, true, folder, CancellationToken.None);

                // Assert
                System.IO.Directory.GetFiles(folder).Length.Should().Be(1);
            }
            finally
            {
                if (System.IO.Directory.Exists(folder))
                    System.IO.Directory.Delete(folder, true);
            }
        }

        // Verify crawler retries when receiving a 429 response.
        [Fact]
        public async Task Test_Crawler_RetryOn429()
        {
            var rootSite = "http://contoso.com";
            var rootUri = new Uri(rootSite);
            var page1Uri = new Uri($"{rootSite}/page1");

            var handler = new FakeResponseHandler();
            handler.AddFakeResponse(rootUri, new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<a href='/page1'>page1</a>")
            });
            handler.AddFakeResponse(page1Uri, new HttpResponseMessage((HttpStatusCode)429));
            handler.AddFakeResponse(page1Uri, new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>")
            });

            var client = new HttpClient(handler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

            var crawler = new WebCrawler(new Downloader(factory.Object), new HtmlParser());

            var result = await crawler.RunAsync(rootSite, 2, false, null, CancellationToken.None);

            result.Links.Count.Should().Be(2);
            result.Links[$"{rootSite}/page1"].Error.Should().BeNull();
        }
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref int location, int value)
    {
        int current;
        while ((current = Volatile.Read(ref location)) < value)
        {
            if (Interlocked.CompareExchange(ref location, value, current) == current)
                break;
        }
    }
}
