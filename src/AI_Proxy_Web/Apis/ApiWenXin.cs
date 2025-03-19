using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;


[ApiClass(M.百度文心, "文心4.5", "百度文心一言4.5，对话能力提升，支持图片，但不支持function call。", 13, canUseFunction:false, canProcessImage:true, canProcessMultiImages:true, priceIn: 4, priceOut: 16)]
public class ApiWenXin:ApiBase
{
    protected WenXinClient _client;
    public ApiWenXin(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<WenXinClient>();
    }
    
    /// <summary>
    /// 使用文心一言来回答
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
    /// 使用文心一言来回答
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
    
    /// <summary>
    /// 可输入数组，每个字符串长度1000字以内，返回向量长度384
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var resp = await _client.Embeddings(qc, embedForQuery);
        return resp;
    }
}

[ApiClass(M.Baidu_DeepSeekR1, "DS R1百度版", "DeepSeek R1百度云备用通道。", 120, type: ApiClassTypeEnum.推理模型, priceIn: 4, priceOut: 16)]
public class ApiWenXinDeepSeekR1 : ApiWenXin
{
    public ApiWenXinDeepSeekR1(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client.SetModel("deepseek-r1");
    }
}

/// <summary>
/// 文心一言大模型接口
/// 文档地址 https://cloud.baidu.com/doc/WENXINWORKSHOP/s/4lilb2lpf
/// </summary>
public class WenXinClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public WenXinClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        
        APIKEY = configHelper.GetConfig<string>("Service:WenXin:Key");
        APISecret = configHelper.GetConfig<string>("Service:WenXin:Secret");
        AK = configHelper.GetConfig<string>("Service:WenXin:AK");
        SK = configHelper.GetConfig<string>("Service:WenXin:SK");
        
        AccessTokenCacheKey = $"{APIKEY}_Token";
    }
    
    private static String v2HostUrl = "https://qianfan.baidubce.com/v2/chat/completions";
    private static String embedUrl = "https://aip.baidubce.com/rpc/2.0/ai_custom/v1/wenxinworkshop/embeddings/embedding-v1";

    private String APIKEY;//从开放平台控制台中获取
    private String APISecret;//从开放平台控制台中获取
    
    private String AK;//从开放平台控制台中获取
    private String SK;//从开放平台控制台中获取
    private string BearTokenCacheKey = "BaiduV1_BearToken";

    private string AccessTokenCacheKey;
    public static DateTime NextRefreshTime = DateTime.Now;
    private string modelName = "ernie-4.5-8k-preview";

    public void SetModel(string name)
    {
        this.modelName = name;
    }
    
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

    /// <summary>
    /// OpenAI 兼容接口使用 BearToken 认证，文档https://cloud.baidu.com/doc/WENXINWORKSHOP/s/Um2wxbaps
    /// </summary>
    /// <param name="client"></param>
    /// <returns></returns>
    public async Task<string> GetBearerToken(HttpClient client)
    {
        var token = CacheService.Get<string>(BearTokenCacheKey);
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }
        var timestamp = DateTime.UtcNow.ToString("s") + "Z";
        var expire = 2592000;
        var prefix = $"bce-auth-v1/{AK}/{timestamp}/{expire}";
        var canonical = $"GET\n/v1/BCE-BEARER/token\nexpireInSeconds={expire}\nhost:iam.bj.baidubce.com";
        var signKey = HashHelper.GetSha256HEX(SK, prefix);
        var signature = HashHelper.GetSha256HEX(signKey, canonical);
        var requestToken = $"{prefix}/host/{signature}";
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", requestToken);
        var url = "https://iam.bj.baidubce.com/v1/BCE-BEARER/token?expireInSeconds=" + expire;
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url));
        var respContent = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(respContent);
        token = json["token"].Value<string>();
        CacheService.Save(BearTokenCacheKey, token, DateTime.Now.AddDays(29));
        return token;
    }
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if (!string.IsNullOrEmpty(funcPrompt))
            input.ChatContexts.AddQuestion(funcPrompt, ChatType.System);
        var msgs = GetFullMessages(input.ChatContexts);
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            model = modelName,
            messages = msgs,
            temperature = input.Temprature,
            stream,
            tools,
            max_completion_tokens = 2048,
            user = input.External_UserId,
            web_search = new{enable=true}
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
        var token = await GetBearerToken(client);
        client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {token}");
        var url = v2HostUrl;
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
        var token = await GetBearerToken(client);
        client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {token}");
        var url = v2HostUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessQueryResponse(resp);
    }
    
    public async Task<Result> TextToImageIRag(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var token = await GetBearerToken(client);
        client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {token}");
        var url = "https://qianfan.baidubce.com/v2/images/generations";
        var msg = JsonConvert.SerializeObject(new
        {
            model = "irag-1.0",
            prompt = input.ChatContexts.Contexts.Last().QC.Last().Content
        });
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var o = JObject.Parse(content);
        var imageurl = o["data"][0]["url"].Value<string>();
        client = _httpClientFactory.CreateClient();
        var bytes = await client.GetByteArrayAsync(imageurl);
        return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
    }
    
    
    private string GetEmbeddingsMsgBody(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        var embeddings = qc.Select(t => t.Content).ToArray();
        return JsonConvert.SerializeObject(new
        {
            input = embeddings
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
        var url = embedUrl+"?access_token="+(await GetAccessToken(client));
        var msg = GetEmbeddingsMsgBody(qc, embedForQuery);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            var result = JsonConvert.DeserializeObject<EmbeddingsResponse>(content);
            return (ResultType.Answer, result.Data.Select(t => t.Embedding).ToArray(), string.Empty);
        }
        else
            return (ResultType.Error, null, content);
    }
}