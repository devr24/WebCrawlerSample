using System;
using System.Collections.Generic;

namespace WebCrawler.Core.Models
{
    public class CrawledPage
    {
        public int FirstVisitedDepth { get; }
        public Uri PageUri { get; }
        public List<string> PageLinks { get; }
        public string Error { get; }

        public CrawledPage(Uri pageUri, int firstVisitDepth, List<string> pageLinks, string error = null)
        {
            PageUri = pageUri;
            FirstVisitedDepth = firstVisitDepth;
            PageLinks = pageLinks;
            Error = error;
        }
    }
}
