using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace AI_Proxy_Web.Apis.V2.Extra;

[ApiProvider("DeepDataSpace")]
public class ApiDeepDataSpaceProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiDeepDataSpaceProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
    }

    private String createTaskUrl;
    private String checkTaskUrl;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        createTaskUrl = _host + "v2/task/trex/detection";
        checkTaskUrl = _host + "v2/task_status/";
    }
    
    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
       var ctx = input.ChatContexts.Contexts.Last();
        var url = createTaskUrl;
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Token", _key);
        var image1 = ctx.QC.FirstOrDefault(t => t.Type == ChatType.图片Base64)?.Content;
        var text = ctx.QC.FirstOrDefault(t => t.Type == ChatType.坐标).Content;
        var arr = JArray.Parse(text);
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msg = JsonConvert.SerializeObject(new
        {
            model="T-Rex-2.0",
            image= "data:image/jpeg;base64," + image1,
            targets = new[]{"bbox"},
            prompt = new
            {
                type="visual_images",
                visual_images = new[]
                {
                    new
                    {
                        image = "data:image/jpeg;base64," + image1,
                        interactions = new[]
                        {
                            new{type="rect", rect = arr}
                        }
                    }
                }
            }
        }, jSetting);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync(); // {'code': 0, 'data': {'task_uuid': '092ccde4-a51a-489b-b384-9c4ba8af7375'}, 'msg': 'ok'}
        var json = JObject.Parse(content);
        if (json["code"].Value<int>()==0)
        {
            var id = json["data"]["task_uuid"].Value<string>();
            int times = 0;
            while (times<60)
            {
                url = checkTaskUrl + id;
                resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
                content = await resp.Content.ReadAsStringAsync();
                json = JObject.Parse(content);
                var state = json["data"]["status"].Value<string>();
                if (state == "success")
                {
                    var results = json["data"]["result"]["objects"] as JArray;
                    var answer = $"共检测到 {results.Count} 个目标";
                    var bboxes = new List<SKRect>();
                    var sboxes = new List<SKRect>();
                    foreach (var result in results)
                    {
                        var box = (result["bbox"] as JArray).ToObject<float[]>();
                        var score = result["score"].Value<double>();
                        if (score <= 0.35)
                            sboxes.Add(new SKRect(box[0], box[1], box[2], box[3]));
                        else
                            bboxes.Add(new SKRect(box[0], box[1], box[2], box[3]));
                    }

                    if (sboxes.Count > 0)
                    {
                        answer += $"，其中 {sboxes.Count} 个比较可疑";
                    }
                    yield return Result.Answer(answer + "。");
                    var bytes = DrawBoundingBox(Convert.FromBase64String(image1), bboxes.ToArray(), SKColors.Green, sboxes.ToArray(), SKColors.Red, 1);
                    yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
                    break;
                }
                else if (state == "waiting" || state == "running")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(500);
                }
                else
                {
                    yield return Result.Error(content);
                    break;
                }
            }
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
    public byte[] DrawBoundingBox(byte[] imageData, SKRect[] bboxes, SKColor color, SKRect[] sboxes, SKColor scolor, float strokeWidth)
    {
        // Step 1: Load the image from the binary data
        using var inputStream = new MemoryStream(imageData);
        using var skBitmap = SKBitmap.Decode(inputStream);
        using var skImage = SKImage.FromBitmap(skBitmap);
        using var skCanvas = new SKCanvas(skBitmap);

        // Step 2: Configure the paint for the bounding box
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke, // Only draw the border
            Color = color,              // Set the color of the rectangle
            StrokeWidth = strokeWidth   // Set the border thickness
        };

        // Step 3: Draw the bounding box onto the canvas
        foreach (var bbox in bboxes)
            skCanvas.DrawRect(bbox, paint);
        
        if (sboxes.Length > 0)
        {
            using var paint2 = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = scolor,
                StrokeWidth = strokeWidth
            };
            foreach (var bbox in sboxes)
                skCanvas.DrawRect(bbox, paint2);
        }

        // Step 4: Save the modified image back to a binary array
        using var outputStream = new MemoryStream();
        using var skImageModified = SKImage.FromBitmap(skBitmap);
        using var skData = skImageModified.Encode(SKEncodedImageFormat.Png, 100); // Save as PNG with high quality

        skData.SaveTo(outputStream);

        return outputStream.ToArray();
    }
    
    public override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
    }
}
