using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions.InternalFunctions;

[Processor("SearchAndSummarize")]
public class SearchAndSummarizeProcessor: BaseProcessor
{
    public SearchAndSummarizeProcessor(IApiFactory factory) : base(factory)
    {
    }

    protected override void ProcessParam(ApiChatInputIntern input, string funcArgs)
    {
        var arg = JObject.Parse(funcArgs);
        var q = arg["q"].Value<string>();
        var target = arg["target"].Value<string>();
        input.ChatContexts = ChatContexts.New(q);
        input.ChatContexts.AddQuestion(target);
        input.ChatModel = (int)M.搜索摘要;
    }
}