using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("QwenImage")]
public class ApiQwenImageProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiQwenImageProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
    }
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        extraOptionsList = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1328*1328"),
                    new KeyValuePair<string, string>("横屏", "1472*1140"),
                    new KeyValuePair<string, string>("竖屏", "1140*1472")
                }
            }
        };
    }
    
    private static string textToImageUrl =
        "https://dashscope.aliyuncs.com/api/v1/services/aigc/text2image/image-synthesis";
    private static string textToImageTaskUrl = "https://dashscope.aliyuncs.com/api/v1/tasks/";

    private static string imageEditUrl =
        "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";
    
    public string GetTextToImageMsgBody(ApiChatInputIntern input)
    {
        var qc = input.ChatContexts.Contexts.Last().QC;
        var body = new
        {
            model = _modelName,
            input = new
            {
                prompt = qc.FirstOrDefault(t=>t.Type== ChatType.文本)?.Content
            },
            parameters = new
            {
                size = GetExtraOptions(input.External_UserId)[0].CurrentValue
            }
        };
        
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(body, jSetting);
    }
    public string GetImageEditMsgBody(ApiChatInputIntern input)
    {
        var qc = input.ChatContexts.Contexts.Last().QC;
        var iq = qc.FirstOrDefault(t => t.Type == ChatType.图片Base64);
        var tq = qc.FirstOrDefault(t => t.Type == ChatType.文本);
        var body = new
        {
            model = _visionModelName,
            input = new
            {
                messages = new[]
                {
                    new
                    {
                        role="user",
                        content= new object[]
                        {
                            new{image = $"data:{(string.IsNullOrEmpty(iq?.MimeType) ? "image/jpeg" : iq.MimeType)};base64," +
                                        iq?.Content},
                            new{text = tq?.Content}
                        }
                    }
                }
            }
        };
        
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(body, jSetting);
    }
    
    public async IAsyncEnumerable<Result> TextToImage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        client.DefaultRequestHeaders.Add("X-DashScope-Async", "enable");
        client.DefaultRequestHeaders.Add("X-DashScope-DataInspection", "disable");
        var url = textToImageUrl;
        var msg = GetTextToImageMsgBody(input);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["output"] != null && json["output"]["task_id"] != null)
        {
            var taskid = json["output"]["task_id"].Value<string>();
            await foreach (var res in CheckWanXiangTask(taskid))
                yield return res;
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
    private async IAsyncEnumerable<Result> CheckWanXiangTask(string taskid)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = textToImageTaskUrl+taskid;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        int times = 0;
        while (true)
        {
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["output"] != null && json["output"]["task_id"] != null)
            {
                var state = json["output"]["task_status"].Value<string>();
                if (state == "RUNNING"||state == "PENDING")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else if (state == "SUCCEEDED")
                {
                    yield return Result.Waiting("画图完成，下载图片...");

                    var arr = json["output"]["results"] as JArray;
                    foreach (var t in arr)
                    {
                        if (t["url"] != null)
                        {
                            var imageUrl = t["url"].Value<string>();
                            var bytes = await client.GetByteArrayAsync(imageUrl);
                            yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
                        }
                        else
                            yield return Result.Error(t["code"].Value<string>());
                    }
                    break;
                }
                else
                {
                    yield return Result.Error(content);
                    break;
                }
            }
            else
            {
                yield return Result.Error(content);
                break;
            }
        }
    }
    
    public async IAsyncEnumerable<Result> ImageEdit(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        client.DefaultRequestHeaders.Add("X-DashScope-DataInspection", "disable");
        var url = imageEditUrl;
        var msg = GetImageEditMsgBody(input);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["output"] != null && json["output"]["choices"] != null)
        {
            var arr = json["output"]["choices"] as JArray;
            foreach (var t in arr)
            {
                var imageUrl = t["message"]["content"][0]["image"].Value<string>();
                var bytes = await client.GetByteArrayAsync(imageUrl);
                yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
            }
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        if (input.ChatContexts.HasImage())
        {
            await foreach (var message in ImageEdit(input))
                yield return message;
        }
        else
        {
            await foreach (var message in TextToImage(input))
                yield return message;
        }
    }
    
    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }
    
}
