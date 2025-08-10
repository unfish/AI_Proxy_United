using System.Collections.Concurrent;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;

namespace AI_Proxy_Web.Feishu;

public interface IBookFeishuService: IFeishuService
{
}

/// <summary>
/// 注意，该类自己处理上下文保存，自己Flush消息
/// </summary>
public class BookFeishuService : FeishuService, IBookFeishuService
{
    public BookFeishuService(IFeishuRestClient restClient, ILogRepository logRepository,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IApiFactory apiFactory, ConfigHelper configHelper) :
        base(restClient, logRepository, serviceProvider, httpClientFactory, apiFactory, configHelper)
    {
        AppId = configHelper.GetConfig<string>("FeiShu:Book:AppId");
        AppSecret = configHelper.GetConfig<string>("FeiShu:Book:AppSecret");
        TokenCacheKey = "FeiShuApiToken_BookGpt";
        contextCachePrefix = "book";
    }

    public override int GetUserDefaultModel(string user_id)
    {
        return DI.GetModelIdByName("ReadBook");
    }

    public override string GetControllerPath()
    {
        return "bookfeishu";
    }
}
