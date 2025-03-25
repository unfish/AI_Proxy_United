using System.Text;
using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Functions.InternalFunctions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions;

/// <summary>
/// 处理Function Call功能的类
/// </summary>
public class FunctionRepository:IFunctionRepository
{
    private LogDbContext _dbContext;
    private IRestRepository _restRepository;
    private IBoyerMooreMatchService _boyerMooreMatchService;
    private IApiFactory _apiFactory;
    private IServiceProvider _serviceProvider;
    public FunctionRepository(LogDbContext context, IRestRepository restRepository, IBoyerMooreMatchService boyerMooreMatchService, IApiFactory apiFactory, IServiceProvider serviceProvider)
    {
        _dbContext = context;
        _restRepository = restRepository;
        _boyerMooreMatchService = boyerMooreMatchService;
        _apiFactory = apiFactory;
        _serviceProvider = serviceProvider;
    }
    
    /// <summary>
    /// 获取可用的function列表
    /// </summary>
    /// <param name="functionNames"></param>
    /// <param name="groupName"></param>
    /// <returns></returns>
    public List<Function> GetFunctionList(string[]? functionNames)
    {
        var list = new List<Function>();
        if (functionNames == null || functionNames.Length == 0)
            return list;
        var q = _dbContext.ChatGptFunctions.Where(t=>!t.Disabled);
        q = q.Where(t => functionNames.Contains(t.Name));
        var functions = q.OrderBy(t=>t.Id).Take(10).ToList();
        foreach (var func in functions)
        {
            list.Add(new Function(func.Name, func.Description, JObject.Parse(func.Parameters), func.FunctionPrompt));
        }
        return list;
    }

    private static DateTime LastUpdateCacheTime = DateTime.Now.AddDays(-1);
    private static Dictionary<string, string[]> triggerWords = new Dictionary<string, string[]>();
    private static Dictionary<string, Dictionary<string, string[]>> funcnameToTriggerWords =
        new Dictionary<string, Dictionary<string, string[]>>();
    
    /// <summary>
    /// 根据用户输入内容确定要附加哪些function定义到请求里，不能把所有的function都加上，因为会占字数，而且相近的定义也会影响识别准确性
    /// </summary>
    /// <param name="groupName"></param>
    /// <param name="chatContexts"></param>
    /// <returns></returns>
    public string[] GetFunctionNamesByScene(ChatContexts chatContexts, string groupName="Internal")
    {
        var sb = new StringBuilder();
        sb.AppendLine(chatContexts.SystemPrompt ?? "");
        foreach (var ctx in chatContexts.Contexts)
        {
            foreach (var qc in ctx.QC)
            {
                if (qc.Type == ChatType.文本)
                    sb.AppendLine(qc.Content);
            }
        }

        var allQ = sb.ToString();
        //使用静态变量缓存，避免每次都去查数据库，这个数据几乎是不会变的
        if (LastUpdateCacheTime.AddMinutes(30) < DateTime.Now)
        {
            triggerWords.Clear();
            funcnameToTriggerWords.Clear();
            
            var list = _dbContext.ChatGptFunctions.Where(t => !t.Disabled)
                .ToList();
            var groups = list.Select(t => t.GroupName).Distinct().ToArray();
            foreach (var group in groups)
            {
                triggerWords.Add(group, string
                    .Join(',', list.Where(t => t.GroupName==group && !string.IsNullOrEmpty(t.TriggerWords)).Select(t => t.TriggerWords).ToArray())
                    .Split(new char[] { ',', '，', ' ', '　' }, StringSplitOptions.RemoveEmptyEntries));
                funcnameToTriggerWords.Add(group, list.Where(t => t.GroupName == group && !string.IsNullOrEmpty(t.TriggerWords)).ToDictionary(
                    t => t.Name,
                    t => t.TriggerWords.Split(new char[] { ',', '，', ' ', '　' }, StringSplitOptions.RemoveEmptyEntries)));
            }
            LastUpdateCacheTime = DateTime.Now;
        }

        var names = new HashSet<string>();
        //把命中了触发词的函数名称返回给调用方
        if (!triggerWords.ContainsKey(groupName))
            return names.ToArray();
        var searchResult = _boyerMooreMatchService.Search(allQ, triggerWords[groupName], 10);
        foreach (var result in searchResult)
        {
            foreach (var dic in funcnameToTriggerWords[groupName])
            {
                if (dic.Value.Contains(result.Word))
                    names.Add(dic.Key);
            }
        }
        return names.ToArray();
    }

    /// <summary>
    /// 执行前检查参数配置是否完整
    /// </summary>
    /// <param name="func"></param>
    /// <returns></returns>
    private string CheckFunctionParam(ChatGptFunction? func)
    {
        if (func == null)
        {
            return "[FUNC FAILED] 方法未找到";
        }
        else if (func.FunctionType == FunctionType.Backend && string.IsNullOrEmpty(func.CallUrl))
        {
            return "[FUNC FAILED] 方法的调用URL未配置";
        }

        return string.Empty;
    }

    /// <summary>
    /// 如果是后端function，自动调用GET或POST接口，并获取返回数据
    /// </summary>
    /// <param name="func"></param>
    /// <param name="funcArgs"></param>
    /// <param name="userToken">调用后端功能的时候使用的用户Token，可以解决权限问题</param>
    /// <returns></returns>
    private (bool success, string result) GetBackendFunctionResult(ChatGptFunction func, string funcArgs, string userToken)
    {
        if (func.CallMethod == CallMethod.GET)
        {
            var url = func.CallUrl;
            if (!string.IsNullOrEmpty(funcArgs))
            {
                var o = JObject.Parse(funcArgs);
                foreach (var p in o.Properties())
                {
                    url = url.Replace("{" + p.Name + "}", p.Value.ToString());
                }
            }
            return _restRepository.DoGet(url, userToken);
        }
        else
        {
            var url = func.CallUrl;
            if (string.IsNullOrEmpty(funcArgs))
            {
                funcArgs = "{}";
            }
            return _restRepository.DoPost(url, funcArgs, userToken);
        }
    }
    
    /// <summary>
    /// 如果是Internal函数类型，根据指定的Service重新执行Process请求并返回最终结果
    /// </summary>
    /// <returns></returns>
    private async IAsyncEnumerable<Result> GetInternalFunctionChatResult(FunctionCall func,
        ApiChatInputIntern input, bool reEnter = false)
    {
        //复制一个新的input出来传递到下层，不包含当前的上下文，替换Prefix为函数ID，可以防止保存上下文时与主对话的上下文冲突，同时共享SessionId可以将Log日志连续保存
        var newInput = input with
        {
            ContextCachePrefix = func.Id, ChatContexts = null,
            RecursionLevel = input.RecursionLevel + 1, WithFunctions = null, AgentResults = input.ChatContexts?.AgentResults
        };
        BaseProcessor? funcProcessor = null;
        string? error = null;
        try
        {
            funcProcessor = _apiFactory.GetFuncProcessor(func.Name);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        if (funcProcessor == null)
        {
            yield return Result.Error(error);
            yield break;
        }
        
        await foreach (var res in funcProcessor.ProcessResult(func, newInput, input, reEnter))
        {
            yield return res;
        }
    }

    /// <summary>
    /// 处理多函数的Chat模式。
    /// reEntoer是否第二次(或更多次)重复进入该function，首次不需要用户输入的信息，二次进入的话需要
    /// </summary>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> ProcessChatFunctionCalls(List<FunctionCall> functionCalls, ApiChatInputIntern input, bool reEnter = false)
    {
        List<FunctionCall> duplicate = new List<FunctionCall>(); //有时候会出现相同方法相同参数的多次调用，避免重复调用
        foreach (var call in functionCalls)
        {
            if(ApiBase.CheckStopSigns(input))
                break;
            if(call.Result != null)
                continue;
            
            //MoonShot的内置联网搜索功能需要特殊处理，将参数原样返回即可
            if (call.Name == "$web_search")
            {
                call.Result = Result.Answer(call.Arguments);
                call.Type = FunctionType.Internal;
                call.NeedRecall = true;
                continue;
            }

            //Claude专用方法
            if (AutomationHelper.AutomationFunctions.Contains(call.Name))
            {
                call.Result = Result.Answer("DONE");
                call.Type = FunctionType.Frontend;
                yield return FrontFunctionResult.Answer(call);
                continue;
            }

            var func = _dbContext.ChatGptFunctions.FirstOrDefault(t => t.Name == call.Name);
            var error = CheckFunctionParam(func);
            if (!string.IsNullOrEmpty(error)) //如果定义不完整，直接返回错误信息给前端
            {
                call.Result = Result.Error(error);
                call.Type = FunctionType.Frontend;
                yield return call.Result;
            }
            else
            {
                call.Type = func.FunctionType;
                if (func.FunctionType == FunctionType.Backend) //如果是后端方法，调用后端接口获取结果
                {
                    //有时候GPT会返回相同的函数名和参数两次，避免重复执行，取相同的结果
                    if (duplicate.Any(t => t.Name == call.Name && t.Arguments == call.Arguments))
                    {
                        var last = duplicate.First(t => t.Name == call.Name && t.Arguments == call.Arguments);
                        call.Result = last.Result;
                        continue;
                    }
                    
                    //执行后端部方法
                    yield return FunctionStartResult.Answer(call);
                    var (success, fcResult) = GetBackendFunctionResult(func, call.Arguments, input.UserToken);
                    if (!success)
                    {
                        error = "[FUNC FAILED]" + JsonConvert.SerializeObject(call) + fcResult;
                        call.Result = Result.Error(error);
                        yield return call.Result;
                    }
                    else
                    {
                        if (func.UseResultDirect) //如果方法要求直接原样返回结果，将结果中的body参数直接返回前端
                        {
                            var o = JObject.Parse(fcResult);
                            call.Result = Result.Answer("DONE");
                            yield return Result.Answer(o["body"].ToString());
                        }
                        else //否则将方法返回的结果再次传给GPT进行回答
                        {
                            call.Result = Result.Answer(fcResult);
                            call.Prompt = func.FunctionPrompt;
                            call.NeedRecall = true;
                        }
                    }
                    duplicate.Add(call);
                }
                else if (func.FunctionType == FunctionType.Internal) //如果是Internal函数则调用内部专用方法逻辑来处理
                {
                    if (duplicate.Any(t => t.Name == call.Name && t.Arguments == call.Arguments)) //防止返回重复的方法定义导致重复执行
                    {
                        var last = duplicate.First(t => t.Name == call.Name && t.Arguments == call.Arguments);
                        call.Result = last.Result;
                        continue;
                    }
                    
                    //执行内部方法
                    yield return FunctionStartResult.Answer(call);
                    await foreach (var res in GetInternalFunctionChatResult(call, input, reEnter))
                    {
                        if(res.resultType == ResultType.FunctionResult || res.resultType == ResultType.SearchResult)
                        {
                            call.Result = Result.Answer(res.ToString());
                            call.Prompt = func.FunctionPrompt;
                            call.NeedRecall = true;
                        }
                        else if (res.resultType == ResultType.Error) //错误消息既返回前台用于调试，也返回模型重新处理，模型可能会调整参数重新发起别的function call
                        {
                            call.Result = res;
                            call.NeedRecall = true;
                            yield return res;
                        }
                        else
                        {
                            yield return res;
                        }
                    }

                    reEnter = false; //如果reEnter进来的时候是true，也只能使用一次，如果有多个串行function的话，后续的其它func仍然是首次进入
                    if (call.Result == null)
                    {
                        if (DI.IsMultiTurnFunc(call.Name)) //如果是可以多轮对话的function，前面没有返回完成状态，则中止后面的function执行
                        {
                            yield break;
                        }
                        else
                        {
                            call.Result = Result.Answer("DONE"); //如果函数处理过程没有返回需要保存的结果，自动给一个默认内容，告诉模型已经调用成功就可以了
                        }
                    }
                    duplicate.Add(call);
                }
                else if (func.FunctionType == FunctionType.Frontend) //前端方法直接返回方法定义给前端
                {
                    call.Result = Result.Answer("DONE");
                    if(duplicate.Any(t=>t.Name==call.Name && t.Arguments==call.Arguments)) //防止返回重复的方法定义导致前端重复执行
                        continue;
                    duplicate.Add(new FunctionCall() { Name = call.Name, Arguments = call.Arguments });
                    yield return FrontFunctionResult.Answer(call);
                }
            }
        }
    }
    
}