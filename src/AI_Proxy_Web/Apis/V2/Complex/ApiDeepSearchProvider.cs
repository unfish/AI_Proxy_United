using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2.Extra;

[ApiProvider("DeepSearch")]
public class ApiDeepSearchProvider : ApiProviderBase
{
    protected IApiFactory _apiFactory;
    public ApiDeepSearchProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IApiFactory apiFactory):base(configHelper,serviceProvider)
    {
        _apiFactory = apiFactory;
    }

    /// <summary>
    /// 流式接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        var modelId = int.Parse(_modelName);
        input.ChatModel = modelId;
        var api = _apiFactory.GetApiCommon(modelId);
        bool isFirstChat = input.ChatContexts.Contexts.Count==1;
        if (isFirstChat) //首次进入增加系统指令
        {
            var question = "当你接收到用户的需求，请认真分析用户的目的及深层需求，并列出所有该任务需要用户明确的需求点，例如调研的方向、研究范围、边界、明确的目标市场或目标客户群等等，等用户回答完以后再开始解决问题。\n" +
                           "通过调用搜索摘要功能来获取互联网上的信息，你需要仔细的分解搜索任务，每次只执行单一搜索任务，比如市场占有率和用户评价，合并搜索会极大的影响搜索结果排序。对同一个主题可以分别使用中文和英文进行两次搜索可以获得更高质量的搜索结果。\n" +
                           "注意信息搜索的方法，如果要做行业或产品品类的信息收集，应该先确定行业内的头部参与者或该品类的代表性产品，然后按照每家公司/指定的具体产品的信息进行独立搜索，如果直接搜索行业或品类关键词+创新/差异化/优劣势等，通常是搜索不到有效的内容的，要能够通过各类独立信息整合汇总出需要的结果。\n" +
                           "每一步操作完成以后如果不需要用户提供更多信息，就自动开始执行下一步，直至整个任务完成。";
            input.ChatContexts.AddQuestion(question, ChatType.System);
        }
        await foreach (var res in api.ProcessChat(input))
        {
            yield return res;
        }
        input.IgnoreAutoContexts = true; //跟内层模型共享同一个input对象，内层模型已经保存过上下文了，外层不需要保存，不然会重复叠加上下文
    }
    
    public override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
    }
}
