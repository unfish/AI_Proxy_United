using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions.InternalFunctions;

[Processor("OneAgent", multiTurn:true)]
public class OneAgentProcessor: BaseProcessor
{
    public OneAgentProcessor(IApiFactory factory) : base(factory)
    {
        ClearUserQuestions = false; //默认不清除，后面自己处理
    }
    
    private record ModelInfo
    {
        public string SystemPrompt { get; set; }
        public int Model { get; set; }
    }
    private static Dictionary<string, ModelInfo> skills = new Dictionary<string, ModelInfo>()
    {
        {"信息搜集", new (){SystemPrompt = "你可以使用搜索摘要工具，获取某个主题的相关信息。要注意的是，搜索是通过Google来搜索，所以一次搜索的关键词不能太多，复杂的任务需要拆分成多次搜索来完成。该工具不会返回原始搜索结果，而是返回本次搜索目的所要求的解答信息，所以每次调用该工具时本次搜索的目的及需要它返回的信息的内容、格式等请描述清楚。" +
                                       "对任务有任何不确定的、模糊或缺失的信息需要用户确认的，可以随时向用户提问要求补充信息以提高最后的结果质量。最后你需要针对本次要求解决的问题产出一个完整的汇总结果，一次性输出，并以<finish>true</finish>来结束输出，以便程序知道你的信息搜索过程已经结束了。", Model = (int)M.Claude中杯}},
        {"操作助手", new (){SystemPrompt = "对用户指令有任何不确定的、模糊或缺失的信息需要用户确认的，可以随时向用户提问要求补充信息以提高最后的结果质量。最后你需要针对本次要求解决的问题输出一个完整的汇总结果，一次性输出，并以<finish>true</finish>来结束输出，以便程序知道你的处理过程已经结束了。即使只是让你处理文件读写，完成后也要输出结束标记。", Model = (int)M.Automation}},
        {"方案设计", new (){SystemPrompt = "你是一名优秀的方案设计师，需要完成用户指定的需求的详细方案设计，如果对方案有任何不清楚的，可以向用户提问后再继续。" +
                                       "对任务有任何不确定的、模糊或缺失的信息需要用户确认的，可以随时向用户提问要求补充信息以提高最后的结果质量。最后你需要产出一个完整的方案，一次性输出，并以<finish>true</finish>来结束输出，以便程序知道你的方案设计过程已经完成了。", Model = (int)M.Doubao_DeepSeekR1}},
        {"代码编写", new (){SystemPrompt = "你是一名优秀的程序设计师，需要根据用户提供的方案来编写完整的代码实现。请保持程序结构的简洁，及代码的完整性。不要重复思考过程和最后的输出过程，" +
                                       "对任务有任何不确定的、模糊或缺失的信息需要用户确认的，可以随时向用户提问要求补充信息以提高最后的结果质量。最后你需要产出一套完整的代码，一次性输出，并以<finish>true</finish>来结束输出，以便程序知道你的代码设计过程已经完成了。", Model = (int)M.Claude中杯}},
        {"文档编写", new (){SystemPrompt = "你是一名优秀的文档编写专家，可以针对任何需求，根据已知信息编写出一份结构合理、逻辑清晰、并且丰富完整的文档。" +
                                       "对任务有任何不确定的、模糊或缺失的信息需要用户确认的，可以随时向用户提问要求补充信息以提高最后的结果质量。最后你需要产出一个完整的文档，一次性输出，并以<finish>true</finish>来结束输出，以便程序知道你的文档编写过程已经完成了。", Model = (int)M.Claude中杯}},
        {"审阅者", new (){SystemPrompt = "你是一名评审专家，可以对文章、需求文档和代码进行质量评审，并返回修改意见。你可以向用户进行追问来确保自己的工作符合用户的预期。" +
                                      "对任务有任何不确定的、模糊或缺失的信息需要用户确认的，可以随时向用户提问要求补充信息以提高最后的结果质量。最后你需要产出一个完整的结果，只包含修改意见，不需要返回原文或修改后的完整文章。请一次性输出，并以<finish>true</finish>来结束输出，以便程序知道你的评审过程已经完成了。", Model = (int)M.MiniMax大杯}},
        {"计算者", new (){SystemPrompt = "你是一名数学专家，可以将复杂的问题转换成一行数学表达式，比如 (3+5)*2, 表达式中可以使用C#的一些数学运算符，比如Math.Pow等等，然后将公式调用数学计算器进行实际计算并得到精确的返回结果。" +
                                      "然后你需要将计算过程和结果产出一个完整的结论，一次性输出，并以<finish>true</finish>来结束输出，以便程序知道你的计算过程已经完成了。", Model = (int)M.GPT4o}}
    };

    protected override async IAsyncEnumerable<Result> DoProcessResult(FunctionCall func, ApiChatInputIntern input, bool reEnter = false)
    {
        var o = JObject.Parse(func.Arguments);
        input.Agent = new ApiChatInputIntern.AgentInfo()
        {
            Skill = o["skill"].Value<string>(), Role = o["role"].Value<string>(), Task = o["task"].Value<string>()
        };
        if (!skills.ContainsKey(input.Agent.Skill))
        {
            yield return Result.Error("技能参数错误：" + input.Agent.Skill);
            yield break;
        }
        var info = skills[input.Agent.Skill];
        if (!reEnter) //如果是首次进入，清除用户输入，只使用function的参数作为输入指令
        {
            input.QuestionContents = new();
            input.ChatContexts = ChatContexts.New();
            input.ChatContexts.AddQuestion(input.Agent.Task);
            var need_contextsNames = o["need_contexts"].Values<string>();
            if (need_contextsNames != null && need_contextsNames.Count() > 0)
            {
                foreach (var name in need_contextsNames)
                {
                    foreach (var ctx in input.ChatContexts.AgentResults) //用于传递多个Agent结果的内容保存在主对话上下文中
                    {
                        if (ctx.Key == name)
                        {
                            input.ChatContexts.AddQuestion(ctx.Value);
                            break;
                        }
                    }
                }
            }
        }
        
        input.ChatModel = info.Model;
        if (input.ChatContexts != null) //首次进入时才不为空，被function初始化过了
        {
            input.ChatContexts.AddQuestion(info.SystemPrompt, ChatType.System);
        }
        var attr = ChatModel.GetModel(info.Model);
        if (attr == null)
        {
            yield return Result.Error("模型参数配置错误：" + info.Model);
            yield break;
        }

        yield return Result.Reasoning($">**助理 {input.Agent.Role} 开始工作，使用模型 {attr.Name}**\n");
        var sb = new StringBuilder();
        var searchApi = _apiFactory.GetService(info.Model);
        await foreach (var res in searchApi.ProcessChat(input))
        {
            yield return res;
            if (res.resultType == ResultType.Answer)
            {
                sb.Append(res.ToString());
            }
        }

        if (sb.ToString().Contains("<finish>true</finish>"))
        {
            yield return Result.New(ResultType.FunctionResult, sb.ToString());
            input.ChatContexts.AgentResults.Add(
                new KeyValuePair<string, string>(input.Agent.Role,
                    "以下为 " + input.Agent.Role + "提供的参考信息：\n" + sb.ToString()));
        }
    }
}