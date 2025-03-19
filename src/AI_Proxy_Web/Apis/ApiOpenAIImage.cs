using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.Dall_E3, "Dall-E3", "OpenAI旗下最强大的文本画图工具，可以用中文，而且会返回翻译并强化的提示英文提示词。", 207, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 1)]
public class ApiOpenAIImage:ApiBase
{
    private OpenAIImageClient _client;
    public ApiOpenAIImage(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<OpenAIImageClient>();
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        await foreach(var resp in _client.SendMessage(input))
            yield return resp;
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.SendMessage(input))
        {
            if(resp.resultType== ResultType.ImageBytes|| resp.resultType== ResultType.Error)
                return resp;
        }

        return Result.Error("No response"); //不应该走到这里
    }

    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        if (string.IsNullOrEmpty(input.ImageSize))
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

/// <summary>
/// OpenAI接口
/// 文档地址 https://ohmygpt-docs.apifox.cn/api-105130827
/// 使用的是https://aigptx.top/网站的接口，可以直接充值使用，没有太多限制
/// 但它的域名也不能直接调用，所以自己建了一层转发代理
/// http://chat-gpt-bot.yesmro.cn:9000/
/// </summary>
public class OpenAIImageClient: IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    public OpenAIImageClient(IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        
        APIKEY = configHelper.GetConfig<string>("Service:OpenAI:Key");
        hostUrl = configHelper.GetConfig<string>("Service:OpenAI:Host") + "v1/images/generations";
    }
    private String hostUrl;
    private String APIKEY;
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1024x1024"),
                    new KeyValuePair<string, string>("横屏", "1792x1024"),
                    new KeyValuePair<string, string>("竖屏", "1024x1792")
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
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input)
    {
        return JsonConvert.SerializeObject(new
        {
            prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
            model = "dall-e-3",
            n = 1,
            quality = "hd",
            response_format = "b64_json",
            size = input.ImageSize,
            user = input.External_UserId
        });
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        var url = hostUrl;
        var msg = GetMsgBody(input);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var res = JObject.Parse(content);
        if (res["data"] != null)
        {
            yield return Result.Answer(res["data"][0]["revised_prompt"].Value<string>());
            yield return FileResult.Answer(Convert.FromBase64String(res["data"][0]["b64_json"].Value<string>()),"png", ResultType.ImageBytes);
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
}