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

[ApiProvider("Ideogram")]
public class ApiIdeogramProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiIdeogramProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
    }

    private string imageHost = String.Empty;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        _chatUrl = _host + "v1/ideogram-v3/generate";
        imageHost = configHelper.GetProviderConfig<string>(attr.Provider, "Image_Host");
        extraOptionsList = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "风格", Contents = new[]
                {
                    new KeyValuePair<string, string>("自动", "AUTO"),
                    new KeyValuePair<string, string>("通用", "GENERAL"),
                    new KeyValuePair<string, string>("真实", "REALISTIC"),
                    new KeyValuePair<string, string>("设计", "DESIGN")
                }
            },
            new ExtraOption()
            {
                Type = "尺寸", Contents = new[]
                {
                    new KeyValuePair<string, string>("方形", "1x1"),
                    new KeyValuePair<string, string>("横屏", "4x3"),
                    new KeyValuePair<string, string>("竖屏", "3x4"),
                    new KeyValuePair<string, string>("宽横屏", "16x10"),
                    new KeyValuePair<string, string>("长竖屏", "10x16")
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
        var url = _chatUrl;
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Api-Key",_key);
        var options = GetExtraOptions(input.External_UserId);
        var msg = JsonConvert.SerializeObject(new
        {
            prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
            aspect_ratio = options[1].CurrentValue,
            style_type = options[0].CurrentValue
        });
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        });
        var content = await resp.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["data"] != null)
        {
            var arr = json["data"] as JArray;
            foreach (JToken item in arr)
            {
                var image_url = item["url"].Value<string>();
                image_url = image_url.Replace("https://ideogram.ai/", imageHost);
                var bytes = await client.GetByteArrayAsync(image_url);
                yield return FileResult.Answer(bytes, "png",
                    ResultType.ImageBytes);
            }
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
