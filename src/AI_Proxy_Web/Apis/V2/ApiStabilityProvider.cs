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

[ApiProvider("Stability")]
public class ApiStabilityProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiStabilityProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
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
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("方形", "1:1"),
                    new KeyValuePair<string, string>("横屏", "3:2"),
                    new KeyValuePair<string, string>("竖屏", "2:3"),
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
        var url = _host + "v2beta/stable-image/generate/" + _modelName;
        HttpClient client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _key);
        client.DefaultRequestHeaders.Add("accept","image/*");
        var boundary = DateTime.Now.Ticks.ToString("X");
        var content = new MultipartFormDataContent(boundary);
        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + boundary);
        var cnt = new StringContent("png");
        content.Add(cnt, "output_format");
        cnt.Headers.Remove("Content-Disposition");
        cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"output_format\";");
        cnt = new StringContent(input.ChatContexts.Contexts.Last().QC.Last().Content);
        content.Add(cnt, "prompt");
        cnt.Headers.Remove("Content-Disposition");
        cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"prompt\";");
        cnt = new StringContent(GetExtraOptions(input.External_UserId)[0].CurrentValue);
        content.Add(cnt, "aspect_ratio");
        cnt.Headers.Remove("Content-Disposition");
        cnt.Headers.TryAddWithoutValidation("Content-Disposition", $"form-data; name=\"aspect_ratio\";");

        var resp = await client.PostAsync(url, content);
        if (resp.IsSuccessStatusCode)
        {
            var response = await resp.Content.ReadAsByteArrayAsync();
            yield return FileResult.Answer(response, "png", ResultType.ImageBytes);
        }
        else
        {
            var response = await resp.Content.ReadAsStringAsync();
            yield return Result.Error(response);
        }
    }

    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }
}
