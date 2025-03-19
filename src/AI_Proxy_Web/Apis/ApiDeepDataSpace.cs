using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.DeepData计数, "DeepData计数", "DeepDataSpace T-Rex交互式目标检测和计数系统，上传一张目标图片和一张参考图片，并指定参考图片中要数数的目标的位置或方框坐标，自动在目标图片中找出所有的目标图片。", 320, type: ApiClassTypeEnum.辅助模型,  priceIn:0, priceOut: 3)]
public class ApiDeepDataSpace:ApiBase
{
    private DeepDataSpaceClient _client;
    public ApiDeepDataSpace(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<DeepDataSpaceClient>();
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach(var resp in _client.SendMessage(input))
            yield return resp;
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("该接口不支持Query调用");
    }
}

/// <summary>
/// deepdataspace API接口
/// 文档地址 https://cloud.deepdataspace.com/docs#/api/trex_generic_infer
/// </summary>
public class DeepDataSpaceClient: IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    public DeepDataSpaceClient(IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        
        APIKEY = configHelper.GetConfig<string>("Service:DeepDataSpace:Key");
        hostUrl = configHelper.GetConfig<string>("Service:DeepDataSpace:Host");
    }
    private String hostUrl;
    private String APIKEY;
    
    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessage(ApiChatInputIntern input)
    {
        var ctx = input.ChatContexts.Contexts.Last();
        var url = hostUrl;
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Token", APIKEY);
        var image1 = ctx.QC.FirstOrDefault(t => t.Type == ChatType.图片Base64)?.Content;
        var text = ctx.QC.FirstOrDefault(t => t.Type == ChatType.坐标).Content;
        var arr = JArray.Parse(text);
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msg = JsonConvert.SerializeObject(new
        {
            batch_infers = new[]
            {
                new
                {
                    image = "data:image/jpeg;base64," + image1,
                    prompt_type = "point",
                    prompts = new[]
                    {
                        new
                        {
                            category_id = 1, points =new[]{arr}
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
            while (true)
            {
                url = "https://api.deepdataspace.com/task_statuses/" + id;
                resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
                content = await resp.Content.ReadAsStringAsync();
                json = JObject.Parse(content);
                var state = json["data"]["status"].Value<string>();
                if (state == "success")
                {
                    var results = json["data"]["result"]["object_batches"][0] as JArray;
                    yield return Result.Answer($"共检测到 {results.Count} 个目标对象");
                    var bboxes = new List<SKRect>();
                    foreach (var result in results)
                    {
                        var box = (result["bbox"] as JArray).ToObject<float[]>();
                        bboxes.Add(new SKRect(box[0], box[1], box[2], box[3]));
                    }
                    var bytes = DrawBoundingBox(Convert.FromBase64String(image1), bboxes.ToArray(), SKColors.Red, 1);
                    yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
                    break;
                }
                else if (state == "waiting" || state == "running")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
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
    
    public byte[] DrawBoundingBox(byte[] imageData, SKRect[] bboxes, SKColor color, float strokeWidth)
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

        // Step 4: Save the modified image back to a binary array
        using var outputStream = new MemoryStream();
        using var skImageModified = SKImage.FromBitmap(skBitmap);
        using var skData = skImageModified.Encode(SKEncodedImageFormat.Png, 100); // Save as PNG with high quality

        skData.SaveTo(outputStream);

        return outputStream.ToArray();
    }
}