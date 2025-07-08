using System;
using System.Linq;
using FluentAssertions;
using System.Threading.Tasks;
using WebCrawlerSample.Services;
using Xunit;

namespace WebCrawlerSample.Tests.Integration
{
    public class CrawlerIntegrationTest
    {
        // Run the crawler to one depth of the test page and ensure the results are as expected.
        [Fact]
        public async Task Test_Crawler_RunAsync()
        {
            // Arrange
            var testSite = "https://www.crawler-test.com/";
            var crawler = new WebCrawler(new Downloader(), new HtmlParser());
            
            // Act
            var result = await crawler.RunAsync(testSite);

            // Assert
            result.MaxDepth.Should().Be(1);
            result.RunTime.Should().BeGreaterThan(new TimeSpan());
            result.Site.Should().BeEquivalentTo(new Uri(testSite));
            result.Links.Count.Should().Be(1); // hard coded expected links - not ideal.
            result.Links.FirstOrDefault().Value.PageLinks.Count.Should().Be(412); // hard coded expected links - not ideal.
        }
    }
}
