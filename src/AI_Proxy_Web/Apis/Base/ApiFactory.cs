using AI_Proxy_Web.Apis.V2;
using AI_Proxy_Web.Functions.InternalFunctions;
using AI_Proxy_Web.Helpers;

namespace AI_Proxy_Web.Apis.Base;

public interface IApiFactory
{
    BaseProcessor GetFuncProcessor(string funcName);

    ApiCommon GetApiCommon(int chatModel);
    ApiCommon GetApiCommon(string modelName);
}

public class ApiFactory:IApiFactory
{
    private IServiceProvider _serviceProvider;
    public ApiFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public BaseProcessor GetFuncProcessor(string funcName)
    {
        return DI.GetFuncProcessor(funcName, _serviceProvider);
    }
    
    public ApiCommon GetApiCommon(int chatModel)
    {
        return ActivatorUtilities.CreateInstance<ApiCommon>(_serviceProvider, chatModel);
    }
    
    public ApiCommon GetApiCommon(string modelName)
    {
        return ActivatorUtilities.CreateInstance<ApiCommon>(_serviceProvider, DI.GetModelIdByName(modelName));
    }
}