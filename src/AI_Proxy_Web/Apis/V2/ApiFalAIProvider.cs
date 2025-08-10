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

[ApiProvider("FalAI")]
public class ApiFalAIProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiFalAIProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
    }

    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        extraOptionsList = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new[]
                {
                    new KeyValuePair<string, string>("方形", "1:1"),
                    new KeyValuePair<string, string>("横屏", "4:3"),
                    new KeyValuePair<string, string>("竖屏", "3:4"),
                    new KeyValuePair<string, string>("宽横屏", "16:9"),
                    new KeyValuePair<string, string>("长竖屏", "9:16")
                }
            }
        };
    }
    
    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        var url = _host + _modelName;
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Key " + _key);
        var msg = JsonConvert.SerializeObject(new
        {
            prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
            aspect_ratio = GetExtraOptions(input.External_UserId)[0].CurrentValue
        });
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["images"] != null)
        {
            var image_url = json["images"][0]["url"].Value<string>();
            var image_type = json["images"][0]["content_type"].Value<string>();
            var bytes = await client.GetByteArrayAsync(image_url);
            yield return FileResult.Answer(bytes, image_type.Contains("jpeg")?"jpg":"png", ResultType.ImageBytes);
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
}
