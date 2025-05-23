using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.GPTImage, "GPT画图", "OpenAI旗下最强大的文本画图工具，可以用中文，可以图生图，不支持多轮对话。", 207, type: ApiClassTypeEnum.画图模型, canProcessImage:true, canProcessMultiImages:true, priceIn: 0, priceOut: 1)]
public class ApiDeerApiImage:ApiBase
{
    private DeerApiImageClient _client;
    public ApiDeerApiImage(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<DeerApiImageClient>();
    }
    
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        if (input.ChatContexts.Contexts.Last().QC.Any(t => t.Type == ChatType.图片Base64))
        {
            await foreach (var resp in _client.SendEditMessage(input))
                yield return resp;
        }
        else
        {
            await foreach (var resp in _client.SendCreateMessage(input))
                yield return resp;
        }
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
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
/// OpenAI画图接口DeerApi代理接口
/// 文档地址https://apidoc.deerapi.com/api-276386063
/// </summary>
public class DeerApiImageClient: IApiClient
{
    private IHttpClientFactory _httpClientFactory;
    public DeerApiImageClient(IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        
        APIKEY = configHelper.GetConfig<string>("Service:DeerApi:Key");
        createUrl = "https://api.deerapi.com/v1/images/generations";
        editUrl = "https://api.deerapi.com/v1/images/edits";
    }
    private String createUrl;
    private String editUrl;
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
                    new KeyValuePair<string, string>("横屏", "1536x1024"),
                    new KeyValuePair<string, string>("竖屏", "1024x1536"),
                    new KeyValuePair<string, string>("自动", "auto")
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
            model = "gpt-image-1",
            n = 1,
            size = input.ImageSize
        });
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendCreateMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        client.Timeout = TimeSpan.FromSeconds(300);
        var url = createUrl;
        var msg = GetMsgBody(input);
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var res = JObject.Parse(content);
        if (res["data"] != null)
        {
            yield return FileResult.Answer(Convert.FromBase64String(res["data"][0]["b64_json"].Value<string>()),"png", ResultType.ImageBytes);
        }
        else
        {
            yield return Result.Error(content);
        }
    }
    
    public async IAsyncEnumerable<Result> SendEditMessage(ApiChatInputIntern input)
    {
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+APIKEY);
        client.Timeout = TimeSpan.FromSeconds(300);
        var url = editUrl;
        var qc = input.ChatContexts.Contexts.Last().QC;
        var content = new MultipartFormDataContent
        {
            { new StringContent("gpt-image-1"), "model" },
            { new StringContent(qc.LastOrDefault(x=>x.Type== ChatType.文本)?.Content??""), "prompt" },
            { new StringContent("1"), "n" },
            { new StringContent(input.ImageSize), "size" }
        };
        foreach (var q in qc)
        {
            if (q.Type == ChatType.图片Base64)
            {
                content.Add(new ByteArrayContent(Convert.FromBase64String(q.Content)), "image", q.FileName);
            }
        }
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        });
        var respContent = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
        {
            var json = JObject.Parse(respContent);
            if (json["data"] != null)
            {
                yield return FileResult.Answer(Convert.FromBase64String(json["data"][0]["b64_json"].Value<string>()),"png", ResultType.ImageBytes);
            }
            else
            {
                yield return Result.Error(respContent);
            }
        }
        else
            yield return Result.Error(respContent);
    }

}