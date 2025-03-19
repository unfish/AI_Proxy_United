using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.MidJourney, "Midjourney", "MidJourney是文本画图届的领军人物，尽量使用英文指令，中文也可以但细节控制略差一点，同时需要使用一些特殊的参数来控制画图效果。\n--ar 3:2可以控制画面比例，--niji可以指定动漫风格模型，--no 可以指定不出现什么内容。", 206, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 1)]
public class ApiMidJourney:ApiBase
{
    private MidJourneyClient _client;
    public ApiMidJourney(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<MidJourneyClient>();
    }
    
    /// <summary>
    /// 使用MidJourney来画图
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        if (input.MidJourneyTunnel != "NORMAL")
            input.MidJourneyTunnel = "FAST";
        var q = input.ChatContexts.Contexts.Last().QC.Last();
        if (q.Type == ChatType.图片Base64)
        {
            var tunnel = "NORMAL";
            var res = await _client.CreateMidJourneyDescribeTask(Convert.FromBase64String(q.Content), tunnel);
            if (res.success)
            {
                await foreach (var resp in _client.CheckMidJourneyTask(res.taskid, tunnel))
                {
                    yield return resp;
                }
            }
            else
            {
                yield return Result.Error(res.taskid);
            }
        }
        else
        {
            var res = await _client.CreateMidJourneyTask(input.ChatContexts.Contexts.Last().QC.Last().Content,
                input.MidJourneyTunnel);
            if (res.success)
            {
                await foreach (var resp in _client.CheckMidJourneyTask(res.taskid, input.MidJourneyTunnel))
                {
                    yield return resp;
                }
            }
            else
            {
                yield return Result.Error(res.taskid);
            }
        }
    }

    /// <summary>
    /// 使用MidJourney后续动作按钮
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> ProcessAction(string taskId, string customId, string tunnel)
    {
        var res = await _client.CreateMidJourneyActionTask(taskId, customId, tunnel);
        if (res.success)
        {
            await foreach (var resp in _client.CheckMidJourneyTask(res.taskid, tunnel))
            {
                yield return resp;
            }
        }
        else
        {
            yield return Result.Error(res.taskid);
        }
    }
    
    /// <summary>
    /// 使用MidJourney来画图
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }
}



/// <summary>
/// MidJourney接口，跟Open AI接口一样，通过https://aigptx.top/网站的接口
/// 文档地址 https://ohmygpt-docs.apifox.cn/api-107358560
/// </summary>
public class MidJourneyClient: IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private ConfigHelper _configuration;
    public MidJourneyClient(IHttpClientFactory httpClientFactory, ConfigHelper configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        APIKEY = configuration.GetConfig<string>("OhMyGpt:Key");
        hostUrl = configuration.GetConfig<string>("OhMyGpt:Host");
    }
    
    private String hostUrl;
    private String APIKEY;//从开放平台控制台中获取
    
    //画图接口
    private static String createImagePath = "api/v1/ai/draw/mj/imagine";
    //执行动作接口
    private static String actionPath = "api/v1/ai/draw/mj/action";
    //查询任务状态接口
    private static String checkTaskPath = "api/v1/ai/draw/mj/query";
    //解释图片接口
    private static String describeTaskPath = "api/v1/ai/draw/mj/describe";
    
    #region 画图
    public KeyValuePair<string,string>[] GetCreateMsgBody(string prompt, string tunnel)
    {
        return new[]
        {
            new KeyValuePair<string, string>("model", "midjourney"),
            new KeyValuePair<string, string>("prompt", prompt),
            new KeyValuePair<string, string>("type", tunnel) //NORMAL或FAST，FAST贵一点
        };
    }
    public KeyValuePair<string,string>[] GetActionMsgBody(string taskId, string customId, string tunnel)
    {
        return new[]
        {
            new KeyValuePair<string, string>("model", "midjourney"),
            new KeyValuePair<string, string>("taskId", taskId),
            new KeyValuePair<string, string>("customId", customId),
            new KeyValuePair<string, string>("type", tunnel) //NORMAL或FAST，FAST贵一点，必须跟前面的任务一致
        };
    }
    public KeyValuePair<string,string>[] GetCheckTaskMsgBody(string taskId)
    {
        return new[]
        {
            new KeyValuePair<string, string>("model", "midjourney"),
            new KeyValuePair<string, string>("taskId", taskId)
        };
    }
    
    public KeyValuePair<string,string>[] GetDescribeMsgBody(byte[] file, string tunnel)
    {
        return new[]
        {
            new KeyValuePair<string, string>("model", "midjourney"),
            new KeyValuePair<string, string>("base64", "data:image/jpeg;base64,"+Convert.ToBase64String(file)),
            new KeyValuePair<string, string>("type", tunnel) //NORMAL或FAST，FAST贵一点
        };
    }
    
    public async Task<(bool success, string taskid)> CreateMidJourneyTask(string prompt, string tunnel)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = hostUrl+createImagePath;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        var msg = GetCreateMsgBody(prompt, tunnel);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(msg)
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["statusCode"] != null && json["statusCode"].Value<int>()==200)
        {
            return (true, json["data"].Value<int>().ToString());
        }
        else
        {
            return (false, content); //{"code":"","message":""}
        }
    }
    
    
    public async Task<(bool success, string taskid)> CreateMidJourneyActionTask(string taskId, string customId, string tunnel)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = hostUrl+actionPath;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        var msg = GetActionMsgBody(taskId, customId, tunnel);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(msg)
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["statusCode"] != null && json["statusCode"].Value<int>()==200)
        {
            return (true, json["data"].Value<int>().ToString());
        }
        else
        {
            return (false, content); //{"code":"","message":""}
        }
    }
    
    public async Task<(bool success, string taskid)> CreateMidJourneyDescribeTask(byte[] file, string tunnel)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = hostUrl+describeTaskPath;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        var msg = GetDescribeMsgBody(file, tunnel);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(msg)
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["statusCode"] != null && json["statusCode"].Value<int>()==200)
        {
            return (true, json["data"].Value<int>().ToString());
        }
        else
        {
            return (false, content); //{"code":"","message":""}
        }
    }
    
    public async IAsyncEnumerable<Result> CheckMidJourneyTask(string taskid, string tunnel)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = hostUrl+checkTaskPath;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {APIKEY}");
        int times = 0;
        while (true)
        {
            var msg = GetCheckTaskMsgBody(taskid);
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(msg)
            });
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["statusCode"] != null && json["statusCode"].Value<int>() == 200)
            {
                var state = json["data"]["status"].Value<string>();
                if (state == "IN_PROGRESS" || state == "NOT_START" || state == "SUBMITTED")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else if (state == "SUCCESS")
                {
                    if (json["data"]["imageDcUrl"]!=null && json["data"]["actions"]!=null)
                    {
                        yield return Result.Waiting("画图完成，下载图片...");
                        var img_url = json["data"]["imageDcUrl"].Value<string>();
                        var proxy = _configuration.GetConfig<string>("Discord:CdnHost");
                        if (!string.IsNullOrEmpty(proxy))
                            img_url = img_url.Replace("https://cdn.discordapp.com/", proxy);
                        var bytes = await client.GetByteArrayAsync(img_url);
                        yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
                        var arr = json["data"]["actions"] as JArray;
                        var actions = new MidJourneyActions()
                        {
                            TaskId = taskid, Tunnel = tunnel,
                            Actions = arr?.Select(t => t["customId"].Value<string>()).ToArray()
                        };
                        yield return MidjourneyActionsResult.Answer(actions);
                    }
                    else
                    {
                        var desc = json["data"]["prompt"].Value<string>();
                        yield return Result.Answer(desc);
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
    
    public class MidJourneyActions
    {
        public string TaskId { get; set; }
        public string Tunnel { get; set; }
        public string[] Actions { get; set; }
    }
    
    #endregion
}