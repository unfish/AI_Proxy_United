using System.Net;
using System.Security.Cryptography;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.智谱清言, "智谱清言", "智谱清言GLM-4v，来自清华，中文开源大模型中最强，支持图文，支持function call。", 20,  canUseFunction:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 50, priceOut: 50)]
public class ApiZhiPu:ApiBase
{
    protected ZhiPuClient _client;
    public ApiZhiPu(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<ZhiPuClient>();
    }
    
    /// <summary>
    /// 使用智谱AI来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.SendMessageStream(input))
        {
            yield return resp;
        }
    }

    /// <summary>
    /// 使用智谱AI来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
    
    /// <summary>
    /// 只能输入单个字符串，512字符以内，返回向量长度1024
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var resp = await _client.Embeddings(qc, embedForQuery);
        return resp;
    }
}

[ApiClass(M.智谱免费, "智谱免费", "智谱清言Flash版免费模型，来自清华，免费，效果不错。", 21, canUseFunction:true, canProcessImage:true, canProcessMultiImages:true,  priceIn: 0, priceOut: 0)]
public class ApiZhiPuFlash : ApiZhiPu
{
    public ApiZhiPuFlash(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModel("glm-4-flash");
        _client.SetImageModel("glm-4v-flash");
    }
}

[ApiClass(M.智谱Zero, "智谱Zero", "智谱清言Zero-preview，类o1思考模型。", 115, ApiClassTypeEnum.推理模型, canUseFunction:true, canProcessImage:true, canProcessMultiImages:true, priceIn: 50, priceOut: 50)]
public class ApiZhiPuZero : ApiZhiPu
{
    public ApiZhiPuZero(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client.SetModel("glm-zero-preview");
        _client.SetImageModel("glm-zero-preview");
    }
}

[ApiClass(M.CogView, "智谱画图", "智谱CogView 4文本画图，中英文都可以，支持超长提示词，支持在图片中生成中文文字内容。", 204, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.06)]
public class ApiZhiPuCogView:ApiBase
{
    private ZhiPuClient _client;
    public ApiZhiPuCogView(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<ZhiPuClient>();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        var res = await _client.TextToImage(input);
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
    
    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return _client.GetExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        _client.SetExtraOptions(ext_userId, type, value);
    }
}


[ApiClass(M.智谱视频, "智谱视频", "智谱清言CogVideoX文生视频大模型，可以用文字描述或文字加图片来生成6秒视频，图片需要使用横屏图片。", 220, type: ApiClassTypeEnum.视频模型, canProcessImage:true, priceIn: 0, priceOut: 0.5)]
public class ApiZhiPuVideo:ApiBase
{
    protected ZhiPuClient _client;
    public ApiZhiPuVideo(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<ZhiPuClient>();
    }
    
    /// <summary>
    /// 使用智谱AI来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.TextToVideo(input))
        {
            yield return resp;
        }
    }

    /// <summary>
    /// 使用智谱AI来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("视频接口不支持Query调用");
    }
}

[ApiClass(M.智谱搜索, "智谱搜索", "智谱提供的网络搜索API接口。", 187, type: ApiClassTypeEnum.辅助模型)]
public class ApiZhiPuWebSearch:ApiBase
{
    private ZhiPuClient _client;
    public ApiZhiPuWebSearch(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<ZhiPuClient>();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        var res = await _client.WebSearch(input);
        yield return res;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var res = await _client.WebSearch(input);
        return res;
    }

}

/// <summary>
/// 智谱AI大模型接口
/// 文档地址 https://open.bigmodel.cn/dev/api#nosdk
/// </summary>
public class ZhiPuClient: OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public ZhiPuClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        AppId = configHelper.GetConfig<string>("Service:ZhiPu:APPID");
        AppSecret = configHelper.GetConfig<string>("Service:ZhiPu:Secret");
    }
    private static String hostUrl = "https://open.bigmodel.cn/api/paas/v4/chat/completions";
    private static String imgHostUrl = "https://open.bigmodel.cn/api/paas/v4/images/generations";

    private String AppId;//从开放平台控制台中获取
    private String AppSecret;//从开放平台控制台中获取
    private string modelName = "glm-4-plus";
    private string imageModelName = "glm-4v-plus";

    public void SetModel(string name)
    {
        modelName = name;
    }
    public void SetImageModel(string name)
    {
        imageModelName = name;
    }
    
    private string GetJwtToken()
    {
        var header = "{\"alg\":\"HS256\",\"sign_type\":\"SIGN\"}";
        var payload = JsonConvert.SerializeObject(new
        {
            api_key = AppId, exp = GetMillSeconds(DateTime.Now.AddMinutes(10)), timestamp = GetMillSeconds(DateTime.Now)
        });
        var bHeader = Base64UrlEncode(header);
        var bPayload = Base64UrlEncode(payload);
        var t = Base64UrlEncode(HmacSha256($"{bHeader}.{bPayload}", AppSecret));
        return $"{bHeader}.{bPayload}.{t}";
    }

    private static long GetMillSeconds(DateTime dt)
    {
        return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
    }
    private static string Base64UrlEncode(string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        return Base64UrlEncode(bytes);
    }
    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", string.Empty);
    }
    private static byte[] HmacSha256(string str, string key)
    {
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(str));
    }
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    private string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        var model = isImageMsg ? imageModelName : modelName;
        int? max_tokens = isImageMsg ? 1024 : 4096;
        if (model.Contains("zero"))
        {
            max_tokens = null;
            input.ChatContexts.SystemPrompt = "";
            input.ChatContexts.AddQuestion("Please think deeply before your response.", ChatType.System);
        }

        var msgs = GetFullMessages(input.ChatContexts);
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            model = model,
            messages = msgs,
            tools,
            temperature = input.Temprature,
            max_tokens = max_tokens,
            user = input.External_UserId,
            stream
        }, jSetting);
    }
    
    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", GetJwtToken());
        var url = hostUrl;
        var msg = GetMsgBody(input, true);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);
        
        await foreach (var resp in ProcessStreamResponse(response))
            yield return resp;
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", GetJwtToken());
        var url = hostUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessQueryResponse(resp);
    }
    
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1024x1024"),
                    new KeyValuePair<string, string>("横屏", "1344x768"),
                    new KeyValuePair<string, string>("竖屏", "768x1344")
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
    public async Task<Result> TextToImage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", GetJwtToken());
        var url = imgHostUrl;
        var msg = JsonConvert.SerializeObject(new
        {
            model="cogview-4",
            size = GetExtraOptions(input.External_UserId)[0].CurrentValue,
            prompt = input.ChatContexts.Contexts.Last().QC.Last().Content
        });
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var result = await resp.Content.ReadAsStringAsync();
        var o = JObject.Parse(result);
        if (o["data"] != null)
        {
            url = o["data"][0]["url"].Value<string>();
            var file = await client.GetByteArrayAsync(url);
            return FileResult.Answer(file, "png", ResultType.ImageBytes, "image.png");
        }
        return Result.Error(result);
    }
    
    public async IAsyncEnumerable<Result> TextToVideo(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", GetJwtToken());
        var url = "https://open.bigmodel.cn/api/paas/v4/videos/generations";
        var prompt = input.ChatContexts.Contexts.Last().QC.First(t => t.Type == ChatType.文本).Content;
        var image = input.ChatContexts.Contexts.Last().QC.FirstOrDefault(t => t.Type == ChatType.图片Base64)?.Content;
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        var msg = JsonConvert.SerializeObject(new
        {
            model="cogvideox",
            prompt = prompt,
            image_url = image,
            user_id = input.External_UserId
        }, jSetting);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var result = await resp.Content.ReadAsStringAsync();
        var o = JObject.Parse(result);
        if (o["id"] != null)
        {
            var taskId = o["id"].Value<string>();
            await foreach (var resp2 in CheckVideoTask(taskId))
            {
                yield return resp2;
            }
        }
        else
        {
            yield return Result.Error(result);
        }
    }

    public async IAsyncEnumerable<Result> CheckVideoTask(string taskid)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var url = "https://open.bigmodel.cn/api/paas/v4/async-result/"+taskid;
        client.DefaultRequestHeaders.Add("Authorization", GetJwtToken());
        int times = 0;
        while (true)
        {
            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if (json["task_status"] != null)
            {
                var state = json["task_status"].Value<string>();
                if (state == "PROCESSING")
                {
                    times++;
                    yield return Result.Waiting(times.ToString());
                    Thread.Sleep(2000);
                }
                else if (state == "SUCCESS")
                {
                    if (json["video_result"][0]["url"]!=null)
                    {
                        yield return Result.Waiting("生成完成，下载视频...");
                        var video_url = json["video_result"][0]["url"].Value<string>();
                        var bytes = await client.GetByteArrayAsync(video_url);
                        var img_url = json["video_result"][0]["cover_image_url"].Value<string>();
                        var bytes2 = await client.GetByteArrayAsync(img_url);
                        yield return VideoFileResult.Answer(bytes, "mp4", "video.mp4", bytes2);
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
    
    private static String embedUrl = "https://open.bigmodel.cn/api/paas/v4/embeddings";
    private string GetEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        return JsonConvert.SerializeObject(new
        {
            input = qc.Last().Content,
            model = "embedding-2"
        });
    }
    private class EmbeddingsResponse
    {
        public string Object { get; set; }
        public EmbeddingObject[] Data { get; set; }
    }
    private class EmbeddingObject
    {
        public string Object { get; set; }
        public double[] Embedding { get; set; }
        public int Index { get; set; }
    }
    public async Task<(ResultType resultType, double[][]? result, string error)> Embeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", GetJwtToken());
        var url = embedUrl;
        var msg = GetEmbeddingsMsgBody(qc, embedForQuery);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var o = JsonConvert.DeserializeObject<EmbeddingsResponse>(await resp.Content.ReadAsStringAsync());
        return (ResultType.Answer, o.Data.Select(t => t.Embedding).ToArray(), string.Empty);
    }
    
    
    public async Task<Result> WebSearch(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", GetJwtToken());
        var url = "https://open.bigmodel.cn/api/paas/v4/tools";
        var msg = JsonConvert.SerializeObject(new
        {
            tool = "web-search-pro",
            messages = new[]
            {
                new
                {
                    role = "user", content = input.ChatContexts.Contexts.Last().QC.Last().Content
                }
            }
        });
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var result = await resp.Content.ReadAsStringAsync();
        var o = JObject.Parse(result);
        if (o["choices"] != null)
        {
            var list = new List<SearchResultDto>();
            var arr = o["choices"][0]["message"]["tool_calls"] as JArray;
            foreach (var tk in arr)
            {
                if (tk["type"].Value<string>() == "search_result")
                {
                    var search_results = tk["search_result"] as JArray;
                    foreach (var hit in search_results)
                    {
                        var dto = new SearchResultDto()
                        {
                            title = hit["title"].Value<string>(),
                            url = hit["link"].Value<string>(),
                            content = hit["content"].Value<string>()
                        };
                        list.Add(dto);
                    }
                    return SearchResult.Answer(list);
                }
            }
        }
        return Result.Error(result);
    }
}