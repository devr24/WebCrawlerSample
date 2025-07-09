using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WebCrawler.Core.Services
{
    public class HtmlParser : IHtmlParser
    {
        private static readonly HashSet<string> _ignoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".css", ".js", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".ico", ".webp",
            ".json", ".xml", ".po", ".mo", ".resx", ".lang"
        };

        public List<string> FindLinks(string htmlContent, Uri pageUri)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            var nodes = doc.DocumentNode.SelectNodes("//a");
            if (nodes == null)
                return new List<string>();

            var links = new List<string>();

            foreach (var a in nodes)
            {
                var href = a.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrWhiteSpace(href))
                    continue;

                var link = href.StartsWith("/") ? $"{pageUri.GetLeftPart(UriPartial.Authority)}{href}" : href;

                if (ShouldIgnore(link))
                    continue;

                links.Add(link);
            }

            return links.Distinct().ToList();
        }

        private static bool ShouldIgnore(string link)
        {
            if (!Uri.TryCreate(link, UriKind.RelativeOrAbsolute, out var uri))
                return false;

            var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.ToString();
            var ext = System.IO.Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return false;

            return _ignoredExtensions.Contains(ext);
        }
    }

    public interface IHtmlParser
    {
        List<string> FindLinks(string htmlContent, Uri pageUri);
    }
}
