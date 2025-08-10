using System.Net;
using System.Security.Cryptography;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("ZhiPu")]
public class ApiZhiPuProvider : ApiOpenAIProvider
{
    public ApiZhiPuProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IFunctionRepository functionRepository, IHttpClientFactory httpClientFactory) : base(configHelper, serviceProvider, functionRepository, httpClientFactory)
    {
    }
    
    private string appId = string.Empty;
    private string secret = string.Empty;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        appId = configHelper.GetProviderConfig<string>(attr.Provider, "APPID");
        secret = configHelper.GetProviderConfig<string>(attr.Provider, "Secret");
    }

    #region GetJwtToken
    
    private string GetJwtToken()
    {
        var header = "{\"alg\":\"HS256\",\"sign_type\":\"SIGN\"}";
        var payload = JsonConvert.SerializeObject(new
        {
            api_key = appId, exp = GetMillSeconds(DateTime.Now.AddMinutes(10)), timestamp = GetMillSeconds(DateTime.Now)
        });
        var bHeader = Base64UrlEncode(header);
        var bPayload = Base64UrlEncode(payload);
        var t = Base64UrlEncode(HmacSha256($"{bHeader}.{bPayload}", secret));
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

    #endregion

    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        _key = GetJwtToken();
        await foreach (var res in base.SendMessageStream(input))
        {
            yield return res;
        }
    }

    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        _key = GetJwtToken();
        return await base.SendMessage(input);
    }
}