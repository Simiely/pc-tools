// Services/RichText.cs - 富文本复制（对齐 app.js buildWordDoc / copyRich）
// 纯逻辑移植：buildWordDoc 把片段包成带 Word 命名空间(xmlns:o/w/m)的完整文档；
// Windows 剪贴板 HTML 走 CF_HTML 规范（Version/StartHTML/EndHTML/StartFragment/EndFragment + 字节偏移），
// Word/飞书靠 xmlns + StartFragment/EndFragment 识别"来自 Word"保留格式。
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace ClipboardExe.Services;

/// <summary>富文本复制（对齐 Web app.js：存入时 normalize / 复制时 buildWordDoc + 双格式写入）。</summary>
public static class RichText
{
    private static readonly Regex BodyFrag = new(@"^<body([^>]*)>([\s\S]*)</body>$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>复制包装：片段 → 带 Word 命名空间的完整文档（对齐 app.js buildWordDoc：已是 Word/完整文档则原样返回）。</summary>
    public static string BuildWordDoc(string? html)
    {
        var s = (html ?? "").Trim();
        if (s.Length == 0) return "";
        // 已是完整文档（含 Word 命名空间或 &lt;html 根）——直接返回，避免二次包装
        if (s.Contains("xmlns:w=") ||
            s.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("<html ", StringComparison.OrdinalIgnoreCase))
            return s;
        var m = BodyFrag.Match(s);
        if (m.Success)
        {
            // body 片段(normalizeRichHtml 保留的文档级属性 tab-interval 等)→ 属性并入外层 body，避免嵌套
            return DocHead + "<body" + m.Groups[1].Value + "><!--StartFragment -->" + m.Groups[2].Value + "<!--EndFragment --></body></html>";
        }
        return DocHead + "<body><!--StartFragment -->" + s + "<!--EndFragment --></body></html>";
    }

    private const string DocHead =
        "<html xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w=\"urn:schemas-microsoft-com:office:word\" xmlns:m=\"http://schemas.microsoft.com/office/2004/12/omml\"><head><meta charset=\"utf-8\"></head>";

    /// <summary>CF_HTML 编码（Windows 剪贴板 HTML 规范：字节偏移指向 StartFragment/EndFragment 之间的 Fragment）。</summary>
    public static string EncodeCfHtml(string htmlDoc)
    {
        const string header = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        var enc = Encoding.UTF8;
        var placeholder = string.Format(header, 0, 0, 0, 0);
        int startHtml = enc.GetByteCount(placeholder);
        int endHtml = startHtml + enc.GetByteCount(htmlDoc);
        const string sf = "<!--StartFragment -->";
        const string ef = "<!--EndFragment -->";
        int idxSf = htmlDoc.IndexOf(sf, StringComparison.Ordinal);
        int idxEf = htmlDoc.IndexOf(ef, StringComparison.Ordinal);
        int startFrag, endFrag;
        if (idxSf >= 0 && idxEf >= 0)
        {
            startFrag = startHtml + enc.GetByteCount(htmlDoc[..(idxSf + sf.Length)]);
            endFrag = startHtml + enc.GetByteCount(htmlDoc[..idxEf]);
        }
        else
        {
            startFrag = startHtml;
            endFrag = endHtml;
        }
        return string.Format(header, startHtml, endHtml, startFrag, endFrag) + htmlDoc;
    }

    /// <summary>双格式复制（对齐 app.js copyRich）：剪贴板同时写 text/html(CF_HTML) + text/plain。</summary>
    public static bool CopyRich(string? html, string? plain)
    {
        var rich = BuildWordDoc(html);
        if (string.IsNullOrEmpty(rich)) return false;
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.Text, plain ?? "");
            data.SetData(DataFormats.Html, EncodeCfHtml(rich));
            ClipboardHelper.SetDataObject(data);
            return true;
        }
        catch { return false; }
    }

    private static readonly Regex UrlRe = new(@"(https?:\/\/\S+)", RegexOptions.Compiled);
    private static readonly Regex ParaSplitRe = new(@"\n{2,}", RegexOptions.Compiled);

    /// <summary>纯文本 → 简单富文本 HTML（换行转段落，URL 转可点链接；对齐 app.js textToHtml）。</summary>
    public static string TextToHtml(string? text)
    {
        var esc = (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        var paras = ParaSplitRe.Split(esc);
        var sb = new StringBuilder("<div>");
        foreach (var p in paras)
        {
            var withBr = p.Replace("\n", "<br>");
            var withLinks = UrlRe.Replace(withBr, "<a href=\"$1\">$1</a>");
            sb.Append("<p>").Append(withLinks).Append("</p>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }
}
