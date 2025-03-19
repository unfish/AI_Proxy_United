using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.月之暗面, "月之暗面", "月之暗面MoonShot是国产第一个200K长上下文大模型，长文总结能力强，不支持图片，支持Function call。", 26, canProcessImage:true, canProcessMultiImages:true, canUseFunction:true, priceIn: 12, priceOut: 12)]
public class ApiMoonShot:ApiBase
{
    private MoonShotClient _client;
    public ApiMoonShot(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<MoonShotClient>();
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



/// <summary>
/// Kimi大模型接口
/// 文档地址 https://platform.moonshot.cn/docs/api/chat
/// </summary>
public class MoonShotClient:OpenAIClientBase, IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    private IFunctionRepository _functionRepository;
    public MoonShotClient(IHttpClientFactory httpClientFactory, IFunctionRepository functionRepository, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        _functionRepository = functionRepository;
        APIKEY = configHelper.GetConfig<string>("Service:MoonShot:Key");
    }
    
    private static String hostUrl = "https://api.moonshot.cn/v1/chat/completions";
    private String APIKEY;//从开放平台控制台中获取
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <param name="stream">是否流式返回</param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input, bool stream)
    {
        bool isImageMsg = IsImageMsg(input.ChatContexts);
        var model = isImageMsg ? "moonshot-v1-8k-vision-preview" : "moonshot-v1-auto";
        var msgs = GetFullMessages(input.ChatContexts);
        var tools = GetToolParamters(input.WithFunctions, _functionRepository, out var funcPrompt);
        if(!string.IsNullOrEmpty(funcPrompt))
            msgs.Insert(0, new TextMessage(){role = "system", content = funcPrompt});
        if (tools == null)
        {
            tools = new List<ToolParamter>()
            {
                new FunctionToolParamter()
                    { type = "builtin_function", function = new Function() { Name = "$web_search" } }
            };
        }
        var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
        return JsonConvert.SerializeObject(new
        {
            model = model,
            messages = msgs,
            temperature = input.Temprature,
            stream,
            tools,
            max_tokens = 4096,
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
        client.DefaultRequestHeaders.Add("Authorization",$"Bearer {APIKEY}");
        var url = hostUrl;
        var msg = GetMsgBody(input, false);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        
        return await ProcessQueryResponse(resp);
    }
    
}