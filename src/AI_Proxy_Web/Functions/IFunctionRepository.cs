using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Models;

namespace AI_Proxy_Web.Functions;

public interface IFunctionRepository
{
    List<Function> GetFunctionList(string[]? functionNames);

    string[] GetFunctionNamesByScene(ChatContexts chatContexts, string groupName="Internal");

    IAsyncEnumerable<Result> ProcessChatFunctionCalls(List<FunctionCall> functionCalls,
        ApiChatInputIntern input, bool reEnter = false);

}