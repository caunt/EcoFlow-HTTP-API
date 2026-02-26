using HtmlAgilityPack;

namespace EcoFlow.Exporter.Models;

public static class Extensions
{
    public static IEnumerable<HtmlNode> SelectNodesOrEmpty(this HtmlNode rootNode, string xpath)
    {
        return rootNode.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>();
    }
}
