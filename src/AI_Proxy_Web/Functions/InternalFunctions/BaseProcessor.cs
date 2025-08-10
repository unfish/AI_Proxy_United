using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions.InternalFunctions;

public class BaseProcessor
{
    protected IApiFactory _apiFactory;
    protected bool ClearUserQuestions = true; //默认为true，如果当前function需要多轮交互，需要在后续的对话中设为false来接收用户的输入
    public BaseProcessor(IApiFactory factory)
    {
        _apiFactory = factory;
    }
    
    public async IAsyncEnumerable<Result> ProcessResult(FunctionCall func,
        ApiChatInputIntern input, ApiChatInputIntern callerInput, bool reEnter = false)
    {
        if (ClearUserQuestions)
            input.QuestionContents = new();
        ProcessParam(input, func.Arguments);
        await foreach (var res in DoProcessResult(func, input, callerInput, reEnter))
        {
            yield return res;
        }
    }
    
    protected virtual void ProcessParam(ApiChatInputIntern input, string funcArgs)
    {
    }

    /// <summary>
    /// 默认处理逻辑，调用指定的子模型来解决问题，如果该方法只需要调用某个子模型，重写ProcessParam设置正确的input参数就可以，特殊方法单独重写该方法。
    /// 需要把结果重新提交给大模型来组织回复的，需要返回FunctionResult类型，否则会将函数的返回结果直接返回给用户。
    /// </summary>
    /// <param name="func"></param>
    /// <param name="input"></param>
    /// <param name="callerInput"></param>
    /// <param name="reEnter"></param>
    /// <returns></returns>
    protected virtual async IAsyncEnumerable<Result> DoProcessResult(FunctionCall func,
        ApiChatInputIntern input, ApiChatInputIntern callerInput, bool reEnter = false)
    {
        var api = _apiFactory.GetApiCommon(input.ChatModel);
        await foreach (var res in api.ProcessChat(input))
        {
            yield return res;
        }
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class ProcessorAttribute : Attribute
{
    public string Name { get; set; }
    public bool MultiTurn { get; set; } //该函数执行过程中是否可以跟用户多轮对话

    public ProcessorAttribute(string name, bool multiTurn = false)
    {
        Name = name;
        MultiTurn = multiTurn;
    }
}