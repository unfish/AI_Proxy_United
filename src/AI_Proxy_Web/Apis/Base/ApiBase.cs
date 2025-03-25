using System.Collections.Concurrent;
using System.Text;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using HtmlAgilityPack;
using Newtonsoft.Json;
using VersOne.Epub;

namespace AI_Proxy_Web.Apis.Base;

/// <summary>
/// 基类实现，提供三个虚方法定义
/// </summary>
public abstract class ApiBase
{
    private IServiceProvider _serviceProvider;
    private ILogRepository _logRepository;
    private IFunctionRepository _functionRepository;
    protected ApiBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logRepository = serviceProvider.GetRequiredService<ILogRepository>();
        _functionRepository = serviceProvider.GetRequiredService<IFunctionRepository>();
    }

    #region 并发检查和长循环自动停止检查
    private static ConcurrentDictionary<string, bool> StopSignsDictionary = new ConcurrentDictionary<string, bool>(); //全局中止命令
    private static ConcurrentDictionary<string, bool> RunningSignsDictionary = new ConcurrentDictionary<string, bool>(); //全局运行锁，同一个用户要防止多任务并发
    private static void SetRunningSigns(ApiChatInputIntern input)
    {
        if(input.RecursionLevel==0)
            RunningSignsDictionary.AddOrUpdate($"{input.External_UserId}_{input.ContextCachePrefix}_Running", true,
                (s, b) => true);
    }
    private static bool CheckRunningSigns(ApiChatInputIntern input)
    {
        if (input.RecursionLevel == 0)
            return RunningSignsDictionary.ContainsKey($"{input.External_UserId}_{input.ContextCachePrefix}_Running");
        return false;
    }
    private static void RemoveRunningSigns(ApiChatInputIntern input)
    {
        if (input.RecursionLevel == 0)
        {
            RunningSignsDictionary.TryRemove($"{input.External_UserId}_{input.ContextCachePrefix}_Running", out _);
            RemoveStopSigns(input);
        }
    }
    private static void SetStopSigns(ApiChatInputIntern input)
    {
        StopSignsDictionary.AddOrUpdate($"{input.External_UserId}_{input.ContextCachePrefix}_Stoping", true,
            (s, b) => true);
    }
    public static bool CheckStopSigns(ApiChatInputIntern input)
    {
        return StopSignsDictionary.ContainsKey($"{input.External_UserId}_{input.ContextCachePrefix}_Stoping");
    }
    private static void RemoveStopSigns(ApiChatInputIntern input)
    {
        StopSignsDictionary.TryRemove($"{input.External_UserId}_{input.ContextCachePrefix}_Stoping", out _);
    }
    
    #endregion

    /// <summary>
    /// 预处理输入参数
    /// </summary>
    /// <param name="input"></param>
    private async IAsyncEnumerable<Result> ProcessChatInput(ApiChatInputIntern input)
    {
        var dp = DI.GetApiClassAttribute(input.ChatModel);
        var ques = input.QuestionContents.FirstOrDefault(t => t.Type == ChatType.文本)?.Content ?? "";
        if (CheckRunningSigns(input) && (dp?.NeedLongProcessTime ?? false))
        {
            if (ques == "stop" || ques == "停止")
            {
                SetStopSigns(input);
                yield return Result.Error("程序会在结束当前阶段性任务后自动停止。");
            }
            else if (ques == "CLEAR")
            {
                RemoveRunningSigns(input);
                yield return Result.Error("运行标志已强制清除，您现在可以发起新的对话了。");
            }
            else
            {
                yield return Result.Error("上一个对话还在进行中，请在结束后发起新的对话。");
            }

            yield break;
        }

        this.InitSpecialInputParam(input);
        if (input.ChatContexts == null) //只有为空的时候才自动加载，某些API需要自己控制Contexts的保存和读取
        {
            if (input.IgnoreAutoContexts)
                input.ChatContexts = ChatContexts.New();
            else
            {
                input.ChatContexts = ChatContexts.GetChatContexts(input.External_UserId, input.ContextCachePrefix);
                //加载上下文记录以后，检查最后一次对话中是否有未完成的function call,如果有的话把本次用户的新输入提交给该function并继续执行
                var ac = input.ChatContexts.Contexts.LastOrDefault()?.AC.LastOrDefault();
                if (ac != null && ac.Type == ChatType.FunctionCall)
                {
                    var calls = JsonConvert.DeserializeObject<List<FunctionCall>>(ac.Content);
                    if (calls?.Any(t => t.Result is null) == true)
                    {
                        if (ques == "跳过")
                        {
                            foreach (var call in calls)
                            {
                                if (call.Result is null)
                                {
                                    call.Result = Result.Answer("Error: 未知错误，Function无法正常运行。");
                                }
                            }
                        }
                        else
                        {
                            await foreach (var res2 in _functionRepository.ProcessChatFunctionCalls(calls, input, true))
                            {
                                yield return res2;
                            }
                        }

                        ReplaceChatResultContexts(ResultType.FunctionCalls, JsonConvert.SerializeObject(calls), input);
                        input.QuestionContents.Clear();
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(input.ChatContexts.SystemPrompt))
        {
            var configHelper = _serviceProvider.GetRequiredService<ConfigHelper>();
            input.ChatContexts.SystemPrompt =
                input.ChatContexts.SystemPrompt.Replace("{Instruction}", configHelper.GetConfig<string>("Instruction"));
        }

        //预留处理，当使用多代理功能的时候，用户的输入可能要转发给下级代理来处理
        foreach (var q in input.QuestionContents)
        {
            if (q.Type == ChatType.文件Bytes && q.Bytes != null && q.Bytes.Length > 0)
            {
                if (q.MimeType.Contains("image"))
                {
                    q.Content = Convert.ToBase64String(ImageHelper.Compress(q.Bytes));
                    q.Type = ChatType.图片Base64;
                    q.Bytes = null;
                }
                else if (q.MimeType.Contains("audio"))
                {
                    if (dp?.CanProcessAudio == true)
                    {
                        q.Content = Convert.ToBase64String(q.Bytes);
                        q.Type = ChatType.语音Base64;
                        q.Bytes = null;
                    }
                    else
                    {
                        var audioService = _serviceProvider.GetRequiredService<IAudioService>();
                        var res = await audioService.VoiceToText(q.Bytes, q.FileName);
                        if (res.resultType == ResultType.Answer)
                        {
                            q.Content = res.ToString();
                            q.Type = ChatType.文本;
                            q.Bytes = null;
                        }
                    }
                }
            }
        }

        input.ChatContexts.AddQuestions(input.QuestionContents); //将本次问题合并进完整上下文
        input.QuestionContents.Clear();

        if (input.ChatContexts.Contexts.Last().AC.Count > 0)
        {
            input.ChatContexts.Contexts.Add(ChatContext.New(new List<ChatContext.ChatContextContent>()));
        }
        
        //前台没有指定使用特定函数的时候，根据输入的词自动加载可用函数
        if (ChatModel.CanUseFunction(input.ChatModel) && (input.WithFunctions == null || input.WithFunctions.Length == 0))
        {
            input.WithFunctions = _functionRepository.GetFunctionNamesByScene(input.ChatContexts);
        }
        
        //如果有业务系统用户ID，自动生成一个用户对应的token，用来调后端接口
        if (string.IsNullOrEmpty(input.UserToken) && input.UserId > 0)
        {
            input.UserToken = _logRepository.GenerateTokenByFeishuUserId(input.External_UserId);
        }

        if (dp?.CanProcessImage != true && input.ChatContexts.HasImage())
        {
            yield return Result.Error("当前模型不支持图片处理。");
        }

        if (dp?.CanProcessAudio != true && input.ChatContexts.HasAudio())
        {
            yield return Result.Error("当前模型不支持语音处理。");
        }

        if (dp?.CanProcessFile != true && input.ChatContexts.HasFile())
        {
            yield return Result.Error("当前模型不支持文件处理。");
        }

    }

    /// <summary>
    /// 虚方法，用来给子类继承，实现各个子类特有的参数预处理逻辑
    /// </summary>
    /// <param name="input"></param>
    protected virtual void InitSpecialInputParam(ApiChatInputIntern input)
    {
        
    }
    
    /// <summary>
    /// 虚方法，用来给子类继承，实现各个子类特有的开始新会话的处理，比如GPT Assistant在开启新会话的时候需要删除旧的ThreadId
    /// </summary>
    public virtual void StartNewContext(string ownerId)
    {
    }

    private void SaveChatResultContexts(ResultType resultType, string result, ApiChatInputIntern input)
    {
        if (input.IgnoreAutoContexts)
            return;
        
        //目前只有这三种需要保存到聊天上下文
        if (resultType == ResultType.Answer)
        {
            input.ChatContexts.AddAnswer(result, ChatType.文本);
        } 
        else if (resultType == ResultType.Reasoning)
        {
            input.ChatContexts.AddAnswer(result, ChatType.Reasoning);
        }
        else if (resultType == ResultType.FunctionCalls)
        {
            input.ChatContexts.AddAnswer(result, ChatType.FunctionCall);
        }
        else if (resultType == ResultType.MultiMediaResult)
        {
            input.ChatContexts.AddAnswer(result, ChatType.MultiResult);
        }

        ChatContexts.SaveChatContexts(input.External_UserId, input.ContextCachePrefix, input.ChatContexts);
    }
    
    private void ReplaceChatResultContexts(ResultType resultType, string result, ApiChatInputIntern input)
    {
        if (resultType == ResultType.FunctionCalls)
        {
            var an = input.ChatContexts.Contexts.LastOrDefault()?.AC
                .LastOrDefault(t => t.Type == ChatType.FunctionCall);
            if(an != null)
                an.Content = result;
            
            if(!input.IgnoreAutoContexts)
                ChatContexts.SaveChatContexts(input.External_UserId, input.ContextCachePrefix, input.ChatContexts);
        }
    }
    
    /// <summary>
    /// 后处理保存上下文和日志，返回sessionId
    /// </summary>
    /// <param name="result"></param>
    /// <param name="resultType"></param>
    /// <param name="input"></param>
    private Result SaveChatLogs(string result, ResultType resultType, ApiChatInputIntern input)
    {
        if (input.ChatContexts.Contexts.Count>0 && result.Length > 0 && !input.IgnoreSaveLogs) //有正常返回，保存上下文的日志. Internal类型是被内部调用的，它的外层应该保存过日志了
        {
            var q = "";
            var qt = ChatType.文本;
            var last = input.ChatContexts.Contexts.Last();
            foreach (var chat in last.QC)
            {
                if (chat.Type == ChatType.提示模板)
                {
                    q += chat.FileName;
                    qt = ChatType.提示模板;
                }
                else if (chat.Type == ChatType.文本)
                    q += chat.Content;
                else
                {
                    q += "[文件问答，不保存内容];";
                    qt = chat.Type;
                }
            }

            var rt = (resultType == ResultType.FunctionCalls
                ? ChatType.FunctionCall
                : (resultType == ResultType.ImageBytes ? ChatType.图片Base64 : ChatType.文本));
            var isFirst = input.ChatContexts.Contexts.Count == 1 && input.RecursionLevel == 0;
            var nid = _logRepository.AddChatLog(input.UserId, input.ChatFrom, input.ChatModel,
                q, result, qt, rt, input.External_UserId,
                input.ChatContexts.SessionId, isFirst);
            return LogSavedResult.Answer(new LogSavedResult.ChatLog()
                { Id = nid, SessionId = input.ChatContexts.SessionId, Content = result });
        }

        return Result.Error("无需保存");
    }
    
    private ResultType[] typesNeedLog = new[]
        { ResultType.Answer, ResultType.Reasoning, ResultType.FunctionCalls };
    
    /// <summary>
    /// 文本流式消息，如果子类不覆盖，默认使用GPT3.5来处理
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> ProcessChat(ApiChatInput apiInput)
    {
        var input = apiInput as ApiChatInputIntern;
        await foreach (var pRes in ProcessChatInput(input))
        {
            yield return pRes;
            if(pRes.resultType == ResultType.Error)
                yield break;
        }

        SetRunningSigns(input);
        var sb = new StringBuilder(); //返回的内容，只有正常返回需要记日志
        var sbAnswer = new StringBuilder(); //Answer需要记对话上下文
        var sbReason = new StringBuilder(); //Reason需要记对话上下文
        FunctionsResult? functionCalls = null; //Function Call需要保存下来到最后统一处理
        MultiMediaResult multiMediaResult = null;
        await foreach (var res in this.DoProcessChat(input))
        {
            if (typesNeedLog.Contains(res.resultType))
                sb.Append(res.ToString());
            if (res.resultType == ResultType.Answer)
                sbAnswer.Append(res.ToString());
            if (res.resultType == ResultType.Reasoning)
                sbReason.Append(res.ToString());

            if (res.resultType == ResultType.FunctionCalls) //只有function calls需要等过程结束后统一处理，其它中间响应都原样返回
                functionCalls = (FunctionsResult)res;
            else if (res.resultType == ResultType.MultiMediaResult) //记录完整上下文，忽略其它上下文
                multiMediaResult = (MultiMediaResult)res;
            else
            {
                yield return res;
            }
        }
        //保存上下文
        if (multiMediaResult != null)
        {
            SaveChatResultContexts(ResultType.MultiMediaResult, multiMediaResult.ToString(), input);
        }
        else
        {
            if (sbReason.Length > 0)
                SaveChatResultContexts(ResultType.Reasoning, sbReason.ToString(), input);
            if (sbAnswer.Length > 0)
                SaveChatResultContexts(ResultType.Answer, sbAnswer.ToString(), input);
        }

        if (functionCalls != null)
            SaveChatResultContexts(ResultType.FunctionCalls, functionCalls.ToString(), input);
        
        yield return Result.New(ResultType.AnswerFinished); 
        //保存日志
        var sRes = SaveChatLogs(sb.ToString(), sb.Length > 0 ? ResultType.Answer : ResultType.Error, input);
        if (sRes is LogSavedResult)
            yield return sRes;

        if (functionCalls != null && !CheckStopSigns(input))
        {
            await foreach (var res2 in _functionRepository.ProcessChatFunctionCalls(functionCalls.result, input))
            {
                yield return res2;
            }
            ReplaceChatResultContexts(ResultType.FunctionCalls, functionCalls.ToString(), input);
            if (!input.IgnoreAutoContexts && functionCalls.result.Any(t => t.NeedRecall)) //自动重新发起调用，递归调用自己，IgnoreAutoContexts为true的时候回答不会添加进上下文，重新发起调用会死循环
            {
                SaveChatLogs(functionCalls.ToString(), ResultType.Answer, input);
                await foreach (var res3 in ProcessChat(input))
                {
                    yield return res3;
                }
            }
        }
       
        RemoveRunningSigns(input);
    }

    //虚方法，留给子类覆盖
    protected virtual async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        var api = _serviceProvider.GetRequiredService<ApiOpenAIBase>();
        await foreach (var res in api.ProcessChat(input))
        {
            yield return res;
        }
    }

    /// <summary>
    /// 文本非流式消息，如果子类不覆盖，默认使用GPT3.5来处理
    /// </summary>
    /// <param name="apiInput"></param>
    /// <returns></returns>
    public async Task<Result> ProcessQuery(ApiChatInput apiInput)
    {
        var input = apiInput as ApiChatInputIntern;
        await foreach (var pRes in ProcessChatInput(input))
        {
            if(pRes.resultType == ResultType.Error)
                return pRes;
        }
        
        var res = await this.DoProcessQuery(input);
        SaveChatResultContexts(res.resultType, res.ToString(), input);
        SaveChatLogs(res.ToString(), res.resultType, input);
        
        if (res.resultType == ResultType.FunctionCalls)
        { 
            var functionCalls = (FunctionsResult) res;
            List<FunctionCall> frontFunctions = new List<FunctionCall>();
            var sb = new StringBuilder();
            await foreach (var res2 in _functionRepository.ProcessChatFunctionCalls(functionCalls.result, input))
            {
                if (res2.resultType == ResultType.FuncFrontend)
                    frontFunctions.Add(((FrontFunctionResult)res2).result);
                else if (res2.resultType == ResultType.Answer)
                    sb.Append(res2.ToString());
                else if (res2.resultType == ResultType.Error)
                    res = res2;
            }
            ReplaceChatResultContexts(ResultType.FunctionCalls, functionCalls.ToString(), input);
            if (!input.IgnoreAutoContexts && functionCalls.result.Any(t => t.NeedRecall)) //自动重新发起调用，递归调用自己，如果IgnoreAutoContexts上次的回答不会添加到上下文里，重新发起调用会死循环
            {
                SaveChatLogs(functionCalls.ToString(), ResultType.Answer, input);
                res = await ProcessQuery(input);
            }
            else
            {
                if (frontFunctions.Count > 1)
                    return FunctionsResult.Answer(frontFunctions, ResultType.FuncFrontendMulti);
                if (frontFunctions.Count==1)
                    return FrontFunctionResult.Answer(frontFunctions[0]);
                if (sb.Length > 0)
                    return Result.Answer(sb.ToString());
            }
        }
        return res;
    }
    
    //虚方法，留给子类覆盖
    protected virtual async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        var api = _serviceProvider.GetRequiredService<ApiOpenAIBase>();
        return await api.ProcessQuery(input);
    }

    public async Task<string> ReadFileTextContent(byte[] file, string fileName)
    {
        try
        {
            if (fileName.EndsWith(".txt"))
            {
                using var stream = new StreamReader(new MemoryStream(file));
                return stream.ReadToEnd();
            }
            else if (fileName.EndsWith(".epub"))
            {
                using var stream = new MemoryStream(file);
                EpubBook book = EpubReader.ReadBook(stream);
                StringBuilder sb = new StringBuilder();
                foreach (var textContentFile in book.ReadingOrder)
                {
                    HtmlDocument htmlDocument = new();
                    htmlDocument.LoadHtml(textContentFile.Content);
                    foreach (HtmlNode node in htmlDocument.DocumentNode.SelectNodes("//text()"))
                    {
                        sb.AppendLine(node.InnerText.Trim());
                    }
                }

                return sb.ToString();
            }
        }catch{}
        return string.Empty;
    }
    
    /// <summary>
    /// 文本向量化接口，虚方法，留给子类覆盖
    /// 注意：不同的模型的向量化的输入长度和输出长度不同，务必使用同一个模型进行向量化索引和搜索
    /// </summary>
    /// <param name="qc">问题列表</param>
    /// <param name="embedForQuery">向量的目的：true for文档索引，false for 查询</param>
    /// <returns></returns>
    public virtual async Task<(ResultType resultType, double[][]? result, string error)> ProcessEmbeddings(List<ChatContext.ChatContextContent> qc, bool embedForQuery =  false)
    {
        return (ResultType.Error, null, "该模型未实现向量化接口");
    }
    
    /// <summary>
    /// 虚方法，用来获取子类的额外配置项，用于画图类模型的样式和尺寸选择，以及思考模型的可控的思考深度等选项
    /// </summary>
    /// <param name="ext_userId"></param>
    public virtual List<ExtraOption>? GetExtraOptions(string ext_userId)
    {
        return null;
    }
    
    public virtual void SetExtraOptions(string ext_userId, string type, string value)
    {
    }
}
