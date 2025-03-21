namespace AI_Proxy_Web.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;

public class HtmlHelper
{
    // 保留的元素标签列表
    private static readonly HashSet<string> ElementsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 结构元素
        "html", "head", "body",

        // 元数据
        "title", "meta",

        // 标题元素
        "h1", "h2", "h3", "h4", "h5", "h6",

        // 内容元素
        "p", "div", "span", "article", "section", "main", "header", "footer", "nav",

        // 列表元素
        "ul", "ol", "li", "dl", "dt", "dd",

        // 表格元素
        "table", "tr", "td", "th", "thead", "tbody", "tfoot",

        // 表单元素
        "form", "input", "select", "option", "button", "textarea", "label",

        // 其他重要元素
        "a", "img", "aside", "figure", "figcaption", "code", "pre"
    };

    // 需要移除的元素
    private static readonly HashSet<string> ElementsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "iframe", "canvas", "svg", "video", "audio",
        "track", "source", "object", "embed", "param", "link"
    };

    // 保留的属性列表
    private static readonly HashSet<string> AttributesToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "id", "class", "name", "type", "value", "href", "src", "alt", "title", "placeholder",
        "aria-label", "role", "data-testid", "content", "charset", "http-equiv"
    };

    // 特定元素需要保留的特定属性
    private static readonly Dictionary<string, HashSet<string>> ElementSpecificAttributes =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "target", "rel" },
            ["img"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "src", "alt", "width", "height" },
            ["input"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "type", "name", "value", "placeholder", "required", "checked", "disabled" },
            ["meta"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "name", "content", "charset", "http-equiv" },
            ["form"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "action", "method" },
            ["select"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "multiple" },
            ["table"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "border" },
            ["td"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan" },
            ["th"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan", "scope" }
        };

    // 保留的meta标签的name属性值
    private static readonly HashSet<string> MetaNamesToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "description", "keywords", "viewport", "author"
    };

    // 保留的data属性前缀
    private static readonly HashSet<string> DataAttributePrefixesToKeep =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "data-id", "data-testid", "data-qa", "data-test", "data-cy"
        };

    // 不应该被删除的重要标签，即使它们是空的
    private static readonly HashSet<string> NotRemovableIfEmpty = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "html", "head", "body", "main", "header", "footer", "meta", "title"
    };

    // 被认为是重要属性的集合，拥有这些属性的标签不应该被删除
    private static readonly HashSet<string> SignificantAttributes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "id", "href", "src", "role", "aria-label", "data-testid"
        };

    /// <summary>
    /// 从URL获取HTML并提取核心DOM
    /// </summary>
    public static async Task<string> ExtractFromUrl(string url, bool indent = true)
    {
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

            string htmlContent = await client.GetStringAsync(url);
            return ExtractCoreDom(htmlContent, indent);
        }
    }

    /// <summary>
    /// 从HTML字符串提取核心DOM
    /// </summary>
    /// <param name="html">原始HTML字符串</param>
    /// <param name="indent">是否使用缩进格式化输出</param>
    public static string ExtractCoreDom(string html, bool indent = true)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(html);
        // 删除注释节点
        RemoveComments(htmlDoc.DocumentNode);

        // 删除不需要的元素
        RemoveUnnecessaryElements(htmlDoc.DocumentNode);

        // 清理属性
        CleanAttributes(htmlDoc.DocumentNode);

        // 删除空白文本节点
        RemoveEmptyTextNodes(htmlDoc.DocumentNode);

        // 递归删除空标签
        RemoveEmptyTags(htmlDoc.DocumentNode);
        // 返回格式化后的HTML
        return FormatHtml(htmlDoc.DocumentNode.OuterHtml, indent);
    }

    private static void RemoveComments(HtmlNode node)
    {
        var comments = node.SelectNodes("//comment()");

        if (comments != null)
        {
            foreach (var comment in comments.ToList())
            {
                comment.Remove();
            }
        }
    }

    private static void RemoveUnnecessaryElements(HtmlNode node)
    {
        var nodesToRemove = new List<HtmlNode>();

        foreach (var childNode in node.Descendants().ToList())
        {
            if (ElementsToRemove.Contains(childNode.Name))
            {
                nodesToRemove.Add(childNode);
            }
            else if (!ElementsToKeep.Contains(childNode.Name) &&
                     childNode.Name != "#text" && childNode.Name != "#document")
            {
                nodesToRemove.Add(childNode);
            }
            else if (childNode.Name == "meta")
            {
                var nameAttr = childNode.Attributes["name"]?.Value;
                var httpEquivAttr = childNode.Attributes["http-equiv"]?.Value;
                var charsetAttr = childNode.Attributes["charset"]?.Value;

                if (nameAttr == null && httpEquivAttr == null && charsetAttr == null)
                {
                    nodesToRemove.Add(childNode);
                }
                else if (nameAttr != null && !MetaNamesToKeep.Contains(nameAttr))
                {
                    nodesToRemove.Add(childNode);
                }
            }
        }

        foreach (var nodeToRemove in nodesToRemove)
        {
            nodeToRemove.Remove();
        }
    }

    private static void CleanAttributes(HtmlNode node)
    {
        foreach (var element in node.Descendants().ToList())
        {
            if (!ElementsToKeep.Contains(element.Name))
                continue;
            var attributesToRemove = new List<HtmlAttribute>();

            foreach (var attribute in element.Attributes.ToList())
            {
                bool keepAttribute = false;

                // 检查全局保留属性
                if (AttributesToKeep.Contains(attribute.Name))
                {
                    keepAttribute = true;
                }

                // 检查元素特定保留属性
                if (ElementSpecificAttributes.TryGetValue(element.Name, out var specificAttributes))
                {
                    if (specificAttributes.Contains(attribute.Name))
                    {
                        keepAttribute = true;
                    }
                }

                // 检查data-*属性
                if (attribute.Name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var prefix in DataAttributePrefixesToKeep)
                    {
                        if (attribute.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            keepAttribute = true;
                            break;
                        }
                    }
                }

                if (!keepAttribute)
                {
                    attributesToRemove.Add(attribute);
                }
            }

            foreach (var attributeToRemove in attributesToRemove)
            {
                element.Attributes.Remove(attributeToRemove);
            }
        }
    }

    private static void RemoveEmptyTextNodes(HtmlNode node)
    {
        var textNodes = node.Descendants("#text").ToList();

        foreach (var textNode in textNodes)
        {
            if (string.IsNullOrWhiteSpace(textNode.InnerText))
            {
                textNode.Remove();
            }
        }
    }

    /// <summary>
    /// 递归删除空标签
    /// </summary>
    private static void RemoveEmptyTags(HtmlNode node)
    {
        // 从最深层级的节点开始处理，确保递归删除嵌套的空节点
        for (int i = node.ChildNodes.Count - 1; i >= 0; i--)
        {
            if (i < node.ChildNodes.Count)
            {
                var child = node.ChildNodes[i];
                if (child.NodeType == HtmlNodeType.Element)
                {
                    RemoveEmptyTags(child);
                }
            }
        }

        // 检查当前节点是否应该被移除
        if (IsEmptyTag(node))
        {
            node.Remove();
        }
    }

    /// <summary>
    /// 判断标签是否为空且可删除
    /// </summary>
    private static bool IsEmptyTag(HtmlNode node)
    {
        // 不处理文本节点、文档节点或必须保留的节点
        if (node.NodeType != HtmlNodeType.Element ||
            NotRemovableIfEmpty.Contains(node.Name))
        {
            return false;
        }

        // 自关闭标签（如img, input, br）不应该被删除
        if (node.Name == "img" ||
            node.Name == "input" ||
            node.Name == "br" ||
            node.Name == "hr" ||
            node.Name == "meta")
        {
            return false;
        }

        // 检查节点是否有文本内容
        string textContent = node.InnerText.Trim();
        if (!string.IsNullOrEmpty(textContent))
        {
            return false;
        }

        // 检查是否有任何重要属性
        foreach (var attr in node.Attributes)
        {
            if (SignificantAttributes.Contains(attr.Name))
            {
                return false;
            }
        }

        // 检查是否有非空子元素
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Element && !IsEmptyTag(child))
            {
                return false;
            }
        }

        // 如果到达这里，该标签是空的且可以删除
        return true;
    }

    private static string FormatHtml(string html, bool indent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        if (indent)
        {
            var sb = new StringBuilder();
            FormatNodeWithIndent(doc.DocumentNode, sb, 0);
            return sb.ToString();
        }
        else
        {
            var sb = new StringBuilder();
            FormatNodeWithoutIndent(doc.DocumentNode, sb);
            return sb.ToString();
        }
    }

    private static void FormatNodeWithIndent(HtmlNode node, StringBuilder sb, int indent)
    {
        string indentString = new string(' ', indent * 2);

        if (node.NodeType == HtmlNodeType.Comment)
        {
            return; // 跳过注释
        }

        if (node.NodeType == HtmlNodeType.Text)
        {
            string text = node.InnerText.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                sb.AppendLine($"{indentString}{text}");
            }

            return;
        }

        if (node.NodeType == HtmlNodeType.Document)
        {
            foreach (var childNode in node.ChildNodes)
            {
                FormatNodeWithIndent(childNode, sb, indent);
            }

            return;
        }

        if (node.NodeType == HtmlNodeType.Element)
        {
            sb.Append($"{indentString}<{node.Name}");

            foreach (var attr in node.Attributes)
            {
                sb.Append($" {attr.Name}=\"{attr.Value}\"");
            }

            // 自闭合标签处理
            if (node.Name == "img" || node.Name == "input" ||
                node.Name == "br" || node.Name == "hr" || node.Name == "meta")
            {
                sb.AppendLine(" />");
            }
            else
            {
                sb.AppendLine(">");

                foreach (var childNode in node.ChildNodes)
                {
                    FormatNodeWithIndent(childNode, sb, indent + 1);
                }

                sb.AppendLine($"{indentString}</{node.Name}>");
            }
        }
    }

    private static void FormatNodeWithoutIndent(HtmlNode node, StringBuilder sb)
    {
        if (node.NodeType == HtmlNodeType.Comment)
        {
            return; // 跳过注释
        }

        if (node.NodeType == HtmlNodeType.Text)
        {
            string text = node.InnerText.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                sb.Append(text);
            }

            return;
        }

        if (node.NodeType == HtmlNodeType.Document)
        {
            foreach (var childNode in node.ChildNodes)
            {
                FormatNodeWithoutIndent(childNode, sb);
            }

            return;
        }

        if (node.NodeType == HtmlNodeType.Element)
        {
            sb.Append($"<{node.Name}");

            foreach (var attr in node.Attributes)
            {
                sb.Append($" {attr.Name}=\"{attr.Value}\"");
            }

            // 自闭合标签处理
            if (node.Name == "img" || node.Name == "input" ||
                node.Name == "br" || node.Name == "hr" || node.Name == "meta")
            {
                sb.Append("/>");
            }
            else
            {
                sb.Append(">");

                foreach (var childNode in node.ChildNodes)
                {
                    FormatNodeWithoutIndent(childNode, sb);
                }

                sb.Append($"</{node.Name}>");
            }
        }
    }
}