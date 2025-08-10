using System.Diagnostics;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2.Extra;

/// <summary>
/// 用截屏并模拟鼠标键盘操作的方式操作虚拟浏览器执行任务或仅执行本地文件操作任务，支持Claude模型的RPA模式或编辑器模型
/// </summary>
[ApiProvider("Automation")]
public class ApiAutomationProvider : ApiProviderBase
{
    protected IApiFactory _apiFactory;
    public ApiAutomationProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IApiFactory apiFactory):base(configHelper,serviceProvider)
    {
        _apiFactory = apiFactory;
    }

    private void SaveFile(string filename, string content)
    {
        if (!Path.Exists(Path.GetDirectoryName(filename)))
            Directory.CreateDirectory(Path.GetDirectoryName(filename));
        File.WriteAllText(filename, content);
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
        
        var system = "";
        if (modelId == 331)
        {
            system = $"""
                       You are a helpful assistant that can control the computer text editor. Only use computer when you needed.
                       <SYSTEM_CAPABILITY>
                       * You can use bash and text_editor tools to run LINUX commands or edit local text file. Always use relative path. DO NOT recheck file content each time you write.
                       * If user need you send edited File to him, call "SendFile" function. Always use relative path.
                       * When using your computer function calls, they take a while to run and send back to you. Where possible/feasible, try to chain multiple of these calls all into one function calls request.
                       * The current time is {DateTime.Now:yyyy-MM-dd HH:mm:ss}.
                       </SYSTEM_CAPABILITY>
                      
                       Do not assume you did it correctly, use tools to verify.
                       If you are sure the current status is correct, you should stop or do next step, do not repeat action.
                       Think step by step. Before you start, think about the steps you need to take to achieve the desired outcome.
                       使用中文回复用户。
                      """;
        }
        else
        {
            system = $"""
                       You are a helpful assistant that can control the computer. Only use computer when you needed.
                       <SYSTEM_CAPABILITY>
                       * You are using a "browser only system" with internet access, the webpage is full screen.
                       * To open a new webpage, just call "OpenUrl" function and give it an url parameter. This should be your first action.
                       * If webpage has a popup window, please close it first, or it may stop all actions.
                       * If webpage need login with QRCode, stop and wait user scan the code to login. Wait until user ask to go on.
                       * If you need back to previous page, call "GoBack" function.
                       * If you need get full page html content, call "GetPageHtml" function.
                       * If you need scroll down or scroll up the web page, use mouse scroll.
                       * When viewing a page make sure you scroll down to see everything before deciding something isn't available.
                       * When using your computer function calls, they take a while to run and send back to you. Where possible/feasible, try to chain multiple of these calls all into one function calls request.
                       * The current time is {DateTime.Now:yyyy-MM-dd HH:mm:ss}.
                       </SYSTEM_CAPABILITY>
                      
                       <IMPORTANT>
                       * Try to chain multiple of these calls all into one function calls request, like click and type text.
                       * Try to use GetPageHtml function to get full page content, for whole article or long search result list content, DO NOT scroll page and screenshot for it. Your contexts can only keep last two screenshots.
                       * Before any text input, ensure the target has focus.
                       * This computer can NOT open google.com, youtube, twitter/x.com eg.
                       </IMPORTANT>
                      
                       Do sames things together, like a series of text input or type key commands, do not wait comfirm or screenshot for each step.
                       Do not assume you did it correctly, use tools to verify.
                       If you are sure the current status is correct, you should stop or do next step, do not repeat action.
                       After taking a screenshot, evaluate if you have achieved the desired outcome and to know what to do next and adapt your plan.
                       Think step by step. Before you start, think about the steps you need to take to achieve the desired outcome.
                       使用中文回复用户。
                      """;
        }

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
        var autoStopTimes = 30; //需要自动中止的次数
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
                    if (call.Name == "OpenUrl")
                    {
                        yield return Result.Reasoning($"call {call.Name}({call.Arguments})\n\n");
                        var o = JObject.Parse(call.Arguments);
                        var url = o["url"].Value<string>();
                        var ret = await brower.OpenUrl(url);
                        if(!ret)
                            call.Result= Result.Error("Error: Can't open this url, try another please.");
                    }
                    else if (call.Name == "SendFile")
                    {  
                        var o = JObject.Parse(call.Arguments);
                        var path =  o["path"].Value<string>();
                        var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"auto_files/"+ input.External_UserId + "/",
                            path);
                        if (File.Exists(fullPath))
                        {
                            var bytes = File.ReadAllBytes(fullPath);
                            yield return FileResult.Answer(bytes, Path.GetExtension(fullPath), ResultType.FileBytes,
                                Path.GetFileName(fullPath));
                        }
                        else
                        {
                            yield return Result.Error("文件不存在");
                        }
                    }
                    else if (call.Name == "GoBack")
                    {
                        await brower.GoBack();
                    }else if (call.Name == "GetPageHtml")
                    {
                        var html = await brower.GetVisibleHtml();
                        call.Result = Result.Answer(html);
                    }else if (call.Name == "computer")
                    {
                        yield return Result.Reasoning($"call computer_use({call.Arguments})\n\n");
                        var o =  JObject.Parse(call.Arguments);
                        var action = o["action"].Value<string>();
                        if (action == "screenshot")
                        {
                            var bytes = await brower.Screenshot();
                            if (bytes == null)
                            {
                                call.Result = Result.Answer("Error: You should call OpenUrl before screenshot.");
                            }
                            else
                            {
                                call.Result = FileResult.Answer(bytes, "png", ResultType.ImageBytes, "screenshot.png");
                            }
                            yield return call.Result;
                        }else if (action == "mouse_move")
                        {
                            if (o["coordinate"] is not null)
                            {
                                var coord = o["coordinate"].Values<int>().ToArray();
                                await brower.MoveMouse(coord[0], coord[1]);
                            }
                        }
                        else if (action == "left_click")
                        {
                            if (o["coordinate"] is not null)
                            {
                                var coord = o["coordinate"].Values<int>().ToArray();
                                await brower.Click(coord[0], coord[1]);
                            }
                            else
                            {
                                await brower.Click();
                            }
                        }else if (action == "type")
                        {
                            var text = o["text"].Value<string>();
                            await brower.InputText(text);
                        }else if (action == "key")
                        {
                            var text = o["text"].Value<string>();
                            if (text == "Return")
                                text = "Enter";
                            await brower.PressKey(text);
                        }else if (action == "scroll")
                        {
                            var amount = o["scroll_amount"].Value<int>();
                            var direction = o["scroll_direction"].Value<string>();
                            await brower.ScrollPage(amount, direction);
                        }
                        else if (action == "wait")
                        {
                            var duration = o["duration"].Value<int>();
                            Thread.Sleep(duration * 1000);
                        }
                    }else if (call.Name == "str_replace_based_edit_tool")
                    {
                        yield return Result.Reasoning($"call text_editor()\n\n");
                        var o = JObject.Parse(call.Arguments);
                        var path =  o["path"].Value<string>();
                        var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "auto_files/"+input.External_UserId + "/",
                            path);
                        var command = o["command"].Value<string>();
                        if (command == "create")
                        {
                            SaveFile(fullPath, o["file_text"].Value<string>());
                        }else if (command == "view")
                        {
                            if (!File.Exists(fullPath))
                            {
                                call.Result = Result.Answer("Error: File not exist.");
                            }
                            else
                            {
                                var text = File.ReadAllText(fullPath);
                                if (o["view_range"] is not null)
                                {
                                    var ranges = o["view_range"].Values<int>().ToArray();
                                    var lines = text.Split('\n');
                                    var min = Math.Max(0, ranges[0] - 1);
                                    var max = Math.Min(lines.Length, ranges[1] == -1 ? lines.Length : ranges[1] - 1);
                                    text = string.Join("\n", lines[new Range(min, max)]);
                                }

                                call.Result = Result.Answer(text);
                            }
                        }else if (command == "str_replace")
                        {
                            var text = File.ReadAllText(fullPath);
                            if (o["old_str"] is not null)
                            {
                                text = text.Replace(o["old_str"].Value<string>(), o["new_str"].Value<string>());
                            }
                            else
                            {
                                text += "\n" + o["new_str"].Value<string>();
                            }
                            SaveFile(fullPath, text);
                        }else if (command == "insert")
                        {
                            var text = File.ReadAllText(fullPath);
                            var lines = text.Split('\n').ToList();
                            var insert_line = o["insert_line"].Value<int>();
                            if (insert_line >= lines.Count + 1 || insert_line < 1)
                            {
                                call.Result = Result.Answer("Error: insert_line out of range.");
                            }
                            else
                            {
                                lines.Insert(insert_line - 1, o["new_str"].Value<string>());
                                text = string.Join("\n", lines);
                                SaveFile(fullPath, text);
                            }
                        }else if (command == "undo_edit")
                        {
                            call.Result = Result.Answer("Error: I can't undo edit.");
                        }
                    }else if (call.Name == "bash")
                    {
                        yield return Result.Reasoning($"call bash({call.Arguments})\n\n");
                        var o =  JObject.Parse(call.Arguments);
                        var command = o["command"].Value<string>();
                        var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "auto_files/"+input.External_UserId + "/");
                        // 创建一个 ProcessStartInfo 对象
                        var processStartInfo = new ProcessStartInfo
                        {
                            FileName = "/bin/bash", // 使用 bash shell
                            Arguments = $"-c \"{command}\"", // 执行的命令
                            RedirectStandardOutput = true,  // 重定向标准输出
                            RedirectStandardError = true,   // 重定向错误输出
                            UseShellExecute = false,        // 禁用 shell 执行
                            CreateNoWindow = true,           // 不创建窗口
                            WorkingDirectory = fullPath
                        };
                        // 启动进程
                        using (var process = new Process { StartInfo = processStartInfo })
                        {
                            process.Start();
                            // 读取输出结果
                            string output = process.StandardOutput.ReadToEnd();
                            string error = process.StandardError.ReadToEnd();
                            process.WaitForExit(); // 等待命令执行完成
                            // 如果有错误输出，返回错误信息
                            if (!string.IsNullOrWhiteSpace(error))
                            {
                                call.Result = Result.Error("Error:" + error);
                            }
                            else
                            {
                                // 返回命令执行结果
                                call.Result = Result.Answer(output);
                            }
                        }
                    }
                }
                else
                    yield return res;
            }

            if (!needRerun)
                break;
            if (times > autoStopTimes)
            {
                yield return Result.Answer("已达到自动操作步数上限，自动中止。");
                break;
            }
        }

        input.IgnoreAutoContexts = true; //跟内层模型共享同一个input对象，内层模型已经保存过上下文了，外层不需要保存，不然会重复叠加上下文
    }
    
    public override void InitSpecialInputParam(ApiChatInputIntern input)
    {
        input.IgnoreSaveLogs = true;
    }
}