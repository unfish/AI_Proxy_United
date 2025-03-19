using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.Perplexity, "Perplexity", "Perplexity搜索问答模型，通过自动的互联网搜索并生成深度回复来回答复杂问题。", 189, type: ApiClassTypeEnum.搜索模型, priceIn: 8, priceOut: 8)]
public class ApiPerplexity:ApiBase
{
    protected PerplexityClient _client;
    public ApiPerplexity(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<PerplexityClient>();
    }
    
    /// <summary>
    /// 
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
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var resp = await _client.SendMessage(input);
        return resp;
    }
}

[ApiClass(M.PerplexityPro, "Perplexity Pro", "Perplexity Pro深度搜索问答模型，通过自动的互联网搜索并生成深度回复来回答复杂问题。比普通搜索深度更深，回复更复杂，价格高15倍。", 190, type: ApiClassTypeEnum.搜索模型,
    priceIn: 23, priceOut: 120)]
public class ApiPerplexityPro : ApiPerplexity
{
    public ApiPerplexityPro(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client.SetModel("sonar-pro");
    }
}

[ApiClass(M.PerplexityReasoning, "Perplexity推理", "Perplexity推理问答模型，通过自动的互联网搜索并生成深度回复来回答复杂问题。比普通搜索深度更深，并通过推理逻辑来回答问题，价格高8倍。", 191, type: ApiClassTypeEnum.搜索模型,
    priceIn: 15, priceOut: 60)]
public class ApiPerplexityReasoning : ApiPerplexity
{
    public ApiPerplexityReasoning(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client.SetModel("sonar-reasoning-pro");
    }
}

/// <summary>
/// Perplexity 大模型接口
/// 文档地址 https://docs.perplexity.ai/api-reference/chat-completions
/// </summary>
public class PerplexityClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public PerplexityClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        APIKEY = configHelper.GetConfig<string>("Service:Perplexity:Key");
        hostUrl = configHelper.GetConfig<string>("Service:Perplexity:Host");
    }
    
    private static String hostUrl;
    private String APIKEY;//从开放平台控制台中获取
    private string modelName = "sonar";

    public void SetModel(string name)
    {
        modelName = name;
    }
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        var msgs = GetBasicMessages(input.ChatContexts);
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            model = modelName,
            messages = msgs,
            temperature = input.Temprature,
            stream,
            max_tokens =  4096,
            user = input.External_UserId
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
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var url = hostUrl + "chat/completions";
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
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var url = hostUrl + "chat/completions";
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        return await ProcessQueryResponse(resp);
    }
}