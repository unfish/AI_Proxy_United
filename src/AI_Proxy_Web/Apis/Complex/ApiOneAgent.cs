using System.Collections.Concurrent;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.OneAgent, "万能助理", "提出一个复杂任务，由模型进行任务分解，并自动进行任务编排，进行多模型调度，合作共同完成复杂任务，比如市场调研+写方案，客户需求分析+方案+代码，调用浏览器查询资料并生成文件导出数据等等。更多功能有待挖掘。", 197, type: ApiClassTypeEnum.辅助模型, canUseFunction:true, priceIn: 0, priceOut: 0.1)]
public class ApiOneAgent:ApiBase
{
    private IServiceProvider _serviceProvider;
    private OneAgentClient _client;
    public ApiOneAgent(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<OneAgentClient>();
        _serviceProvider = serviceProvider;
    }

    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach(var res in _client.SendMessageStream(input))
            yield return res;
    }

    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("该模型不支持Query调用");
    }

    protected override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
    }
}

public class OneAgentClient: IApiClient
{
    private IApiFactory _apiFactory;
    public OneAgentClient(IApiFactory apiFactory)
    {
        _apiFactory = apiFactory;
    }
    private int modelId = (int)M.Claude中杯;
    
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        var api = _apiFactory.GetService(modelId);
        bool isFirstChat = input.ChatContexts.Contexts.Count==1;
        if (isFirstChat) //首次进入增加系统指令
        {
            var question = "当你接收到用户的需求，请认真分析用户的目的及深层需求，并列出所有该任务需要用户明确的需求点，例如调研的方向、研究范围、边界、明确的目标市场或目标客户群等等，等用户回答完以后再开始解决问题。先向用户展示你准备采取的工作步骤，然后自动开始执行。\n" +
                           "执行时请调用万能助理来实际完成任务，通过拆解任务并编排多个助理来高质量的完成该任务。对每一个助理生成的任务指令需要尽量详细描述、逻辑清晰。\n" +
                           "每个助理任务完成之后你需要根据所有已知结果重新审视工作流程并合理的调整后续任务，必要时可以多次重复调用同一个助手，但需要分配不同的角色名称给它，以便后续任务能够正确的获取对应角色的任务执行结果作为自己的输入。\n" +
                           "当完成拆解任务后，调用万能助理的操作助手功能将任务步骤写入todo.md文件。然后按步骤执行，每一步分别调用合适的助理来进行，比如调用信息搜集助手搜索和收集互联网信息，调用方案设计助手完成客户需要的新方案的编写等等。在每一步助理完成并返回结果以后，都要调用一次操作助手将上一步的结果更新到todo.md文件里对应的位置。所有任务完成以后，再调用操作助手助理将todo.md文件发给用户。";
            input.ChatContexts.AddQuestion(question, ChatType.System);
        }
        await foreach (var res in api.ProcessChat(input))
        {
            yield return res;
        }
        input.IgnoreAutoContexts = true; //跟内层模型共享同一个input对象，内层模型已经保存过上下文了，外层不需要保存，不然会重复叠加上下文
    }
}
