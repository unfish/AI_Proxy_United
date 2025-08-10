using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("PPIOMedia")]
public class ApiPPIOMediaProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiPPIOMediaProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        if (_modelName.Contains("image"))
        {
            extraOptionsList = new List<ExtraOption>()
            {
                new ExtraOption()
                {
                    Type = "尺寸", Contents = new[]
                    {
                        new KeyValuePair<string, string>("方形", "1536*1536"),
                        new KeyValuePair<string, string>("横屏", "1536*1280"),
                        new KeyValuePair<string, string>("竖屏", "1280*1536"),
                        new KeyValuePair<string, string>("宽横屏", "1536*1024"),
                        new KeyValuePair<string, string>("长竖屏", "1024*1536")
                    }
                }
            };
        }
    }
    
    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        var url = _host + "async/" + _modelName;
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", _key);
        
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msg = JsonConvert.SerializeObject(new
        {
            prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
            size = _modelName.Contains("image") ? GetExtraOptions(input.External_UserId)[0].CurrentValue : null
        }, jSetting);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["task_id"] != null)
        {
            var taskId = json["task_id"].Value<string>();
            await foreach (var res in CheckTask(taskId))
            {
                yield return res;
            }
        }
        else
        {
            yield return Result.Error(content);
        }
    }

    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }


    private async IAsyncEnumerable<Result> CheckTask(string taskid)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = "https://api.ppinfra.com/v3/async/task-result?task_id=" + taskid;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_key}");
        int times = 0;
        while (true)
        {
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["task"] != null && json["task"]["task_id"] != null)
            {
                var state = json["task"]["status"].Value<string>();
                if (state == "TASK_STATUS_RUNNING" || state == "TASK_STATUS_QUEUED"|| state == "TASK_STATUS_PROCESSING")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else if (state == "TASK_STATUS_SUCCEED")
                {
                    yield return Result.Waiting("生成完成，正在下载...");
                    var arr = json["images"] as JArray;
                    foreach (var t in arr)
                    {
                        if (t["image_url"] != null)
                        {
                            var imageUrl = t["image_url"].Value<string>();
                            var bytes = await client.GetByteArrayAsync(imageUrl);
                            yield return FileResult.Answer(bytes, "jpeg", ResultType.ImageBytes);
                        }
                        else
                            yield return Result.Error(t["code"].Value<string>());
                    }
                    var arr2 = json["videos"] as JArray;
                    foreach (var t in arr2)
                    {
                        if (t["video_url"] != null)
                        {
                            var imageUrl = t["video_url"].Value<string>();
                            var bytes = await client.GetByteArrayAsync(imageUrl);
                            yield return FileResult.Answer(bytes, "mp4", ResultType.VideoBytes, "video.mp4", 6000);
                        }
                        else
                            yield return Result.Error(t["code"].Value<string>());
                    }
                    yield break;
                }
                else
                {
                    yield return Result.Error(content);
                    yield break;
                }
            }
            else
            {
                yield return Result.Error(content);
                yield break;
            }
        }
    }
}
