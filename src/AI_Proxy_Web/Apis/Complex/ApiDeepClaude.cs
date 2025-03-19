using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.DeepClaude, "DeepClaude", "对于写代码的需求，通过DeepSeek R1进行需求分析，产出项目结构与详细设计，然后使用Claude 3.7来写代码，质量比单独使用一个模型要高很多。\n同R1的需求讨论的过程可以进行多轮对话，确认所有产出满足需求以后点击写代码才会自动切换到Claude来写代码，之后也可以进行多轮对话来修改代码。", 123, type: ApiClassTypeEnum.推理模型)]
public class ApiDeepClaude:ApiBase
{
    protected DeepClaudeClient _client;
    public ApiDeepClaude(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<DeepClaudeClient>();
    }
    
    public override void StartNewContext(string ownerId)
    {
        _client.StartNewContext(ownerId);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        await foreach (var resp in _client.SendMessageStream(input))
        {
            yield return resp;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("该模型不支持Query调用");
    }
    
}

/// <summary>
/// 双大模型自动讨论接口
/// </summary>
public class DeepClaudeClient: IApiClient
{
    private IApiFactory _apiFactory;
    public DeepClaudeClient(IApiFactory factory)
    {
        _apiFactory = factory;
    }
    
    private string chatsCacheKey = "{0}_deepclaude_chats";
    public void StartNewContext(string ext_userId)
    {
        CacheService.Delete(string.Format(chatsCacheKey, ext_userId));
    }
    
    private int CurrentModel(string ext_userId)
    {
        var index = CacheService.Get<string>(string.Format(chatsCacheKey, ext_userId));
        if (string.IsNullOrEmpty(index))
        {
            return 0;
        }
        return int.Parse(index);
    }
    
    private void SaveCurrentModel(string ext_userId, int modelIndex)
    {
        CacheService.Save(string.Format(chatsCacheKey, ext_userId), modelIndex.ToString(), DateTime.Now.AddDays(7));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        var models = new[] { 114, 18 }; //DeepSeek R1 + Claude 3.7
        var index = CurrentModel(input.External_UserId);
        if (input.ChatContexts.Contexts.Count == 1)
        {
            input.ChatContexts.Contexts.Last().QC.Last().Content = "请分析以下客户提出的项目与功能需求，理解客户的真实目的，拆解成完成该功能实际所需的项目逻辑细节描述与项目结构设计，在用户确认所有的设计已完成并符合需求之前，先不要写任何代码：\n" + input.ChatContexts.Contexts.Last().QC.Last().Content;
        }
        else
        {
            if (input.ChatContexts.Contexts.Last().QC.Last().Content == "写代码")
            {
                index = 1;
                input.ChatContexts.Contexts.Last().QC.Last().Content =
                    "现在，请根据以上详细分析，输出项目所需要的完整代码，如果涉及到数据处理或复杂算法，请给出完整代码，不要省略让用户自行处理。";
            }
        }

        var model = models[index];
        var api = _apiFactory.GetService(model);
        input.ChatModel = model;
        await foreach (var res in api.ProcessChat(input))
        {
            yield return res;
        }

        SaveCurrentModel(input.External_UserId, index);
        if(index == 0)
            yield return FollowUpResult.Answer(new string[] { "写代码" });
    }
}