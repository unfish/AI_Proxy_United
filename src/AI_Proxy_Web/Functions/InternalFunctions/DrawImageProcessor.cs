using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions.InternalFunctions;

[Processor("DrawImage")]
public class DrawImageProcessor: BaseProcessor
{
    public DrawImageProcessor(IApiFactory factory) : base(factory)
    {
    }

    protected override void ProcessParam(ApiChatInputIntern input, string funcArgs)
    {
        var arg = JObject.Parse(funcArgs);
        var prompt = arg["prompt"].Value<string>();
        input.ChatContexts = ChatContexts.New(prompt);
        input.ChatModel = DI.GetModelIdByName("PPIOQwenImage");
    }
}