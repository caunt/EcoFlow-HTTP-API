using HtmlAgilityPack;

namespace EcoFlow.Exporter.Models;

internal static class HtmlNodeExtensions
{
    public static IEnumerable<HtmlNode> SelectNodesOrEmpty(this HtmlNode rootNode, string xpath)
    {
        return rootNode.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>();
    }
}
