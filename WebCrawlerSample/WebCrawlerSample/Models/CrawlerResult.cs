using System;
using System.Collections.Generic;

namespace WebCrawlerSample.Models
{
    public class CrawlResult
    {
        public int MaxDepth { get; }
        public Dictionary<string, CrawledPage> Links { get; }
        public Uri Site { get; }
        public TimeSpan RunTime { get; }

        public CrawlResult(Uri site, int maxDepth, Dictionary<string, CrawledPage> links, TimeSpan runTime)
        {
            MaxDepth = maxDepth;
            Links = links;
            Site = site;
            RunTime = runTime;
        }
    }
}
