using System.Net;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.模型对战, "模型对战", "起一个题目，由两个模型自动对话进行深入讨论或辩论，挖掘出更丰富深层的信息。\n你可以提出一个专业的问题，也可以提出两个相对的观点用来辩论。如果使用辩论，请在后面加上『我是正方，你是反方』。\n请仔细思考你输入的问题，作为提问一方的AI的身份来问这个问题，并将提问方需要完成的任务也加进去。\n每一轮对话结束之后会显示提问一方准备要继续追问的问题，你可以点击使用该问题，也可以忽略它直接输入你想要继续追问的问题。\n要开启新问题的时候不要忘记点开始新会话。", 290, type: ApiClassTypeEnum.辅助模型)]
public class ApiTwoModels:ApiBase
{
    protected TwoModelsClient _client;
    public ApiTwoModels(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<TwoModelsClient>();
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
/// 双大模型自动讨论接口
/// </summary>
public class TwoModelsClient: IApiClient
{
    private IApiFactory _apiFactory;
    public TwoModelsClient(IApiFactory factory)
    {
        _apiFactory = factory;
    }
    
    public List<ExtraOption> GetExtraOptions(string ext_userId)
    {
        var list = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "对战模型", Contents = new []
                {
                    new KeyValuePair<string, string>("GPT 4o vs Claude 3.7", "1:18"),
                    new KeyValuePair<string, string>("阿里通义 vs 百度文心", "3:5"),
                    new KeyValuePair<string, string>("腾讯混元 vs 阶跃星辰", "10:21"),
                    new KeyValuePair<string, string>("MiniMax vs 字节豆包", "4:16"),
                }
            },
            new ExtraOption()
            {
                Type = "论战方式", Contents = new []
                {
                    new KeyValuePair<string, string>("左答右问", "0"),
                    new KeyValuePair<string, string>("左问右答", "1")
                }
            },
            new ExtraOption()
            {
                Type = "讨论内容", Contents = new []
                {
                    new KeyValuePair<string, string>("深入学习", "0"),
                    new KeyValuePair<string, string>("观点辩论", "1"),
                    new KeyValuePair<string, string>("文章撰写", "2"),
                    new KeyValuePair<string, string>("深度思考", "3"),
                    new KeyValuePair<string, string>("COT思维链", "4"),
                    new KeyValuePair<string, string>("TOT思维树", "5"),
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
    
    private string chatsCacheKey = "{0}_twomodel_chats";
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
        var models = vsModels.Split(':').Select(t => int.Parse(t)).ToArray();
        var direct = options[1].CurrentValue;
        if (direct == "1")
            models = models.Reverse().ToArray();
        var chats = CurrentChatsList(input.External_UserId);
        var question = input.ChatContexts.Contexts.Last().QC.Last().Content;
        var discuss = options[2].CurrentValue;
        if (chats.Count == 0)
        {
            var sysPrompt =
                "你作为一位资深的学习导师，即《指导者》，要通过专业的提问和讨论，同另一个大模型AI即《思考者》深入的理解并解答某个问题。你可以适当的发表自己对问题的看法，但重点应该放在对它的回复提出专业的意见来引导它的深入思考。\n注意：一次不要同时提出太多个问题，对不同的分段内容需要可以分成多次来提问，这样才能更完整深入的讨论关键问题。另外你也可以适当发散，对它的回复中没有讲到的问题进行追问，引导它探讨更广泛的相关主题的内容。\n以下是你提出的需要探讨的问题。";
            switch (discuss)
            {
                case "1":
                    sysPrompt =
                        "你是一位资深的教授以及专业的辩手，拥有各专业领域丰富的知识和深入的理解。现在请你提出一个有丰富的可讨论性的题目，和另一个大模型AI作为正反双方，对这个问题通过辩论的形式进行深入的讨论分析，通过严密的逻辑推理和论证来证明自己的观点，直到对方认同自己的观点为止。\n现在请你提出要讨论的问题。";
                    break;
                case "2":
                    sysPrompt =
                        "你是一位资深的导师，即《指导者》，现在需要指导另一个大模型AI即《思考者》写出一篇行文流畅立意深刻文辞优美的文章，通过对它写出来的文章提出批评和建议，包括长度、深度、风格等要求，并做出一个打分，直到它的文章能够得到A+的评分为止。不同主题的文章需要表现出适当的文辞风格。\n现在请你提出一个文章主题。";
                    question =
                        "主题：" + question + @"

指令：请使用以下步骤来完成这篇文章：
1. 分析问题：仔细阅读主题，理解主题，确定需要完成的文章的内核。
2. 确定核心内容：列出文章的内核思想、准备使用的文风、文章的长度、及文章的大纲。
3. 逐步完成：
   对于每一个章节：
   a. 陈述当前章节的主题
   b. 写出详细的本章节内容
   c. 自己给本章节的内容及写作风格打个分
4. 回顾与检查：
   在进行下一步之前：
   a. 回顾之前的章节，并仔细理解我提出的意见和建议
   b. 检查是否有任何错误或需要修改或完善之处
   c. 如果需要，请执行修正并输出上一步骤的修改后的结果，然后继续执行当前步骤
5. 全部章节完成以后，如果是短文章，复盘整个过程，并完善润色，输出最终的文章的整体。对长的文章，只需要复盘整个过程，给文章整体打个分即可。

现在，请开始回答问题。记住，只完成一个步骤后就停止。";
                    break;
                case "3":
                    sysPrompt =
                        "你是一位行业专家，现在同另一个大模型AI进行深入的探讨探寻问题的真相和现实的本质，通过对它的回复内容进行仔细的思考并验证其正确性，找出其中的错误、漏洞、悖论、或观点的薄弱之处，并通过引导它一步一步反思和重新得出结论，来达到对问题最接近正确的结果。\n现在请你先提出一个问题。";
                    break;
                case "4":
                    sysPrompt =
                        "你是一位资深的导师，作为《指导者》，指导另一个大模型AI即《思考者》使用COT思维链的方式解决一个复杂的问题或任务，指导它按照你的要求对任务进行分解，并分步骤完成。\n你需要对它的每一个步骤的执行过程和产出的结果进行审查，并提出意见和建议以帮助它完善上一个步骤的结果，直到最终完成了整个任务。\n记住：每次先对它上一个步骤的产出进行审查和提出意见，然后说：\"请继续下一个步骤，记住，只完成一个步骤后就停止。\"。\n现在请你先提出一个问题。";
                    question =
                        "问题：" + question + @"

指令：让我们使用COT思维链方法来解决这个问题。请使用以下步骤来解答：
1. 分析问题：仔细阅读问题，理解问题，确定需要解决的关键点。
2. 制定解决方案：列出解决问题所需的具体步骤。
3. 逐步解答：
   对于每一个步骤：
   a. 陈述当前步骤的目标
   b. 提供详细的解答过程
   c. 总结这一步骤的结果
4. 回顾与检查：
   在进行下一步之前：
   a. 回顾之前的步骤，并仔细理解我提出的意见和建议
   b. 检查是否有任何错误或需要修改或完善之处
   c. 如果需要，请执行修正并输出上一步骤的修改后的结果，然后继续执行当前步骤

你的回答应该采用以下格式：
分析问题：
[您的分析]
解决方案步骤：
1. [步骤1]
2. [步骤2]
3. [步骤3]

逐步解答时按照格式：
当前步骤：[步骤编号]
回顾与反思：[回顾之前的步骤，检查是否有错误或需要修正或完善的地方，有必要的话输出修改后的结果]
步骤目标：[简述这一步的目标]
执行：[详细描述这一步的执行过程]
结果：[总结这一步的结果]
下一步：[简要说明下一步的计划，如果问题已解决，请输出""{FINISH}""]

现在，请开始回答问题。记住，只完成一个步骤后就停止。";
                    break;
                case "5":
                    sysPrompt =
                        "你是一位资深的导师，作为《指导者》，指导另一个大模型AI即《思考者》使用TOT思维树的方式解决一个复杂的问题或任务。\n你需要对它的每一个方案的结果进行审查，并提出意见和建议以帮助我完善上一个阶段的结果。\n记住：每次先对它上一个步骤的产出进行审查和提出意见，然后说：\"请继续下，记住，只完成一个步骤后就停止。\"\n现在请你先提出一个问题。";
                    question =
                        "问题：" + question + @"

指令：让我们使用TOT思维树方法来解决这个问题，考虑多个可能的解决方案，并使用以下步骤来解答这个问题：
1. 分析问题：仔细阅读问题，理解问题，确定需要解决的关键点。
2. 制定解决方案：列出解决问题可能的方向和方案的概要，至少三个，最多五个。
3. 逐步解答：
   对于每一个方案：
   a. 讲解该方案的出发点或原理
   b. 提供详细的方案内容，可能包含多个步骤的解答过程
   c. 总结这一方案的优缺点
4. 回顾与检查：
   在进行下一步之前：
   a. 回顾之前的方案，并仔细理解我提出的意见和建议
   b. 检查是否有任何错误或需要修改或完善之处
   c. 如果需要，请执行修正并输出上一方案的修改后的结果，然后继续执行当前步骤
5. 总结并形成最终方案:
   对比之前所有的方案和修改意见，重新思考并形成最终的解决方案，请务必详实清晰的给出最终的结果。

你的回答应该采用以下格式：
分析问题：
[您的分析]
可能的解决方案：
1. [方案1]
2. [方案2]
3. [方案3]
然后是每个方案的详情。

逐步解答时按照格式：
当前步骤：[步骤编号]
回顾与反思：[回顾之前的方案，检查是否有错误或需要修正或完善的地方，有必要的话输出修改后的结果]
执行：[详细描述当前方案的内容]
结果：[总结该方案的优缺点]
下一步：[简要说明下一步的计划，如果已经给出最终方案，请输出""{FINISH}""]

现在，请开始回答问题。记住，只完成一个步骤后就停止。";
                    break;
            }

            chats.Add(sysPrompt);
        }
        else if (chats.Count % 2 == 0 && question != "Continue" && question != "继续")
        {
            chats.RemoveAt(chats.Count - 1); //使用用户输入的问题替代AI自动生成的
        }

        if (question != "Continue" && question != "继续" && question != "总结")
            chats.Add(question);

        for (var i = 0; i < 2; i++) //一答一问
        {
            var model = models[i];
            var api = _apiFactory.GetService(model);
            var inputF = JsonConvert.DeserializeObject<ApiChatInputIntern>(JsonConvert.SerializeObject(input)); //深度复制
            inputF.ChatModel = model;
            var contexts = ChatContexts.New();
            var startIndex = i % 2 == 0 ? 1 : 0;
            for (var index = startIndex; index < chats.Count; index++)
            {
                var ctx = ChatContext.New(chats[index]);
                if (chats.Count > index + 1)
                {
                    ctx.AC.Add(ChatContext.NewContent(chats[index + 1]));
                    index++;
                }
                else if(question == "总结")
                {
                    ctx.QC.Last().Content += "\n\n现在讨论结束。请根据以上对话历史和讨论内容，写出针对这个题目的一份详细完整的总结文档，融合各方观点，求同存异，形成一份深度报告。";
                }

                contexts.Contexts.Add(ctx);
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
                    yield break;
                }
            }

            yield return Result.New(ResultType.AnswerFinished);
            if (sb.Length > 0 && question != "总结")
            {
                if (i == 0)
                {
                    sb.Insert(0, "以下是对方的回复内容：\n\n");
                }
                chats.Add(sb.ToString());
            }
        }

        SaveChatsList(input.External_UserId, chats);
        if(question != "总结")
            yield return FollowUpResult.Answer(new string[] { "继续", "总结" });
    }
}