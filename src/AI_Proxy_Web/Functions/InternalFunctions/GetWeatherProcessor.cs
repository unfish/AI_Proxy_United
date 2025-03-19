using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.External;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions.InternalFunctions;

[Processor("GetWeather")]
public class GetWeatherProcessor: BaseProcessor
{
    private IServiceProvider _serviceProvider;
    public GetWeatherProcessor(IApiFactory factory, IServiceProvider serviceProvider) : base(factory)
    {
        _serviceProvider = serviceProvider;
    }

    private string _funcArgs;

    protected override void ProcessParam(ApiChatInputIntern input, string funcArgs)
    {
        _funcArgs = funcArgs;
    }

    protected override async IAsyncEnumerable<Result> DoProcessResult(FunctionCall func, ApiChatInputIntern input, bool reEnter = false)
    {
        var w = _serviceProvider.GetRequiredService<IWeatherApi>();
        var o = JObject.Parse(_funcArgs);
        var res = "";
        bool success = false;
        try
        {
            res = JsonConvert.SerializeObject(w.GetWeather(o["city"].Value<string>()));
            success = true;
        }
        catch (Exception ex)
        {
            res = ex.Message;
        }

        if (success)
            yield return Result.New(ResultType.FunctionResult, res);
        else
            yield return Result.Error(res);
    }
}