using System.Text;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions;

public class RestRepository: IRestRepository
{
    private IHttpClientFactory _httpClientFactory;

    public RestRepository(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }
    
    public (bool success, string result) DoGet(string url, string userToken)
    {
        var client = _httpClientFactory.CreateClient();
        if(!string.IsNullOrEmpty(userToken))
            client.DefaultRequestHeaders.Add("x-access-token", userToken);
        try
        {
            return (true, client.GetStringAsync(url).Result);
        }
        catch (Exception ex)
        {
            return (false, ex.ToString());
        }
    }
    
    public (bool success, string result) DoPost(string url, string body, string userToken)
    {
        var client = _httpClientFactory.CreateClient();
        if(!string.IsNullOrEmpty(userToken))
            client.DefaultRequestHeaders.Add("x-access-token", userToken);
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            return (true, client.PostAsync(url, content).Result.Content.ReadAsStringAsync().Result);
        }
        catch (Exception ex)
        {
            return (false, ex.ToString());
        }
    }
}