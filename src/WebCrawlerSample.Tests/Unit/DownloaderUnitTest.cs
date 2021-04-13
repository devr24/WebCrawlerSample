using Cloud.Core.Testing.Fakes;
using FluentAssertions;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WebCrawlerSample.Services;
using Xunit;

namespace WebCrawlerSample.Tests.Unit
{
    public class DownloaderUnitTest
    {
        // Verify http requests with string content have their content returned.
        [Theory]
        [InlineData("")]
        [InlineData("no content")]
        [InlineData("<html><body></body></html>")]
        [InlineData("<a></a>")]
        public async Task Test_Downloader_GetContent_Ok(string content)
        {
            // Arrange
            var uri = new Uri("http://contoso.com");
            var fakeHandler = new FakeResponseHandler();
            fakeHandler.AddFakeResponse(uri, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
            var downloader = new Downloader(fakeHandler);

            // Act 
            var result = await downloader.GetContent(uri);

            // Assert
            result.Should().Be(content);
        }

        // Verify http requests with no content return null.
        [Fact]
        public async Task Test_Downloader_GetContent_NotFound()
        {
            // Arrange
            var uri = new Uri("http://contoso.com");
            var fakeHandler = new FakeResponseHandler();
            fakeHandler.AddFakeResponse(uri, new HttpResponseMessage(HttpStatusCode.NotFound));
            var downloader = new Downloader(fakeHandler);

            // Act 
            var result = await downloader.GetContent(uri);

            // Assert
            result.Should().Be(null);
        }
    }
}
