using System;
using System.Collections.Generic;

namespace WebCrawler.Core.Models
{
    public class CrawledPage
    {
        public int FirstVisitedDepth { get; }
        public int TotalLinksFound { get; }
        public Uri PageUri { get; }
        public List<string> PageLinks { get; }
        public string Error { get; }

        public CrawledPage(Uri pageUri, int firstVisitDepth, int totalLinksFound, List<string> pageLinks, string error = null)
        {
            PageUri = pageUri;
            FirstVisitedDepth = firstVisitDepth;
            TotalLinksFound = totalLinksFound;
            PageLinks = pageLinks;
            Error = error;
        }
    }
}
