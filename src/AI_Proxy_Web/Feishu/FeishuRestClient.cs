using RestSharp;

namespace AI_Proxy_Web.Feishu;

public interface IFeishuRestClient
{
    RestClient GetClient();
}

/// <summary>
/// 新版的RestClient建议使用Singleton模式
/// </summary>
public class FeishuRestClient:IFeishuRestClient
{
    private const string DomainUrl = "https://open.feishu.cn/";
    private RestClient _client;

    public FeishuRestClient()
    {
        _client = new RestClient(DomainUrl);
    }

    public RestClient GetClient()
    {
        return _client;
    }
}