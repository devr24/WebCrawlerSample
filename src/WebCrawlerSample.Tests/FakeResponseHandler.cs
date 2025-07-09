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
        private readonly ConcurrentDictionary<Uri, ConcurrentQueue<HttpResponseMessage>> _responses = new ConcurrentDictionary<Uri, ConcurrentQueue<HttpResponseMessage>>();
        private readonly ConcurrentDictionary<Uri, HttpResponseMessage> _lastResponse = new ConcurrentDictionary<Uri, HttpResponseMessage>();

        public void AddFakeResponse(Uri uri, HttpResponseMessage response)
        {
            var queue = _responses.GetOrAdd(uri, _ => new ConcurrentQueue<HttpResponseMessage>());
            queue.Enqueue(response);
            _lastResponse[uri] = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.TryGetValue(request.RequestUri, out var queue))
            {
                if (queue.TryDequeue(out var response))
                    return Task.FromResult(response);
                if (_lastResponse.TryGetValue(request.RequestUri, out var last))
                    return Task.FromResult(last);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }
}
