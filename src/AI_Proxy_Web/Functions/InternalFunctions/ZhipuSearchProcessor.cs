using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions.InternalFunctions;

[Processor("ZhipuSearch")]
public class ZhipuSearchProcessor: BaseProcessor
{
    public ZhipuSearchProcessor(IApiFactory factory) : base(factory)
    {
    }

    protected override void ProcessParam(ApiChatInputIntern input, string funcArgs)
    {
        var arg = JObject.Parse(funcArgs);
        var prompt = arg["q"].Value<string>();
        input.ChatContexts = ChatContexts.New(prompt);
        input.ChatModel = (int)M.智谱搜索;
    }
}