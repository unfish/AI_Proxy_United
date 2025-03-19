using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.模型群聊, "模型群聊", "起一个题目，由多个模型自动对话进行深入讨论或辩论，挖掘出更丰富深层的信息。\n你可以提出一个专业的问题，也可以提出两个相对的观点用来辩论。\n请仔细思考你输入的问题，作为提问一方的AI的身份来问这个问题，并将提问方需要完成的任务也加进去。\n每一轮对话结束之后会显示提问一方准备要继续追问的问题，你可以点击使用该问题，也可以忽略它直接输入你想要继续追问的问题。\n要开启新问题的时候不要忘记点开始新会话。", 291, type: ApiClassTypeEnum.辅助模型)]
public class ApiMultiModels:ApiBase
{
    protected MultiModelsClient _client;
    public ApiMultiModels(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<MultiModelsClient>();
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
        input.IgnoreAutoContexts = true;
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
    
    public override List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return _client.GetExtraOptions(ext_userId);
    }

    public override void SetExtraOptions(string ext_userId, string type, string value)
    {
        _client.SetExtraOptions(ext_userId, type, value);
    }
}

/// <summary>
/// 多个大模型自动讨论接口
/// </summary>
public class MultiModelsClient: IApiClient
{
    private IApiFactory _apiFactory;
    public MultiModelsClient(IApiFactory factory)
    {
        _apiFactory = factory;
    }
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "群聊模型", Contents = new []
                {
                    new KeyValuePair<string, string>("GPT, Claude, Gemini, MiniMax", "1,18,12,4"),
                    new KeyValuePair<string, string>("豆包, 通义, 文心, 混元", "16,3,5,10"),
                }
            },
            new ExtraOption()
            {
                Type = "讨论内容", Contents = new []
                {
                    new KeyValuePair<string, string>("深入学习", "0"),
                    new KeyValuePair<string, string>("观点辩论", "1"),
                    new KeyValuePair<string, string>("深度思考", "2")
                }
            }
        };
        foreach (var option in list)
        {
            var cacheKey = $"{ext_userId}_{this.GetType().Name}_{option.Type}";
            var v = CacheService.Get<string>(cacheKey);
            option.CurrentValue = string.IsNullOrEmpty(v) ? option.Contents.First().Value : v;
        }
        return list;
    }
    public void SetExtraOptions(string ext_userId, string type, string value)
    {
        var cacheKey = $"{ext_userId}_{this.GetType().Name}_{type}";
        CacheService.Save(cacheKey, value, DateTime.Now.AddDays(30));
    }
    
    private string chatsCacheKey = "{0}_multimodel_chats";
    public void StartNewContext(string ext_userId)
    {
        CacheService.Delete(string.Format(chatsCacheKey, ext_userId));
    }
    
    private List<string> CurrentChatsList(string ext_userId)
    {
        var chats = CacheService.BGet<List<string>>(string.Format(chatsCacheKey, ext_userId));
        if (chats==null)
        {
            chats = new List<string>();
        }
        return chats;
    }
    
    private void SaveChatsList(string ext_userId, List<string> chats)
    {
        CacheService.BSave(string.Format(chatsCacheKey, ext_userId), chats, DateTime.Now.AddDays(7));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        var options = GetExtraOptions(input.External_UserId);
        var vsModels = options[0].CurrentValue;
        var models = vsModels.Split(',').Select(t => int.Parse(t)).ToArray();
        var chats = CurrentChatsList(input.External_UserId);
        var question = input.ChatContexts.Contexts.Last().QC.Last().Content;
        var discuss = options[1].CurrentValue;
        if (chats.Count == 0)
        {
            var sysPrompt =
                "你正在参与一场多人的讨论，大家通过提问、解答、辩论，最终目的是透彻深入的理解并解答某个问题。";
            switch (discuss)
            {
                case "1":
                    sysPrompt =
                        "你是一位资深的教授以及专业的辩手，拥有各专业领域丰富的知识和深入的理解。针对下面的题目，你需要和另外几位一起，对这个问题通过辩论的形式进行深入的讨论分析，通过严密的逻辑推理和论证来证明自己的观点，并质疑他人的观点，直到他人认同自己的观点为止。";
                    break;
                case "2":
                    sysPrompt =
                        "你是一位行业专家，现在同另外几位进行深入的探讨探寻问题的真相和现实的本质，通过对他人的回复内容进行仔细的思考并验证其正确性，找出其中的错误、漏洞、悖论、或观点的薄弱之处，说出自己的观点，并通过引导大家一步一步反思和重新得出结论，来达到对问题最接近正确的结果。";
                    break;
            }

            sysPrompt += "\n以下是大家需要讨论的题目：\n" + question;
            chats.Add(sysPrompt);
        }
        else
        {
            //用户可以加入自己的内容作为某一方的对话内容
            if (question != "Continue" && question != "继续" && question != "总结")
                chats.Add(question);
        }

        for (var i = 0; i < models.Length; i++) //每一轮让所有AI发言一遍
        {
            var model = models[i]; //当前使用哪个模型
            var api = _apiFactory.GetService(model);
            var inputF = JsonConvert.DeserializeObject<ApiChatInputIntern>(JsonConvert.SerializeObject(input)); //深度复制
            inputF.ChatModel = model;
            var contexts = ChatContexts.New();
            if (chats.Count == 1) //首轮对话
            {
                var ctx = ChatContext.New(chats[0]+"\n现在请你首先发言。请直接给出你的发言内容，不要包含任何额外的说明或角色扮演前缀。发言尽量精练，直达问题的本质。");
                contexts.Contexts.Add(ctx);
            }
            else
            {
                var content = new StringBuilder(chats[0] + "\n以下是其他人的发言内容：\n<speaks>\n");
                for (var j = 1; j < chats.Count; j++)
                {
                    if ((j - 1) % models.Length == i)
                    { 
                        content.Append("</speaks>\n现在轮到你发言了。请根据以上对话历史和讨论主题，思考并给出一个有意义的回复。请直接给出你的发言内容，不要包含任何额外的说明或角色扮演前缀。发言尽量详略得当，直达问题的本质，关键观点可以详细展开论述。");
                        contexts.Contexts.Add(
                            ChatContext.New(
                                new List<ChatContext.ChatContextContent>()
                                    { ChatContext.NewContent(content.ToString()) },
                                new List<ChatContext.ChatContextContent>()
                                    { ChatContext.NewContent(chats[j]) }));
                        content.Clear();
                        content.AppendLine("以下是其他人的发言内容：\n<speaks>");
                    }
                    else
                    {
                        content.AppendLine($"<speak>\n<speaker>\n{ChatModel.GetModel(models[(j - 1) % models.Length])?.Name}\n</speaker>\n<content>\n{chats[j]}\n</content>\n</speak>");
                    }
                }

                if (content.ToString().EndsWith("<speaks>\n"))
                {
                    content.AppendLine("没有其它对话内容了。");
                }
                content.AppendLine("</speaks>");
                if (question == "总结")
                {
                    content.Append("现在讨论结束。请根据以上对话历史和讨论内容，写出针对这个题目的一份详细完整的总结文档，融合各方观点，求同存异，形成一份深度报告。");
                }
                else
                {
                    content.Append("现在轮到你发言了。请根据以上对话历史和讨论主题，仔细思考并给出一个有意义的回复。请直接给出你的发言内容，不要包含任何额外的说明或角色扮演前缀。发言尽量详略得当，直达问题的本质，关键观点可以详细展开论述。如果别人的发言中包含提问，请先回答问题再陈述自己的观点。");
                }
                contexts.AddQuestion(content.ToString());
            }

            inputF.ChatContexts = contexts;
            var sb = new StringBuilder();
            yield return Result.New(ResultType.AnswerStarted);
            yield return Result.Answer("**" + ChatModel.GetModel(model)?.Name + ":**\n");
            await foreach (var res in api.ProcessChat(inputF))
            {
                yield return res;
                if (res.resultType == ResultType.Answer)
                    sb.Append(res.ToString());
                else if (res.resultType == ResultType.Error)
                {
                    yield return Result.Error("出现异常，讨论结束");
                    break;
                }
            }

            yield return Result.New(ResultType.AnswerFinished);
            if (sb.Length > 0 && question != "总结")
            {
                chats.Add(sb.ToString());
            }
        }

        SaveChatsList(input.External_UserId, chats);
        if (question != "总结")
            yield return FollowUpResult.Answer(new string[] { "继续", "总结" });
    }
}