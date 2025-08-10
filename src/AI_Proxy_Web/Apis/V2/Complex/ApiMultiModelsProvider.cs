using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2.Extra;

[ApiProvider("MultiModels")]
public class ApiMultiModelsProvider : ApiProviderBase
{
    protected IApiFactory _apiFactory;
    public ApiMultiModelsProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IApiFactory apiFactory):base(configHelper,serviceProvider)
    {
        _apiFactory = apiFactory;
    }

    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        var models = _modelName.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var names = new List<string>();
        foreach (var model in models)
        {
            var t = DI.GetApiClassAttribute(int.Parse(model));
            names.Add(t.DisplayName);
        }
        extraOptionsList = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "群聊模型", Contents = new []
                {
                    new KeyValuePair<string, string>(string.Join(", ", names), string.Join(",", models))
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
    }
    
    public override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
        input.IgnoreAutoContexts = true;
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
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
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
            if(ApiBase.CheckStopSigns(input))
                break;
            
            var model = models[i]; //当前使用哪个模型
            var api = _apiFactory.GetApiCommon(model);
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
                        content.AppendLine($"<speak>\n<speaker>\n{ChatModel.GetModel(models[(j - 1) % models.Length])?.DisplayName}\n</speaker>\n<content>\n{chats[j]}\n</content>\n</speak>");
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
            yield return Result.Answer("**" + ChatModel.GetModel(model)?.DisplayName + ":**\n");
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
