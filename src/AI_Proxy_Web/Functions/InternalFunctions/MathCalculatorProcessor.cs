using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.External;
using AI_Proxy_Web.Models;
using DynamicExpresso;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions.InternalFunctions;

[Processor("MathCalculator")]
public class MathCalculatorProcessor: BaseProcessor
{
    private IServiceProvider _serviceProvider;
    
    public MathCalculatorProcessor(IApiFactory factory, IServiceProvider serviceProvider) : base(factory)
    {
        _serviceProvider = serviceProvider;
    }

    private string _funcArgs;

    protected override void ProcessParam(ApiChatInputIntern input, string funcArgs)
    {
        _funcArgs = funcArgs;
    }

    protected override async IAsyncEnumerable<Result> DoProcessResult(FunctionCall func, ApiChatInputIntern input, ApiChatInputIntern callerInput, bool reEnter = false)
    {
        var o = JObject.Parse(_funcArgs);
        var formula = o["formula"].ToString();
        var res = "";
        var success = false;
        try
        {
            var interpreter = new Interpreter();
            res = interpreter.Eval(formula).ToString();
            success  = true;
        }
        catch (Exception ex)
        {
            res = $"计算代码执行错误，公式：'{formula}' 错误：{ex.Message}";
        }

        if (success)
            yield return Result.New(ResultType.FunctionResult, res);
        else
            yield return Result.Error(res);
    }
}