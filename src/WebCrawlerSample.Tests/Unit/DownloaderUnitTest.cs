using FluentAssertions;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using WebCrawlerSample.Tests;
using WebCrawlerSample.Services;
using WebCrawlerSample.Models;
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
            var message = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            fakeHandler.AddFakeResponse(uri, message);
            var client = new HttpClient(fakeHandler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            var downloader = new Downloader(factory.Object);

            // Act 
            var result = await downloader.GetContent(uri, CancellationToken.None);

            // Assert
            result.Content.Should().Be(content);
            result.Data.Should().NotBeNull();
        }

        // Verify http requests with no content return error.
        [Fact]
        public async Task Test_Downloader_GetContent_NotFound()
        {
            // Arrange
            var uri = new Uri("http://contoso.com");
            var fakeHandler = new FakeResponseHandler();
            fakeHandler.AddFakeResponse(uri, new HttpResponseMessage(HttpStatusCode.NotFound));
            var client = new HttpClient(fakeHandler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            var downloader = new Downloader(factory.Object);

            // Act
            var result = await downloader.GetContent(uri, CancellationToken.None);

            // Assert
            result.Error.Should().NotBeNull();
        }

        // Verify content returned when type is not text/html.
        [Fact]
        public async Task Test_Downloader_GetContent_NotHtml()
        {
            // Arrange
            var uri = new Uri("http://contoso.com/file.pdf");
            var bytes = new byte[] {1,2,3};
            var fakeHandler = new FakeResponseHandler();
            var message = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            fakeHandler.AddFakeResponse(uri, message);
            var client = new HttpClient(fakeHandler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            var downloader = new Downloader(factory.Object);

            // Act
            var result = await downloader.GetContent(uri, CancellationToken.None);

            // Assert
            result.Content.Should().BeNull();
            result.Data.Should().BeEquivalentTo(bytes);
            result.MediaType.Should().Be("application/pdf");
            result.Error.Should().Be("Content not HTML");
        }

        // Verify content larger than 300KB results in error.
        [Fact]
        public async Task Test_Downloader_GetContent_ContentTooLarge()
        {
            // Arrange
            var uri = new Uri("http://contoso.com");
            var content = new string('a', 307_201);
            var fakeHandler = new FakeResponseHandler();
            var message = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            // Set explicit content length header
            message.Content.Headers.ContentLength = content.Length;
            fakeHandler.AddFakeResponse(uri, message);
            var client = new HttpClient(fakeHandler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            var downloader = new Downloader(factory.Object);

            // Act
            var result = await downloader.GetContent(uri, CancellationToken.None);

            // Assert
            result.Error.Should().Be("Content too large");
        }
    }
}
