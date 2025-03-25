using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.WebControl, "浏览器助手", "根据你的指令，自动使用一个虚拟浏览器打开指定的网页，并通过大模型的理解进行一步一步操作来完成指令，可以用于获取信息，但不要做步骤太复杂的操作。", 196, type: ApiClassTypeEnum.辅助模型, canProcessImage:true,canProcessFile:true, needLongProcessTime:true, priceIn: 0, priceOut: 0.1)]
public class ApiWebControl:ApiBase
{
    protected WebControlClient _client;
    public ApiWebControl(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<WebControlClient>();
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

public class WebControlClient: IApiClient
{
    private IApiFactory _apiFactory;
    public WebControlClient(IApiFactory apiFactory)
    {
        _apiFactory = apiFactory;
    }
    
    public int ModelId = (int)M.GPT4o;
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        input.ChatModel = ModelId;
        var api = _apiFactory.GetService(ModelId);
        var system = $"""
                       You are a helpful assistant that can control the computer. Only use computer when you needed.
                       <SYSTEM_CAPABILITY>
                       * You are using a "browser only system" with internet access, the webpage is full screen.
                       * To open a new webpage, just call "OpenUrl" function and give it an url parameter. This should be your first action.
                       * Once you opened the webpage, get full html by function "GetPageHtml", and check what can you do next.
                       * If you need back to previous page, call "GoBack" function.
                       * If you need send screenshot of current webpage to user, call "Screenshot" function. If current page need login, send Screenshot first, user can decide next step.
                       * You can use "ClickElement" and "InputElement" function to interact with the webpage.
                       * The current time is {DateTime.Now:yyyy-MM-dd HH:mm:ss}.
                       </SYSTEM_CAPABILITY>
                      
                       <IMPORTANT>
                       * Try to chain multiple of these calls all into one function calls request, like click and type text.
                       * This computer can NOT open google.com, youtube, twitter/x.com eg.
                       </IMPORTANT>
                      
                       Do sames things together, like a series of text input or type key commands, do not wait comfirm or screenshot for each step.
                       Do not assume you did it correctly, use tools to verify.
                       If you are sure the current status is correct, you should stop or do next step, do not repeat action.
                       Think step by step. Before you start, think about the steps you need to take to achieve the desired outcome.
                       使用中文回复用户。
                      """;

        if (input.ChatContexts.Contexts.Count == 1)
        {
            if (input.ChatContexts.SystemPrompt?.Contains("<finish>")==true)
            {
                system += "\n\n" + input.ChatContexts.SystemPrompt;
            }
            input.ChatContexts.SystemPrompt = "";
            input.ChatContexts.AddQuestion(system, ChatType.System);
        }
        input.AgentSystem = "web";
        input.DisplayWidth = 1280;
        input.DisplayHeight = 800;

        var times = 0; //计算循环次数，防止死循环
        var autoStopTimes = 20; //需要自动中止的次数
        while (true)
        {
            bool needRerun = false;
            if (ApiBase.CheckStopSigns(input))
            {
                yield return Result.Answer("收到停止指令，停止执行。");
                break;
            }
            times++;
            await foreach (var res in api.ProcessChat(input))
            {
                if (res.resultType == ResultType.FuncFrontend)
                {
                    needRerun = true;
                    var brower = await AutomationHelper.GetInstance(input.ChatContexts.SessionId, input.DisplayWidth.Value, input.DisplayHeight.Value);
                    var fr = (FrontFunctionResult)res;
                    var call = fr.result;
                    yield return Result.Reasoning($"call {call.Name}({call.Arguments})\n\n");
                    if (call.Name == "OpenUrl")
                    {
                        var o = JObject.Parse(call.Arguments);
                        var url = o["url"].Value<string>();
                        var ret = await brower.OpenUrl(url);
                        if(!ret)
                            call.Result= Result.Error("Error: Can't open this url, try another please.");
                    }
                    else if (call.Name == "GoBack")
                    {
                        await brower.GoBack();
                    }else if (call.Name == "Screenshot")
                    {
                        var bytes = await brower.Screenshot();
                        yield return FileResult.Answer(bytes, "png", ResultType.ImageBytes);
                    }else if (call.Name == "GetPageHtml")
                    {
                        var html = await brower.GetVisibleHtml();
                        call.Result = Result.Answer(html);
                    }else if (call.Name == "ClickElement")
                    {
                        var o = JObject.Parse(call.Arguments);
                        var ret = await brower.ClickElement(o["selector"].Value<string>());
                        if(!ret) call.Result =  Result.Error("Error: Can't click element, try another way please.");
                    }else if (call.Name == "InputElement")
                    {
                        var o = JObject.Parse(call.Arguments);
                        var ret = await brower.InputElement(o["selector"].Value<string>(), o["text"].Value<string>());
                        if(!ret) call.Result =  Result.Error("Error: Can't input text to element, try another way please.");
                    }
                }
                else
                    yield return res;
            }
            if(!needRerun)
                break;
            if (times > autoStopTimes)
            {
                yield return Result.Answer("已达到自动操作步数上限，自动中止。");
                break;
            }
        }

        input.IgnoreAutoContexts = true; //跟内层模型共享同一个input对象，内层模型已经保存过上下文了，外层不需要保存，不然会重复叠加上下文
    }
}
