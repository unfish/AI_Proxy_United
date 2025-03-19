using AI_Proxy_Web.Functions.InternalFunctions;
using AI_Proxy_Web.Helpers;

namespace AI_Proxy_Web.Apis.Base;

public interface IApiFactory
{
    ApiBase GetService(int chatModel);
    ApiBase GetService(M chatModel);

    BaseProcessor GetFuncProcessor(string funcName);
}

public class ApiFactory:IApiFactory
{
    private IServiceProvider _serviceProvider;
    public ApiFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ApiBase GetService(int chatModel)
    {
        return DI.GetApiClass(chatModel, _serviceProvider);
    }
    public ApiBase GetService(M chatModel)
    {
        return GetService((int)chatModel);
    }
    public BaseProcessor GetFuncProcessor(string funcName)
    {
        return DI.GetFuncProcessor(funcName, _serviceProvider);
    }
}