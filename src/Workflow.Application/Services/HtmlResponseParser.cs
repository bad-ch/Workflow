using HtmlAgilityPack;
using Newtonsoft.Json.Linq;

namespace Workflow.Application.Services;

/// <summary>
/// Converts an HTML document into a structured <see cref="JObject"/> so that
/// workflow steps can reference page content with the standard {{...}} syntax.
/// </summary>
internal static class HtmlResponseParser
{
    /// <summary>
    /// Parses <paramref name="html"/> and returns a JSON object with the shape:
    /// <code>
    /// {
    ///   "title":    "Page title",
    ///   "meta":     { "description": "...", "keywords": "..." },
    ///   "headings": { "h1": ["..."], "h2": ["..."], ... },
    ///   "links":    [ { "text": "...", "href": "..." } ],
    ///   "tables":   [ [ ["cell","cell"], ["cell","cell"] ] ],
    ///   "text":     "full visible inner-text"
    /// }
    /// </code>
    /// </summary>
    internal static JObject Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        return new JObject
        {
            ["title"]    = ExtractTitle(doc),
            ["meta"]     = ExtractMeta(doc),
            ["headings"] = ExtractHeadings(doc),
            ["links"]    = ExtractLinks(doc),
            ["tables"]   = ExtractTables(doc),
            ["text"]     = ExtractText(doc)
        };
    }

    private static string ExtractTitle(HtmlDocument doc)
        => HtmlEntity.DeEntitize(
               doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim() ?? string.Empty);

    private static JObject ExtractMeta(HtmlDocument doc)
    {
        var meta = new JObject();
        foreach (var node in doc.DocumentNode.SelectNodes("//meta") ?? Enumerable.Empty<HtmlNode>())
        {
            var name    = node.GetAttributeValue("name",       null)
                       ?? node.GetAttributeValue("property",   null);
            var content = node.GetAttributeValue("content",    null);
            if (name is not null && content is not null)
                meta[name] = content;
        }
        return meta;
    }

    private static JObject ExtractHeadings(HtmlDocument doc)
    {
        var headings = new JObject();
        foreach (var tag in new[] { "h1", "h2", "h3", "h4", "h5", "h6" })
        {
            var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
            if (nodes is null) continue;
            headings[tag] = new JArray(nodes.Select(n =>
                HtmlEntity.DeEntitize(n.InnerText.Trim())));
        }
        return headings;
    }

    private static JArray ExtractLinks(HtmlDocument doc)
    {
        var links = new JArray();
        foreach (var node in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            links.Add(new JObject
            {
                ["text"] = HtmlEntity.DeEntitize(node.InnerText.Trim()),
                ["href"] = node.GetAttributeValue("href", string.Empty)
            });
        }
        return links;
    }

    private static JArray ExtractTables(HtmlDocument doc)
    {
        var tables = new JArray();
        foreach (var table in doc.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
        {
            var rows = new JArray();
            foreach (var row in table.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var cells = new JArray();
                foreach (var cell in row.SelectNodes(".//th|.//td") ?? Enumerable.Empty<HtmlNode>())
                    cells.Add(HtmlEntity.DeEntitize(cell.InnerText.Trim()));
                rows.Add(cells);
            }
            tables.Add(rows);
        }
        return tables;
    }

    private static string ExtractText(HtmlDocument doc)
    {
        // Remove script and style nodes before extracting text
        foreach (var node in doc.DocumentNode
            .SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
            node.Remove();

        return HtmlEntity.DeEntitize(
            doc.DocumentNode.SelectSingleNode("//body")?.InnerText.Trim()
            ?? doc.DocumentNode.InnerText.Trim());
    }
}
