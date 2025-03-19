using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions;

public interface IRestRepository
{
    (bool success, string result) DoGet(string url, string userToken);
    (bool success, string result) DoPost(string url, string body, string userToken);
}