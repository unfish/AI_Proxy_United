using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2.Extra;

[ApiProvider("JinaReader")]
public class ApiJinaReaderProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiJinaReaderProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
    }

    private bool removeUrlProtocal;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        removeUrlProtocal = configHelper.GetProviderConfig<bool>(attr.Provider, "RemoveUrlProtocal");//如果使用Nginx代理中转，可能需要去掉Url前面的协议头，不然会造成转义错误(需要nginx中转规则里做补偿）
    }
    
    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        yield return await GetReaderContent(input);
    }

    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        return await GetReaderContent(input);
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
        var url = _host + (removeUrlProtocal ? source.Replace("http://", "").Replace("https://", "") : source);
        try
        {
            var content = await client.GetStringAsync(url);

            var art = new JinaArticle()
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
    
    public override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
    }
    
}

public class JinaArticle
{
    public string Title { get; set; }
    public string Url { get; set; }
    public string Content { get; set; }
}