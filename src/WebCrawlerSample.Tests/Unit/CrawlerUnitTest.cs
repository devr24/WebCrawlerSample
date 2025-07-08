using Cloud.Core.Testing.Fakes;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using WebCrawlerSample.Services;
using Xunit;

namespace WebCrawlerSample.Tests.Unit
{
    public class CrawlerUnitTest
    {
        // Verify all level of links are crawled and the links found should match expected.
        [Fact]
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
            var crawlResult = await crawler.RunAsync(rootSite, 3, CancellationToken.None);
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
        }
    }
}
