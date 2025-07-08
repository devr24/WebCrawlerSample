using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WebCrawlerSample.Tests
{
    internal class FakeResponseHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<Uri, HttpResponseMessage> _responses = new ConcurrentDictionary<Uri, HttpResponseMessage>();

        public void AddFakeResponse(Uri uri, HttpResponseMessage response)
        {
            _responses[uri] = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.TryGetValue(request.RequestUri, out var response))
                return Task.FromResult(response);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }
}
