using AI_Proxy_Web.Models;
using ChromiumHtmlToPdfLib;
using ChromiumHtmlToPdfLib.Settings;
using Newtonsoft.Json.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Markdown;

namespace AI_Proxy_Web.Helpers;

public class PdfHelper
{
    static PdfHelper()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
    }
    
    public static byte[] GeneratePdf(ChatContexts contexts)
    {
        var options = new MarkdownRendererOptions
        {
            UnicodeGlyphFont = "WenQuanYi Micro Hei"
        };
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(new TextStyle().FontSize(12).LineHeight(1.5f).FontFamily("WenQuanYi Micro Hei", "SimHei", "黑体", "宋体", "楷体"));
                page.Content().Column(x =>
                {
                    x.Spacing(10);
                    contexts.Contexts.ForEach(ctx =>
                    {
                        x.Item().AlignRight().Markdown($"**我：**", options);
                        ctx.QC.ForEach(q =>
                        {
                            if(q.Type== ChatType.文本)
                                x.Item().PaddingLeft(200).AlignRight().Background(Colors.Grey.Lighten5).Padding(5).Markdown(q.Content, options);
                        });
                        x.Spacing(20);
                        x.Item().Markdown($"**易小智：**", options);
                        ctx.AC.ForEach(q =>
                        {
                            if(q.Type== ChatType.文本)
                                x.Item().PaddingRight(100).Markdown(q.Content, options);
                        });
                    });
                });
            });
        });
        using var ms = new MemoryStream();
        document.GeneratePdf(ms);
        return ms.ToArray();
    }
    
    public static byte[]? GeneratePdfByUrl(string url, bool toImage = false)
    {
        var pageSettings = new PageSettings(){};
        int pageWidth = url.Contains("/api/ai/") ? 740 : 1366;
        using (var converter = new Converter())
        {
            converter.AddChromiumArgument("--no-sandbox");
            converter.AddChromiumArgument("--disable-dev-shm-usage");
            converter.WaitForNetworkIdle = true;
            using var stream = new MemoryStream();
            if (toImage)
            {
                converter.SetWindowSize(pageWidth, 3000);
                converter.ConvertToImage(new ConvertUri(url), stream, pageSettings);
                var bytes = stream.ToArray();
                return ImageHelper.CropWhiteBorder(bytes);
            }
            else
            {
                converter.SetWindowSize(pageWidth, 1280);
                converter.RunJavascript =
                    "window.scrollTo(0, document.body.scrollHeight);";
                converter.ConvertToPdf(new ConvertUri(url), stream, pageSettings);
                return stream.ToArray();
            }
        }
    }

    /// <summary>
    /// PDF转图片
    /// </summary>
    /// <param name="pdfFile">输入的PDF文件的二进制</param>
    /// <param name="page">转第几页，从1开始</param>
    /// <param name="cropWhiteBorder">是否自动裁白边</param>
    /// <param name="edge">保留的白边宽度</param>
    /// <returns></returns>
    public static byte[]? PdfToImage(byte[] pdfFile, int page = 1, bool cropWhiteBorder = true, int edge = 10)
    {
        using var ms = new MemoryStream();
        PDFtoImage.Conversion.SavePng(ms, pdfFile, page - 1, options: new(Dpi: 300));
        var bytes = ms.ToArray();
        if (cropWhiteBorder)
            return ImageHelper.CropWhiteBorder(bytes, edge: edge);
        else
            return bytes;
    }
}