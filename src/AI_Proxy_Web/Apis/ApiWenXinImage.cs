using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.文心一格, "文心一格", "百度推出的AI画图应用，支持中文指令，支持直接在提示词中指定风格，语法参照：https://ai.baidu.com/ai-doc/NLP/4libyluzs", 202, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.2)]
public class ApiWenXinImage:ApiBase
{
    private WenXinImageClient _client;
    public ApiWenXinImage(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<WenXinImageClient>();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        var res = await _client.CreateTask(input.ChatContexts.Contexts.Last().QC.Last().Content, input.ImageSize);
        if (res.success)
        {
            await foreach (var resp in _client.CheckTask(res.taskid))
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
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }

    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        if(string.IsNullOrEmpty(input.ImageSize))
            input.ImageSize = _client.GetExtraOptions(input.External_UserId)[0].CurrentValue;
    }
    
    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return _client.GetExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        _client.SetExtraOptions(ext_userId, type, value);
    }
}


[ApiClass(M.文心iRAG, "文心iRAG", "百度推出的AI画图应用，支持中文指令，大大降低幻觉问题，可以画指定的人物和景物图。", 202,
    type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.2)]
public class ApiWenXinIRagImage : ApiBase
{
    private WenXinClient _client;

    public ApiWenXinIRagImage(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<WenXinClient>();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        var res = await _client.TextToImageIRag(input);
        yield return res;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }
}


/// <summary>
/// 百度文心一格画图大模型接口
/// 文档地址 https://ai.baidu.com/ai-doc/NLP/1lg53dryv
/// </summary>
public class WenXinImageClient: IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    public WenXinImageClient(IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        APIKEY = configHelper.GetConfig<string>("Service:WenXinImage:Key");
        APISecret = configHelper.GetConfig<string>("Service:WenXinImage:Secret");
        AccessTokenCacheKey = $"{APIKEY}_Token";
    }
    private String APIKEY;//从开放平台控制台中获取
    private String APISecret;//从开放平台控制台中获取

    private string AccessTokenCacheKey;
    public static DateTime NextRefreshTime = DateTime.Now;

    private async Task<string> GetAccessToken(HttpClient client)
    {
        var token = CacheService.Get<string>(AccessTokenCacheKey);
        //获取的accesstoken 3天更新一次，官方说明是30天有效, https://cloud.baidu.com/doc/WENXINWORKSHOP/s/Ilkkrb0i5
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }

        var url =
            $"https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id={APIKEY}&client_secret={APISecret}";
        var resp = await client.GetStringAsync(url);
        var json = JObject.Parse(resp);
        token = json["access_token"].Value<string>();
        CacheService.Save(AccessTokenCacheKey, token, DateTime.Now.AddDays(10));
        return token;
    }

    //画图接口
    private static String imageHostUrl =
        "https://aip.baidubce.com/rpc/2.0/ernievilg/v1/txt2imgv2";
    //查询任务状态接口
    private static String imageTaskUrl = "https://aip.baidubce.com/rpc/2.0/ernievilg/v1/getImgv2";
    
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1024*1024"),
                    new KeyValuePair<string, string>("横屏", "1280*720"),
                    new KeyValuePair<string, string>("竖屏", "720*1280")
                }
            }
        };
        foreach (var option in list)
        {
            var cacheKey = $"{ext_userId}_{this.GetType().Name}_{option.Type}";
            var v = CacheService.Get<string>(cacheKey);
            option.CurrentValue = string.IsNullOrEmpty(v) ? option.Contents.First().Value : v;
        }
        return list;
    }
    public void SetExtraOptions(string ext_userId, string type, string value)
    {
        var cacheKey = $"{ext_userId}_{this.GetType().Name}_{type}";
        CacheService.Save(cacheKey, value, DateTime.Now.AddDays(30));
    }
    
    public string GetMsgBody(string prompt, string size)
    {
        var body = new
        {
            prompt = prompt,
            width =int.Parse(size.Split('*')[0]),
            height = int.Parse(size.Split('*')[1]),
            image_num = 1,
        };
        return JsonConvert.SerializeObject(body);
    }
    
    public async Task<(bool success, string taskid)> CreateTask(string prompt, string size)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = imageHostUrl+"?access_token="+(await GetAccessToken(client));
        var msg = GetMsgBody(prompt, size);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["data"] != null && json["data"]["task_id"] != null)
        {
            return (true, json["data"]["task_id"].Value<string>());
        }
        else
        {
            return (false, content);
        }
    }
    
    public async IAsyncEnumerable<Result> CheckTask(string taskid)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = imageTaskUrl+"?access_token="+(await GetAccessToken(client));
        int times = 0;
        while (true)
        {
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonConvert.SerializeObject(new { task_id = taskid }), Encoding.UTF8,
                    "application/json")
            });
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["data"] != null && json["data"]["task_id"] != null)
            {
                var state = json["data"]["task_status"].Value<string>();
                if (state == "RUNNING"||state == "INIT"||state == "WAIT")
                {
                    times++;
                    yield return Result.New(ResultType.Waiting, times.ToString());
                    Thread.Sleep(2000);
                }
                else if (state == "SUCCESS")
                {
                    var arr = json["data"]["sub_task_result_list"] as JArray;
                    foreach (var t in arr)
                    {
                        var subArr = t["final_image_list"] as JArray;
                        if (subArr != null && subArr.Count > 0)
                        {
                            foreach (var st in subArr)
                            {
                                if (st["img_url"] != null)
                                {
                                    var bytes = await client.GetByteArrayAsync(st["img_url"].Value<string>());
                                    yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
                                }
                            }
                        }
                        else
                            yield return Result.Error(t["sub_task_error_code"].ToString());
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