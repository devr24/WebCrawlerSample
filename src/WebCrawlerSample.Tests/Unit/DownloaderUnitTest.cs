using Cloud.Core.Testing.Fakes;
using FluentAssertions;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Moq;
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
            var client = new HttpClient(fakeHandler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            var downloader = new Downloader(factory.Object);

            // Act 
            var result = await downloader.GetContent(uri, CancellationToken.None);

            // Assert
            result.Should().BeNull();
        }

        // Verify null returned when content type is not text/html.
        [Fact]
        public async Task Test_Downloader_GetContent_NotHtml()
        {
            // Arrange
            var uri = new Uri("http://contoso.com");
            var fakeHandler = new FakeResponseHandler();
            var message = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ignored") };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            fakeHandler.AddFakeResponse(uri, message);
            var client = new HttpClient(fakeHandler, disposeHandler: false);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
            var downloader = new Downloader(factory.Object);

            // Act
            var result = await downloader.GetContent(uri, CancellationToken.None);

            // Assert
            result.Should().BeNull();
        }

        // Verify content larger than 100KB results in null.
        [Fact]
        public async Task Test_Downloader_GetContent_ContentTooLarge()
        {
            // Arrange
            var uri = new Uri("http://contoso.com");
            var content = new string('a', 102_401);
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
            result.Should().BeNull();
        }
    }
}
