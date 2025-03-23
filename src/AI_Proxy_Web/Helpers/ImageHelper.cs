using Newtonsoft.Json.Linq;
using SkiaSharp;
using Svg.Skia;

namespace AI_Proxy_Web.Helpers;

public class ImageHelper
{
    /// <summary>
    /// 压缩图片
    /// </summary>
    public static byte[] Compress(byte[] file, SKSize? size = null, SKEncodedImageFormat  format = SKEncodedImageFormat.Jpeg)
    {
        using (var bitmap = SKBitmap.Decode(file))
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var toWidth = bitmap.Width;
            var toHeight = bitmap.Height;
            if (size.HasValue)
            {
                toWidth = (int)size.Value.Width;
                toHeight = (int)size.Value.Height;
            }
            else
            {
                if (width > 2048 || height > 2048)
                {
                    if (width > height)
                    {
                        toHeight = height * 2048 / width;
                        toWidth = 2048;
                    }
                    else
                    {
                        toWidth = width * 2048 / height;
                        toHeight = 2048;
                    }
                }
            }

            if (width > toWidth || height > toHeight)
            {
                var newBitmap = bitmap.Resize(new SKSizeI(toWidth, toHeight), SKFilterQuality.High);
                return newBitmap.Encode(format, 100).ToArray();
            }
            return bitmap.Encode(format, 100).ToArray();
        }
    }
    
    public static byte[]? ConvertSvgToPng(string svgContent)
    {
        try
        {
            var svg = new SKSvg();
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent)))
            {
                if (svg.Load(stream) is { })
                {
                    using var ms = new MemoryStream();
                    svg.Save(ms, SKColor.Empty, SKEncodedImageFormat.Png, 100, 2f, 2f);
                    return ms.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        return null;
    }
    
    /// <summary>
    /// 图片文件裁掉四周的白边
    /// </summary>
    /// <param name="bytes">图片文件的二进制</param>
    /// <param name="edge">四边留5像素空白</param>
    /// <returns></returns>
    public static byte[]? CropWhiteBorder(byte[] bytes, int edge = 5)
    {
        using var bmp = SKBitmap.Decode(bytes);
        int w = bmp.Width;
        int h = bmp.Height;

        bool AllWhiteRow(int row)
        {
            for (int i = 0; i < w; ++i)
                if (bmp.GetPixel(i, row).Red != 255)
                    return false;
            return true;
        }

        bool AllWhiteColumn(int col)
        {
            for (int i = 0; i < h; ++i)
                if (bmp.GetPixel(col, i).Red != 255)
                    return false;
            return true;
        }

        int topmost = 0;
        for (int row = 0; row < h; ++row)
        {
            if (AllWhiteRow(row))
                topmost = row;
            else break;
        }

        if (topmost > edge) topmost -= edge;

        int bottommost = 0;
        for (int row = h - 1; row >= 0; --row)
        {
            if (AllWhiteRow(row))
                bottommost = row;
            else break;
        }

        if (bottommost < h - edge) bottommost += edge;

        int leftmost = 0, rightmost = 0;
        for (int col = 0; col < w; ++col)
        {
            if (AllWhiteColumn(col))
                leftmost = col;
            else
                break;
        }

        if (leftmost > edge) leftmost -= edge;

        for (int col = w - 1; col >= 0; --col)
        {
            if (AllWhiteColumn(col))
                rightmost = col;
            else
                break;
        }

        if (rightmost < w - edge) rightmost += edge;

        if (rightmost == 0) rightmost = w; // As reached left
        if (bottommost == 0) bottommost = h; // As reached top.

        int croppedWidth = rightmost - leftmost;
        int croppedHeight = bottommost - topmost;

        if (croppedWidth == 0) // No border on left or right
        {
            leftmost = 0;
            croppedWidth = w;
        }

        if (croppedHeight == 0) // No border on top or bottom
        {
            topmost = 0;
            croppedHeight = h;
        }

        try
        {
            using var pixmap =  new SKPixmap(bmp.Info, bmp.GetPixels());
            SkiaSharp.SKRectI rectI = new SkiaSharp.SKRectI(leftmost, topmost, croppedWidth+leftmost, croppedHeight+topmost);
            var subset = pixmap.ExtractSubset(rectI);
            using var data = subset.Encode(SkiaSharp.SKPngEncoderOptions.Default);
            return data.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Values are topmost={topmost} btm={bottommost} left={leftmost} right={rightmost} croppedWidth={croppedWidth} croppedHeight={croppedHeight}",
              ex);
        }

        return null;
    }
}