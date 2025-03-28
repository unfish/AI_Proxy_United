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

[ApiClass(M.Automation, "RPA助手", "根据你的指令，自动使用一个虚拟浏览器打开指定的网页，并通过大模型的理解进行一步一步操作来完成指令，可以用于获取信息，但不要做步骤太复杂的操作。", 196, type: ApiClassTypeEnum.辅助模型, canProcessImage:true,canProcessFile:true, needLongProcessTime:true, priceIn: 0, priceOut: 0.1)]
public class ApiAutomation:ApiBase
{
    protected AutomationClient _client;
    public ApiAutomation(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<AutomationClient>();
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

[ApiClass(M.AutomationEditor, "文件助手",
    "根据你的指令，在服务器上自主创建文件、编辑文件，用来保存长文本，并可以直接返回整个文件。", 197,
    type: ApiClassTypeEnum.辅助模型, canProcessImage: true, canProcessFile: true, priceIn: 0, priceOut: 0.1)]
public class ApiAutomationEditor : ApiAutomation
{
    public ApiAutomationEditor(IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _client.ModelId = (int)M.ClaudeEditor;
    }
}

public class AutomationClient: IApiClient
{
    private IApiFactory _apiFactory;
    public AutomationClient(IApiFactory apiFactory)
    {
        _apiFactory = apiFactory;
    }

    private void SaveFile(string filename, string content)
    {
        if (!Path.Exists(Path.GetDirectoryName(filename)))
            Directory.CreateDirectory(Path.GetDirectoryName(filename));
        File.WriteAllText(filename, content);
    }
    
    public int ModelId = (int)M.ClaudeAgent;
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        input.ChatModel = ModelId;
        var api = _apiFactory.GetService(ModelId);
        var system = "";
        if (ModelId == (int)M.ClaudeEditor)
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
                        var bytes = File.ReadAllBytes(fullPath);
                        yield return FileResult.Answer(bytes, Path.GetExtension(fullPath), ResultType.FileBytes,
                            Path.GetFileName(fullPath));
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
                    }else if (call.Name == "str_replace_editor")
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
}


public class AutomationHelper
{
    private AutomationHelper()
    {
    }
    private static ConcurrentDictionary<string, AutomationHelper> HelperCaches = new ConcurrentDictionary<string, AutomationHelper>();

    public static async Task<AutomationHelper> GetInstance(string sessionId, int width=1024, int height=768)
    {
        if (string.IsNullOrEmpty(sessionId))
            sessionId = "main"; //不传session时使用共享同一个实例
        if (HelperCaches.TryGetValue(sessionId, out var instance))
        {
            return instance;
        }
        else
        {
            var helper = new AutomationHelper() {_sessionId  = sessionId, _pageWidth = width, _pageHeight = height };
            await helper.StartBrowser();
            HelperCaches.TryAdd(sessionId, helper);
            return helper;
        }
    }

    public static readonly string[] AutomationFunctions = new[]
    {
        "computer", "str_replace_editor", "bash", "OpenUrl", "GetPageHtml", "GoBack", "SendFile", "ClickElement", "InputElement", "Screenshot"
    };    
    
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private List<IPage> _pages = new List<IPage>();
    private string? _sessionId;
    private int _pageWidth = 0;
    private int _pageHeight = 0;
    private DateTime _lastActionTime = DateTime.Now;
    private async Task StartBrowser()
    {
        var playwright = await Playwright.CreateAsync();
        _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions(){Headless = true});
        _context = await _browser.NewContextAsync(new BrowserNewContextOptions()
        {
            ViewportSize =  new ViewportSize(){Width = _pageWidth, Height = _pageHeight}, DeviceScaleFactor = 2f
        });
        _context.Page += (sender, page) => { _pages.Add(page); };
    }

    private async Task CloseAllPages()
    {
        if (_pages.Count > 0)
        {
            for(var i = _pages.Count - 1; i >= 0; i--)
            {
                await _pages[i].CloseAsync();
                _pages.RemoveAt(i);
            }
        }
    }

    public async Task<bool> OpenUrl(string url)
    {
        await CloseAllPages();
        var page = await _context.NewPageAsync();
        page.SetDefaultTimeout(30000);
        _lastActionTime = DateTime.Now;
        try
        {
            await page.GotoAsync(url);
            // 等待页面完全加载
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            return true;
        }
        catch
        {
            return  false;
        }
    }

    private async Task Release()
    {
        await CloseAllPages();
        await _context.CloseAsync();
        await _browser.CloseAsync();
        HelperCaches.TryRemove(_sessionId, out _);
    }
    
    public async Task GoBack()
    {
        if (_pages.Count > 1)
        {
            _pages.RemoveAt(_pages.Count - 1);
        }else if (_pages.Count == 1)
        {
            await _pages[0].GoBackAsync();
            Thread.Sleep(_actionWaitTime);
        }
        _lastActionTime = DateTime.Now;
    }

    public async Task<byte[]?> Screenshot(bool fullscreen=false)
    {
        _lastActionTime = DateTime.Now;
        if (_pages.Count > 0)
        {
            var page = _pages.Last();
            var bytes = await page.ScreenshotAsync(new (){ FullPage = fullscreen});
            if (fullscreen)
                return bytes;
            return ImageHelper.Compress(bytes, new SKSize(_pageWidth, _pageHeight), SKEncodedImageFormat.Png);
        }
        else
        {
            return null;
        }
    }

    public async Task<string> GetHtml()
    {
        var page = _pages.Last();
        var html = await page.ContentAsync();
        html = HtmlHelper.ExtractCoreDom(html, false);
        _lastActionTime = DateTime.Now;
        return html;
    }
    
    public async Task<string> GetVisibleHtml()
    {
        var page = _pages.Last();
        // 在浏览器中执行JavaScript来获取可见DOM
        string visibleHtml = await page.EvaluateAsync<string>(@"
        () => {
            // 判断元素是否可见的函数
            function isVisible(element) {
                if (!element) return false;
                
                // 检查元素是否在DOM中
                if (!element.isConnected) return false;
                
                // 获取计算样式
                const style = window.getComputedStyle(element);
                
                // 检查基本可见性属性
                if (style.display === 'none') return false;
                if (style.visibility === 'hidden' || style.visibility === 'collapse') return false;
                if (parseFloat(style.opacity) === 0) return false;
                
                // 检查元素尺寸
                const rect = element.getBoundingClientRect();
                if (rect.width === 0 && rect.height === 0) return false;
                
                // 检查是否被裁剪到不可见
                if (style.overflow !== 'visible' && rect.width === 0 && rect.height === 0) return false;
                
                // 递归检查父元素
                if (element.parentElement) {
                    return isVisible(element.parentElement);
                }
                
                return true;
            }
            
            // 创建一个新的文档
            const visibleDoc = document.implementation.createHTMLDocument('');
            
            // 处理head元素
            const sourceHead = document.head;
            const targetHead = visibleDoc.head;
            
            // 清空目标head
            while (targetHead.firstChild) {
                targetHead.removeChild(targetHead.firstChild);
            }
            
            // 复制重要的head元素
            for (const child of sourceHead.children) {
                if (['TITLE', 'META'].includes(child.tagName)) {
                    const newNode = child.cloneNode(true);
                    targetHead.appendChild(newNode);
                }
            }
            
            // 处理body元素
            const sourceBody = document.body;
            const targetBody = visibleDoc.body;
            
            // 清空目标body
            while (targetBody.firstChild) {
                targetBody.removeChild(targetBody.firstChild);
            }
            
            // 处理body的子元素
            function processBodyChildren(sourceNode, targetParent) {
                for (const child of sourceNode.childNodes) {
                    // 处理元素节点
                    if (child.nodeType === Node.ELEMENT_NODE) {
                        // 跳过脚本和样式元素
                        if (['SCRIPT', 'STYLE', 'NOSCRIPT'].includes(child.tagName)) {
                            continue;
                        }
                        
                        // 检查可见性
                        if (!isVisible(child)) {
                            continue;
                        }
                        
                        // 创建新元素
                        const newElement = visibleDoc.createElement(child.tagName);
                        
                        // 复制属性
                        for (const attr of child.attributes) {
                            newElement.setAttribute(attr.name, attr.value);
                        }
                        
                        // 添加到目标父节点
                        targetParent.appendChild(newElement);
                        
                        // 递归处理子节点
                        processBodyChildren(child, newElement);
                    } 
                    // 处理文本节点
                    else if (child.nodeType === Node.TEXT_NODE) {
                        const text = child.textContent.trim();
                        if (text) {
                            targetParent.appendChild(visibleDoc.createTextNode(child.textContent));
                        }
                    }
                }
            }
            
            // 处理body内容
            processBodyChildren(sourceBody, targetBody);
            
            // 获取DOCTYPE
            let doctype = '';
            if (document.doctype) {
                doctype = new XMLSerializer().serializeToString(document.doctype);
            }
            
            // 返回HTML字符串
            return doctype + visibleDoc.documentElement.outerHTML;
        }
        ");

        var html = HtmlHelper.ExtractCoreDom(visibleHtml, false);
        _lastActionTime = DateTime.Now;
        return html;
    }
    
    public async Task MoveMouse(int x, int y)
    {
        var page = _pages.Last();
        await page.Mouse.MoveAsync(x, y);
        _currentX = x;
        _currentY = y;
        _lastActionTime = DateTime.Now;
    }

    private int _actionWaitTime = 300; //下面这些动作做完以后自动等待一段时间
    public async Task Click(int x, int y)
    {
        var page = _pages.Last();
        _currentX = x;
        _currentY = y;
        await page.Mouse.ClickAsync(x, y);
        // 等待页面完全加载
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }

    private int _currentX = 0;
    private int _currentY = 0;
    public async Task Click()
    {
        var page = _pages.Last();
        await page.Mouse.ClickAsync(_currentX, _currentY);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }
    public async Task InputText(string text)
    {
        var page = _pages.Last();
        await page.Keyboard.InsertTextAsync(text);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }
    
    public async Task PressKey(string text)
    {
        var page = _pages.Last();
        await page.Keyboard.PressAsync(text);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }
    
    public async Task<bool> ClickElement(string selector)
    {
        var page = _pages.Last();
        _lastActionTime = DateTime.Now;
        if (await page.Locator(selector).CountAsync() == 1)
        {
            await page.Locator(selector).ClickAsync(new() { Force = true });
            // 等待页面完全加载
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Thread.Sleep(_actionWaitTime);
            return true;
        }
        return false;
    }
    
    public async Task<bool> InputElement(string selector, string text)
    {
        var page = _pages.Last();
        _lastActionTime = DateTime.Now;
        if (await page.Locator(selector).CountAsync() == 1)
        {
            await page.Locator(selector).FillAsync(text, new() { Force = true });
            Thread.Sleep(_actionWaitTime);
            return true;
        }
        return false;
    }
    
    public async Task ScrollPage(int amount, string direction)
    {
        var x = 0;
        var y = amount * 80;
        switch (direction)
        {
            case "left":
                x = -amount * 80;
                y = 0;
                break;
            case "right":
                x = amount * 80;
                y = 0;
                break;
            case "up":
                x = 0;
                y = -amount * 80;
                break;
        }
        var page = _pages.Last();
        await page.Mouse.WheelAsync(x, y);
        Thread.Sleep(_actionWaitTime);
        _lastActionTime = DateTime.Now;
    }
    
    public static async Task CleanCache() //因为用户不会主动关闭已打开的浏览器，使用定时任务来清除半小时没操作过的浏览器进程
    {
        foreach (var key in HelperCaches.Keys)
        {
            if (HelperCaches.TryGetValue(key, out var instance))
            {
                if (instance._lastActionTime < DateTime.Now.AddMinutes(-30))
                {
                    await instance.Release();
                }
            }
        }
    }
}