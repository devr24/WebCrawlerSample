using System;
using System.Collections.Generic;
using System.Linq;
using HtmlAgilityPack;

namespace WebCrawlerSample.Services
{
    public class HtmlParser
    {
        public List<string> FindLinks(string htmlContent, Uri pageUri)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            // using '//' will define a path to node "a" anywhere within the document (any depth within the tree).
            return doc.DocumentNode
                .SelectNodes("//a")?.Select(a => {
                    var href = a.GetAttributeValue("href", string.Empty);

                    // If its an absolute link, make sure to set the full path.
                    return href.StartsWith("/") ? $"{pageUri.GetLeftPart(UriPartial.Authority)}{href}" : href;
                })
                .Distinct()
                .Where(u => !string.IsNullOrEmpty(u))
                .ToList();
        }
    }
}
