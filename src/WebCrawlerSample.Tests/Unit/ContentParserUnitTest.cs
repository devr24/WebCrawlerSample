using FluentAssertions;
using System;
using System.Linq;
using WebCrawlerSample.Services;
using Xunit;

namespace WebCrawlerSample.Tests.Unit
{
    public class ContentParserUnitTest
    {
        // Verify no a href links found in content.
        [Theory]
        [InlineData("")]
        [InlineData("no content")]
        [InlineData("<html><body></body></html>")]
        [InlineData("<a></a>")]
        public void Test_ContentParser_FinkLinks_NoLinksInContent(string content)
        {
            // Arrange
            var parser = new HtmlParser();

            // Act 
            var links = parser.FindLinks(content, new Uri("http://contoso.com"));

            // Assert
            links.Should().BeNullOrEmpty();
        }

        // Verify the links in the content are found.
        [Fact]
        public void Test_ContentParser_FinkLinks_LinksInContent()
        {
            // Arrange
            var parser = new HtmlParser();
            var uri = new Uri("http://contoso.com");

            // Act 
            var links = parser.FindLinks("<a href='/example1'>example1</a><a href='/example2'>example2</a>", uri);

            // Assert
            links.Count.Should().Be(2);
            links.First().Should().Be($"{uri}example1");
            links.Last().Should().Be($"{uri}example2");
        }
    }

    // Could have added a test for Null content = argument exception thrown
}
