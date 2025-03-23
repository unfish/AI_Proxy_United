using System.Collections.Concurrent;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.DeepSearch, "深度搜索", "提出一个复杂的信息收集任务，由模型进行任务分解，并自动进行任务编排，调用搜索功能执行多次搜索，并输出一篇完整的结果。", 198, type: ApiClassTypeEnum.搜索模型, priceIn: 0, priceOut: 0.1)]
public class ApiDeepSearch:ApiBase
{
    private IServiceProvider _serviceProvider;
    private DeepSearchClient _client;
    public ApiDeepSearch(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<DeepSearchClient>();
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

public class DeepSearchClient: IApiClient
{
    private IApiFactory _apiFactory;
    public DeepSearchClient(IApiFactory apiFactory)
    {
        _apiFactory = apiFactory;
    }
    private int modelId = (int)M.Claude中杯;
    
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        input.ChatModel = modelId;
        var api = _apiFactory.GetService(modelId);
        bool isFirstChat = input.ChatContexts.Contexts.Count==1;
        if (isFirstChat) //首次进入增加系统指令
        {
            var question = "当你接收到用户的需求，请认真分析用户的目的及深层需求，并列出所有该任务需要用户明确的需求点，例如调研的方向、研究范围、边界、明确的目标市场或目标客户群等等，等用户回答完以后再开始解决问题。\n" +
                           "通过调用搜索摘要功能来获取互联网上的信息，你需要仔细的分解搜索任务，每次只执行单一搜索任务，比如市场占有率和用户评价，合并搜索会极大的影响搜索结果排序。对同一个主题可以分别使用中文和英文进行两次搜索以便获得更高质量的搜索结果。\n" +
                           "每一步操作完成以后如果不需要用户提供更多信息，就自动开始执行下一步，直至整个任务完成。";
            input.ChatContexts.AddQuestion(question, ChatType.System);
        }
        await foreach (var res in api.ProcessChat(input))
        {
            yield return res;
        }
        input.IgnoreAutoContexts = true; //跟内层模型共享同一个input对象，内层模型已经保存过上下文了，外层不需要保存，不然会重复叠加上下文
    }
}
