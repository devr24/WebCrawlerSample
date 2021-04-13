using System;
using System.Collections.Generic;

namespace WebCrawlerSample.Models
{
    public class CrawledPage
    {
        public int FirstVisitedDepth { get; }
        public Uri PageUri { get; }
        public List<string> PageLinks { get; }

        public CrawledPage(Uri pageUri, int firstVisitDepth, List<string> pageLinks)
        {
            PageUri = pageUri;
            FirstVisitedDepth = firstVisitDepth;
            PageLinks = pageLinks;
        }
    }
}
