using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.JinaReader, "Reader AI", "Reader AI是jina.ai提供的免费的通过URL获取网页格式化正文的服务。", 125,  type: ApiClassTypeEnum.辅助模型, priceIn: 0, priceOut: 0)]
public class ApiJinaAi:ApiBase
{
    private JinaAiClient _client;
    public ApiJinaAi(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<JinaAiClient>();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
        var resp = await _client.GetReaderContent(input);
        yield return resp;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
        var resp = await _client.GetReaderContent(input);
        return resp;
    }
}


[ApiClass(M.JinaDeepSearch, "Jina DeepSearch", "Jina AI DeepSearch大模型，自动实现深度搜索与话题整理，形成完整报告。不过它的输出默认是英文的。", 188, type: ApiClassTypeEnum.搜索模型, priceIn: 5, priceOut: 5)]
public class ApiJinaAiDeepSearch:ApiBase
{
    private JinaAiClient _client;
    public ApiJinaAiDeepSearch(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<JinaAiClient>();
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.DeepSearchStream(input))
        {
            yield return resp;
        }
    }
    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("该模型不支持Query请求");
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


/// <summary>
/// 接口
/// 文档地址 https://jina.ai/reader/
/// https://jina.ai/deepsearch
/// </summary>
public class JinaAiClient:OpenAIClientBase,IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private string readerHost;
    private string deepSearchHost;
    private String APIKEY;//从开放平台控制台中获取
    public JinaAiClient(IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        readerHost = configHelper.GetConfig<string>("Service:Jina:Host");
        deepSearchHost = configHelper.GetConfig<string>("Service:Jina:DeepSearch_Host");
        APIKEY = configHelper.GetConfig<string>("Service:Jina:Key");
    }

    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "搜索深度", Contents = new []
                {
                    new KeyValuePair<string, string>("低", "low"),
                    new KeyValuePair<string, string>("中", "medium"),
                    new KeyValuePair<string, string>("高", "high")
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

    
    /// <summary>
    ///
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            model = "jina-deepsearch-v1",
            messages = GetBasicMessages(input.ChatContexts),
            reasoning_effort = GetExtraOptions(input.External_UserId)[0].CurrentValue,
            stream
        }, jSetting);
    }

    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> DeepSearchStream(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var url = deepSearchHost+"v1/chat/completions";
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
    public async Task<Result> GetReaderContent(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        var source = input.ChatContexts.Contexts.Last().QC.Last().Content;
        var url = readerHost+source.Replace("http://","").Replace("https://","");
        try
        {
            var content = await client.GetStringAsync(url);

            var art = new Article()
            {
                Content = content, Title = "", Url = source
            };
            if (content.StartsWith("Title:"))
                art.Title = content.Substring("Title:".Length, content.IndexOf('\n'));
            return JinaArticleResult.Answer(art);
        }
        catch(Exception ex)
        {
            return Result.Error(ex.Message);
        }
    }
    
    public class Article
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Content { get; set; }
    }
}