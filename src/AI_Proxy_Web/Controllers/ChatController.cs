using System.Text;
using System.Text.RegularExpressions;
using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using AI_Proxy_Web.WebSockets;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FileResult = AI_Proxy_Web.Apis.Base.FileResult;

namespace AI_Proxy_Web.Controllers;

/// <summary>
/// WEB请求接口，网页、小程序、云文档组件都走这个接口
/// </summary>
[Route("api/ai")]
public class ChatController : BaseController
{
    private ILogRepository _logRepository;
    private IApiFactory _apiFactory;
    private IServiceProvider _serviceProvider;
    private IAudioService _audioService;
    private const string contextCachePrefix = "web";
    private string SiteHost;

    public ChatController(ILogRepository logRepository, IApiFactory apiFactory, IServiceProvider serviceProvider,
        IAudioService audioService, ConfigHelper configHelper)
    {
        this._logRepository = logRepository;
        this._apiFactory = apiFactory;
        this._serviceProvider = serviceProvider;
        this._audioService = audioService;
        SiteHost = configHelper.GetConfig<string>("Site:Host");
        MasterToken = configHelper.GetConfig<string>("Site:MasterToken");
    }

    /// <summary>
    /// 检查传入参数，同时对传入参数做标准化处理
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    private (string error, WebUserDto user) CheckInputError(WebChatInput input)
    {
        if (input == null)
            return ("参数错误", null);
        var check = CheckUserPermission(input.ChatFrom);
        if (check.user!= null && check.user.UserId > 0)
        {
            if (string.IsNullOrEmpty(input.Question))
                return ("请输入您的问题", check.user);
        }
        return check;
    }

    private ApiChatInput GetApiChatInput(WebChatInput input, int userId, string feishuId, string target)
    {
        var token = CurrentToken();
        var extUserId = GetExternalUserId(feishuId, token);
        var contexts = new List<ChatContext.ChatContextContent>();

        if (!string.IsNullOrEmpty(input.SystemPrompt))
            contexts.Add(ChatContext.NewContent(input.SystemPrompt, ChatType.System));
        
        if (!string.IsNullOrEmpty(input.WithPromptKey) && input.WithPromptKey != "menu_tpl_all")
        {
            var tpl = _logRepository.GetPromptByKey(input.WithPromptKey);
            if (tpl != null)
            {
                contexts.Add(ChatContext.NewContent(tpl.Prompt, ChatType.System));
            }
        }

        if (!string.IsNullOrEmpty(input.VirtualContexts))
            contexts.Add(ChatContext.NewContent(input.VirtualContexts, ChatType.VirtualContexts));

        if (!string.IsNullOrEmpty(input.Question))
        {
            contexts.Add(ChatContext.NewContent(input.Question));
        }

        var apiInput = ApiChatInput.New() with
        {
            QuestionContents = contexts, ChatModel = input.ChatModel, ChatFrom = input.ChatFrom,
            WithFunctions = input.WithFunctions, AudioVoice = input.WithVoiceId, AudioFormat = input.WithVoiceFormat,
            UserId = userId, UserToken = token, External_UserId = extUserId, ContextCachePrefix = contextCachePrefix,
            DisplayWidth = input.DisplayWidth, DisplayHeight = input.DisplayHeight, Temprature = input.Temprature ?? (decimal)0.6
        };
        if (!input.WithContext && target=="chat")
        {
            ChatContexts.ClearChatContexts(extUserId, contextCachePrefix);
        }
        return apiInput;
    }

    /// <summary>
    /// 聊天接口，调用ApiFactory统一处理
    /// </summary>
    /// <param name="input"></param>
    [HttpPost("chat")]
    public async Task Chat([FromBody] WebChatInput input)
    {
        await HttpContext.SSEInitAsync();
        var check = CheckInputError(input);
        if (!string.IsNullOrEmpty(check.error))
        {
            await HttpContext.SSESendChatEventAsync(JsonConvert.SerializeObject(new ResultDto()
                { resultType = ResultType.Error.ToString(), result = check.error }));
            await HttpContext.SSESendDataAsync("[DONE]");
            return;
        }

        if (!string.IsNullOrEmpty(input.Question))
        {
            var apiInput = GetApiChatInput(input,check.user.UserId, check.user.FeishuId, "chat");
            if (apiInput.ChatModel == -1)
            {
                apiInput.ChatModel = ChatModel.GetUserDefaultModel(apiInput.External_UserId, contextCachePrefix);
                if (input.Question.Contains("切换到"))
                    apiInput.ChatModel = 0;
            }

            if (!DI.IsApiClass(input.ChatModel))
                await HttpContext.SSESendChatEventAsync(
                    JsonConvert.SerializeObject(new ResultDto()
                        { resultType = ResultType.Error.ToString(), result = "ChatModel参数错误，该模型不存在" }));
            else
            {
                var _api = _apiFactory.GetService(input.ChatModel);
                if (!input.WithVoice || input.WithFullVoice)
                {
                    var sb = new StringBuilder();
                    await foreach (var res in _api.ProcessChat(apiInput))
                    {
                        await HttpContext.SSESendChatEventAsync(
                            JsonConvert.SerializeObject(new ResultDto()
                                { resultType = res.resultType.ToString(), result = res.ToString() }));
                        if (res.resultType == ResultType.Answer)
                            sb.Append(res.ToString());

                        if (res.resultType == ResultType.FuncFrontend)
                        {
                            var func = ((FrontFunctionResult)res).result;
                            await ProcessFuncFrontend(func, apiInput.External_UserId);
                        }
                    }

                    if (input.WithFullVoice && sb.Length > 0)
                    {
                        var resp2 = await GetFullAudioFileName(sb, input.WithVoiceId, input.WithVoiceFormat);
                        if (resp2.resultType == ResultType.AudioUrl)
                        {
                            await HttpContext.SSESendChatEventAsync(
                                JsonConvert.SerializeObject(new ResultDto()
                                    { resultType = ResultType.AudioUrl.ToString(), result = resp2.ToString() }));
                        }
                    }
                }
                else
                {
                    var sb = new StringBuilder();
                    var sender = new AudioMessageSender(HttpContext, _apiFactory);
                    sender.Start(input.WithVoiceId, input.WithVoiceFormat, apiInput.External_UserId);
                    await foreach (var res in _api.ProcessChat(apiInput))
                    {
                        await HttpContext.SSESendChatEventAsync(
                            JsonConvert.SerializeObject(new ResultDto()
                                { resultType = res.resultType.ToString(), result = res.ToString() }));
                        if (res.resultType == ResultType.Answer)
                        {
                            sb.Append(res.ToString());
                        }

                        if (res.resultType == ResultType.FuncFrontend)
                        {
                            var func = ((FrontFunctionResult)res).result;
                            await ProcessFuncFrontend(func, apiInput.External_UserId);
                        }

                        sb = await CreateVoiceTaskAndSend(sender, sb);
                    }

                    await SendRemainVoices(sender, sb);
                    sender.Finish();
                    sender.Wait();
                }
            }
        }

        await HttpContext.SSESendDataAsync("[DONE]");
    }

    private async Task<Result> GetFullAudioFileName(StringBuilder sb, string withVoiceId, string withVoiceFormat)
    {
        var resp2 = await _audioService.TextToVoice(sb.ToString(), withVoiceId, withVoiceFormat);
        if (resp2.resultType == ResultType.AudioBytes)
        {
            var bin = ((FileResult)resp2).result;
            var fileName = $"{DateTime.Now.Ticks}.{((FileResult)resp2).fileExt}";
            System.IO.File.WriteAllBytes(fileName, bin);
            return Result.New(ResultType.AudioUrl, fileName);
        }

        return resp2;
    }

    private async Task SendRemainVoices(AudioMessageSender sender, StringBuilder sb)
    {
        if (sb.ToString().Trim().Length > 0)
        {
            sender.AddAnswer(sb.ToString());
        }
    }

    private async Task<StringBuilder> CreateVoiceTaskAndSend(AudioMessageSender sender, StringBuilder sb)
    {
        if (sb.Length > 10)
        {
            var index = 0;
            if (sb.Length > 200)
                index = Math.Max(sb.ToString().LastIndexOf('。', 200), sb.ToString().LastIndexOf('\n', 200));
            else
                index = Math.Max(sb.ToString().LastIndexOf('。'), sb.ToString().LastIndexOf('\n'));
            if (index > 10)
            {
                var text = sb.ToString().Substring(0, index + 1);
                sb = sb.Remove(0, index + 1);
                sender.AddAnswer(text);
            }
        }

        return sb;
    }

    [HttpGet("file/{filename}")]
    public async Task<ActionResult> GetAudioFile(string filename, ChatFrom chatFrom)
    {
        var check = CheckUserPermission(chatFrom);
        if (!string.IsNullOrEmpty(check.error))
        {
            return BadRequest();
        }

        if (filename.EndsWith(".mp3") || filename.EndsWith(".png") || filename.EndsWith(".jpg"))
        {
            if (!System.IO.File.Exists(filename))
                return NotFound();
            var mime = filename.EndsWith(".mp3")
                ? "audio/mpeg"
                : (filename.EndsWith(".png") ? "image/png" : "image/jpeg");
            var fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read);
            return new FileStreamResult(fileStream, mime);
        }

        return NotFound();
    }

    /// <summary>
    /// 非流式聊天接口，调用ApiFactory统一处理
    /// </summary>
    /// <param name="input"></param>
    [HttpPost("query")]
    public async Task<MstApiResult<ResultDto>> Query([FromBody] WebChatInput input)
    {
        var check = CheckInputError(input);
        if (!string.IsNullOrEmpty(check.error))
            return R.Error<ResultDto>(check.error);

        var apiInput = GetApiChatInput(input, check.user.UserId, check.user.FeishuId, "query");
        apiInput.IgnoreAutoContexts = true; //query方式作为一次性调用接口，不考虑上下文
        
        if (!DI.IsApiClass(input.ChatModel))
            return R.Error<ResultDto>("ChatModel参数错误，该模型不存在");

        //GPT4接口请求自动替换成MiniMax
        if (input.ChatModel == 1)
            input.ChatModel = 4;
        var _api = _apiFactory.GetService(input.ChatModel);
        var resp = await _api.ProcessQuery(apiInput);
        var result = new ResultDto() { resultType = resp.resultType.ToString(), result = resp.ToString() };
        if (input.WithVoice && resp.resultType == ResultType.Answer)
        {
            var resp2 = await _audioService.TextToVoice(resp.ToString(), input.WithVoiceId, input.WithVoiceFormat);
            var bin = ((FileResult)resp2).result;
            var fileName = $"{DateTime.Now.Ticks}.{((FileResult)resp2).fileExt}";
            System.IO.File.WriteAllBytes(fileName, bin);
            result.extraResults = new List<ResultDto>()
                { new() { resultType = ResultType.AudioUrl.ToString(), result = fileName } };
        }

        if (resp.resultType == ResultType.FuncFrontendMulti) //返回多个前端函数的时候，为保持兼容主体只返回一个，完整数组放到extraResults里
        {
            var funcs = ((FunctionsResult)resp).result;
            result = new ResultDto()
            {
                resultType = ResultType.FuncFrontend.ToString(), result = JsonConvert.SerializeObject(funcs[0]),
                extraResults = new() { new() { resultType = resp.resultType.ToString(), result = resp.ToString() } }
            };
        }

        return R.New(result);
    }


    /// <summary>
    /// 文件聊天接口，调用ApiFactory统一处理
    /// </summary>
    /// <param name="chatModel"></param>
    /// <param name="chatFrom"></param>
    /// <param name="fileType">要处理的文件类型，image/file/audio</param>
    /// <param name="stream">是否要使用流式返回，飞书小程序不支持流式返回，只能用非流式</param>
    /// <param name="withContext">是否要保留上下文</param>
    /// <param name="withVoice">是否返回语音朗读</param>
    /// <param name="withFullVoice">是否使用完整语音文件，false时流式返回语音片段</param>
    /// <param name="withVoiceId">语音音色ID</param>
    /// <param name="withVoiceFormat">返回的语音文件格式，默认mp3，可选pcm</param>
    /// <param name="temprature">请求模型的温度</param>
    [HttpPost("file")]
    public async Task UploadFile(int chatModel, ChatFrom chatFrom, string fileType = "image", bool stream = true,
        bool withContext = false, bool withVoice = false, bool withFullVoice = false,
        string? withVoiceId = null, string withVoiceFormat = "mp3", decimal? temprature = 0)
    {
        if (stream)
            await HttpContext.SSEInitAsync();
        var check = CheckUserPermission(chatFrom);
        if (!string.IsNullOrEmpty(check.error))
        {
            if (stream)
            {
                await HttpContext.SSESendChatEventAsync(JsonConvert.SerializeObject(new ResultDto()
                    { resultType = ResultType.Error.ToString(), result = check.error }));
                await HttpContext.SSESendDataAsync("[DONE]");
            }
            else
            {
                await HttpContext.SendNonStreamAsync(R.Error<string>(check.error));
            }

            return;
        }

        if (Request.Form.Files.Count > 0)
        {
            var prompt = Request.Form["prompt"];
            var extUserId = GetExternalUserId(check.user.FeishuId, CurrentToken());
            if (chatModel == -1)
                chatModel = ChatModel.GetUserDefaultModel(extUserId, contextCachePrefix);
            var input = ApiChatInput.New() with
            {
                ChatFrom = chatFrom, ChatModel = chatModel, UserId = check.user.UserId,
                AudioVoice = Request.Form["model"], UserToken = CurrentToken(),
                External_UserId = extUserId, IgnoreAutoContexts = !withContext,
                ContextCachePrefix = contextCachePrefix,
                Temprature = temprature ?? 0
            };
            var qc = new List<ChatContext.ChatContextContent>();
            foreach (var formFile in Request.Form.Files)
            {
                using (var ms = new MemoryStream())
                {
                    formFile.CopyTo(ms);
                    qc.Add(ChatContext.NewContent("", ChatType.文件Bytes, GetMimeType(formFile.FileName), formFile.FileName,  ms.ToArray()));
                }
            }
            if (!string.IsNullOrEmpty(prompt))
                qc.Add(ChatContext.NewContent(prompt));

            var _api = _apiFactory.GetService(chatModel);
            var sb = new StringBuilder();
            var resultType = ResultType.Error;
            var sender = new AudioMessageSender(HttpContext, _apiFactory);
            sender.Start(withVoiceId, withVoiceFormat, input.External_UserId);

            if (string.IsNullOrEmpty(prompt))
                qc.Add(ChatContext.NewContent("请简要描述一下这张图片的内容。"));
            await foreach (var res in _api.ProcessChat(input))
            {
                if (res.resultType == ResultType.Answer || res.resultType == ResultType.Error)
                    sb.Append(res.ToString());
                resultType = res.resultType;
                if (stream)
                {
                    await HttpContext.SSESendChatEventAsync(
                        JsonConvert.SerializeObject(new ResultDto()
                            { resultType = res.resultType.ToString(), result = res.ToString() }));
                    if (withVoice && !withFullVoice)
                    {
                        sb = await CreateVoiceTaskAndSend(sender, sb);
                    }
                }
            }

            if (stream && withVoice && !withFullVoice)
            {
                await SendRemainVoices(sender, sb);
            }

            sender.Finish();
            sender.Wait();
            
            if (stream)
            {
                if (withFullVoice && sb.Length > 0)
                {
                    var resp2 = await GetFullAudioFileName(sb, withVoiceId, withVoiceFormat);
                    if (resp2.resultType == ResultType.AudioUrl)
                    {
                        await HttpContext.SSESendChatEventAsync(
                            JsonConvert.SerializeObject(new ResultDto()
                                { resultType = ResultType.AudioUrl.ToString(), result = resp2.ToString() }));
                    }
                }

                await HttpContext.SSESendDataAsync("[DONE]");
            }
            else
            {
                var result = new ResultDto()
                    { resultType = resultType.ToString(), result = sb.ToString() };
                if (withFullVoice && sb.Length > 0)
                {
                    var resp2 = await GetFullAudioFileName(sb, withVoiceId, withVoiceFormat);
                    if (resp2.resultType == ResultType.AudioUrl)
                    {
                        result.extraResults = new List<ResultDto>()
                            { new() { resultType = ResultType.AudioUrl.ToString(), result = resp2.ToString() } };
                    }
                }

                await HttpContext.SendNonStreamAsync(R.New(result));
            }
        }
        else
        {
            if (stream)
            {
                await HttpContext.SSESendChatEventAsync(JsonConvert.SerializeObject(new ResultDto()
                    { resultType = ResultType.Error.ToString(), result = "没有上传文件" }));
                await HttpContext.SSESendDataAsync("[DONE]");
            }
            else
            {
                await HttpContext.SendNonStreamAsync(R.Error<string>("没有上传文件"));
            }
        }
    }

    private async Task ProcessFuncFrontend(FunctionCall func, string exUserId)
    {
        if (func.Name == "ChangeModel")
        {
            var o = JObject.Parse(func.Arguments);
            var model = o["model"]?.Value<int>() ?? -1;
            var name = o["name"]?.Value<string>() ?? "";
            if (!string.IsNullOrEmpty(name))
                model = ChatModel.GetModelIdByName(name);
            if (model == -1)
            {
                await HttpContext.SSESendChatEventAsync(
                    JsonConvert.SerializeObject(new ResultDto()
                        { resultType = ResultType.Error.ToString(), result = "指定的模型不存在" }));
            }
            else
            {
                var tip = ChatModel.SetDefaultModel(exUserId, contextCachePrefix, model);
                await HttpContext.SSESendChatEventAsync(
                    JsonConvert.SerializeObject(new ResultDto()
                        { resultType = ResultType.Answer.ToString(), result = tip }));
            }
        }
    }


    /// <summary>
    /// 聊天接口，调用ApiFactory统一处理
    /// </summary>
    /// <param name="input"></param>
    [HttpPost("SetToolResults")]
    public async Task SetToolResults([FromBody] ToolResultInput input)
    {
        await HttpContext.SSEInitAsync();
        var check = CheckUserPermission(input.ChatFrom);
        if (!string.IsNullOrEmpty(check.error))
        {
            await HttpContext.SSESendChatEventAsync(JsonConvert.SerializeObject(new ResultDto()
                { resultType = ResultType.Error.ToString(), result = check.error }));
            await HttpContext.SSESendDataAsync("[DONE]");
            return;
        }

        var token = CurrentToken();
        var extUserId = GetExternalUserId(check.user.FeishuId, token);
        var chatModel = input.ChatModel > 0
            ? input.ChatModel
            : ChatModel.GetUserDefaultModel(extUserId, contextCachePrefix);
        var qc = new List<ChatContext.ChatContextContent>();
        //使用前端传过来的函数执行结果替换掉之前保存的上下文里面的函数结果

        foreach (var t in input.ToolResults)
        {
            var call = new FunctionCall()
            {
                Id = t.tool_id, Result = t.result_type == ToolResultInput.ToolResultTypeEnum.Image
                    ? FileResult.Answer(Convert.FromBase64String(t.content), t.mime_type, ResultType.ImageBytes)
                    : Result.Answer(t.content)
            };
            qc.Add(ChatContext.NewContent(JsonConvert.SerializeObject(call), ChatType.FunctionCall));
        }
                
        var apiInput = ApiChatInput.New() with
        {
            ChatModel = chatModel, ChatFrom = input.ChatFrom, QuestionContents = qc, IgnoreAutoContexts = false,
            UserId = check.user.UserId, UserToken = token, External_UserId = extUserId,
            ContextCachePrefix = contextCachePrefix
        };
        var _api = _apiFactory.GetService(chatModel);
        await foreach (var res in _api.ProcessChat(apiInput))
        {
            await HttpContext.SSESendChatEventAsync(
                JsonConvert.SerializeObject(new ResultDto()
                    { resultType = res.resultType.ToString(), result = res.ToString() }));
        }

        await HttpContext.SSESendDataAsync("[DONE]");
    }


    /// <summary>
    /// 获取用户历史聊天记录
    /// </summary>
    /// <param name="chatFrom"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    [HttpPost("getlogs")]
    public async Task<MstApiResult<List<ChatGptLog>>> GetLogs(ChatFrom chatFrom, int count, int page = 1)
    {
        var check = CheckUserPermission(chatFrom);
        if (!string.IsNullOrEmpty(check.error))
            return R.Error<List<ChatGptLog>>(check.error);

        return R.New(await _logRepository.GetChatLogs(check.user.UserId, chatFrom, count, page));
    }

    #region 日志查看及导出
    
    [HttpGet("log/{logId}")]
    public async Task<ContentResult> GetSingleLogPage(int logId, string sessionId = "")
    {
        var log = _logRepository.GetChatLogById(logId);
        var tpl = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width initial-scale=1'>
<title>查看日志</title>
<link href=""/api/ai/static/css/logs.css"" type=""text/css"" rel=""stylesheet"" />
<link href=""/api/ai/static/js/katex/katex.min.css"" type=""text/css"" rel=""stylesheet"" />
<link href=""/api/ai/static/js/highlight/styles/stackoverflow-light.min.css"" type=""text/css"" rel=""stylesheet"" />
<script src=""/api/ai/static/js/katex/katex.min.js"" type=""application/javascript""></script>
<script src=""/api/ai/static/js/katex/contrib/auto-render.min.js"" type=""application/javascript""></script>
<script src=""/api/ai/static/js/markdown-it.min.js"" type=""application/javascript""></script>
<script src=""/api/ai/static/js/highlight/highlight.min.js"" type=""application/javascript""></script>
<script src=""/api/ai/static/js/logs.js"" type=""application/javascript""></script>
</head>
  <body>
    <div class=""con markdown"">
{0}
    </div>
  </body>
</html>";
        if (log.SessionId == sessionId && !string.IsNullOrEmpty(log.Result))
        {
            return Content(string.Format(tpl, log.Result), "text/html", Encoding.UTF8);
        }

        return Content(string.Format(tpl, "参数错误，或日志内容为空"), "text/html", Encoding.UTF8);
    }

    /// <summary>
    /// 用来导出聊天记录
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet("session/{sessionId}")]
    public async Task<ContentResult> GetChatContextPage(string sessionId, int id = 0)
    {
        var list = await _logRepository.GetChatLogsBySession(sessionId);
        if (list.All(t => t.Id != id))
        {
            return Content("参数错误", "text/plain", Encoding.UTF8);
        }

        var tpl = @"<!DOCTYPE html>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width initial-scale=1'>
<title>查看日志</title>
<link href=""/api/ai/static/css/logs.css"" type=""text/css"" rel=""stylesheet"" />
<link href=""/api/ai/static/js/katex/katex.min.css"" type=""text/css"" rel=""stylesheet"" />
<link href=""/api/ai/static/js/highlight/styles/stackoverflow-light.min.css"" type=""text/css"" rel=""stylesheet"" />
<script src=""/api/ai/static/js/katex/katex.min.js"" type=""application/javascript""></script>
<script src=""/api/ai/static/js/katex/contrib/auto-render.min.js"" type=""application/javascript""></script>
<script src=""/api/ai/static/js/markdown-it.min.js"" type=""application/javascript""></script>
<script src=""/api/ai/static/js/highlight/highlight.min.js"" type=""application/javascript""></script>
<script src=""/api/ai/static/js/logs.js"" type=""application/javascript""></script>
</head>
<body>
<div class=""con"">
<div class=""title"">AI助手</div>
{0}
</div></body>
</html>";
        var sb = new StringBuilder();
        foreach (var log in list)
        {
            if (log.AType == ChatType.文本 || log.AType == ChatType.FunctionCall)
            {
                sb.Append(
                    $"<div class=\"me\"><div class=\"u\">我:</div><div class=\"q markdown\">{log.Question}</div></div>");
                sb.Append(
                    $"<div class=\"gpt\"><div class=\"u\">{log.ChatModelLabel}:</div><div class=\"a markdown\">{log.Result}</div></div>");
            }
        }

        return Content(string.Format(tpl, sb.ToString()), "text/html", Encoding.UTF8);
    }


    [HttpGet("log/{logId}/html/{**fileName}")]
    public async Task<ContentResult> GetHtmlViewInLog(int logId, string fileName, int? htmlIndex, string sessionId = "")
    {
        var log = _logRepository.GetChatLogById(logId);
        if (log.SessionId == sessionId && !string.IsNullOrEmpty(log.Result))
        {
            var regHtml = new Regex("```html(.*?)```", RegexOptions.Singleline);
            var ms = regHtml.Matches(log.Result);
            if (ms.Count > 0)
            {
                if (ms.Count == 1)
                {
                    return Content(ms[0].Groups[1].Value, "text/html", Encoding.UTF8);
                }
                else
                {
                    if (htmlIndex.HasValue && htmlIndex.Value > 0 && htmlIndex.Value <= ms.Count)
                    {
                        return Content(ms[htmlIndex.Value].Groups[1].Value, "text/html", Encoding.UTF8);
                    }
                    else
                    {
                        var tpl = @"<!DOCTYPE html>";
                        tpl +=
                            @"<html lang=""en""><head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1.0""><title>查看日志</title></head><body>";
                        for (var i = 0; i < ms.Count; i++)
                            tpl +=
                                $@"<div class=""link""><a href=""?htmlIndex={i}&sessionId={sessionId}"">HTML 第 {i + 1} 段</a></div>";
                        tpl += @"</body></html>";
                        return Content(tpl, "text/html", Encoding.UTF8);
                    }
                }
            }

            return Content("该日志中不包含HTML内容。", "text/plain", Encoding.UTF8);
        }
        else if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(log.Result))
        {
            var regHtml = new Regex(Regex.Escape(fileName)+"[^\"]*?```(\\w+)(.*?)```", RegexOptions.Singleline);
            var m = regHtml.Match(log.Result);
            if (m.Success)
            {
                var type = m.Groups[1].Value;
                if (type == "html")
                {
                    return Content(m.Groups[2].Value, "text/html", Encoding.UTF8);
                }
                else if (type == "javascript")
                {
                    return Content(m.Groups[2].Value, "text/javascript", Encoding.UTF8);
                }
                else if (type == "css")
                {
                    return Content(m.Groups[2].Value, "text/css", Encoding.UTF8);
                }
            }
        }

        return Content("参数错误，或日志内容为空。", "text/plain", Encoding.UTF8);
    }


    [HttpGet("exportpdf/{sessionId}")]
    public FileContentResult ExportSessionToPdf(string sessionId, int id)
    {
        var url = SiteHost + "api/ai/session/" + sessionId + "?id=" + id;
        var bytes = PdfHelper.GeneratePdfByUrl(url);
        var pdfFileName = "AI聊天记录"+ DateTime.Now.ToString("yyyyMMddHHmmss")+".pdf";
        return new FileContentResult(bytes, "application/pdf") { FileDownloadName = pdfFileName };
    }
    
    #endregion

    /// <summary>
    /// 获取可用模型列表
    /// </summary>
    /// <param name="chatFrom"></param>
    /// <returns></returns>
    [HttpPost("getmodels")]
    public async Task<MstApiResult<List<ChatModelDto>>> GetModels(ChatFrom chatFrom)
    {
        var check = CheckUserPermission(chatFrom);
        if (!string.IsNullOrEmpty(check.error))
            return R.Error<List<ChatModelDto>>(check.error);

        var accountLevel = _logRepository.GetAccountLevel(check.user.FeishuId);
        return R.New(ChatModel.GetMenus(ChatModel.GetUserDefaultModel(check.user.FeishuId, contextCachePrefix),
            false, accountLevel));
    }

    /// <summary>
    /// 设置用户的默认模型，其实可以不需要保存在后端，前端控制每次传这个参数过来也行
    /// </summary>
    /// <param name="chatFrom"></param>
    /// <returns></returns>
    [HttpPost("setdefaultmodel")]
    public async Task<MstApiResult<string>> SetDefaultModel(ChatFrom chatFrom, int chatModel)
    {
        var check = CheckUserPermission(chatFrom);
        if (!string.IsNullOrEmpty(check.error))
            return R.Error<string>(check.error);

        string res;
        var model = ChatModel.GetModel(chatModel);
        if (model == null)
            res = "输入错误，没有找到对应的模型。";
        else
            res = ChatModel.SetDefaultModel(check.user.FeishuId, contextCachePrefix, model.Id);
        return R.New(res);
    }

    /// <summary>
    /// 获取可用提示模板菜单
    /// </summary>
    /// <param name="chatFrom"></param>
    /// <returns></returns>
    [HttpPost("gettemplates")]
    public async Task<MstApiResult<List<PromptTemplate>>> GetPromptTemplates(ChatFrom chatFrom)
    {
        var check = CheckUserPermission(chatFrom);
        if (!string.IsNullOrEmpty(check.error))
            return R.Error<List<PromptTemplate>>(check.error);

        var list = await _logRepository.GetChatPrompts();
        return R.New(list);
    }


    /// <summary>
    /// 向量化接口，调用ApiFactory统一处理
    /// </summary>
    /// <param name="input"></param>
    [HttpPost("embeddings")]
    public async Task<MstApiResult<EmbeddingsDto>> Embeddings([FromBody] WebEmbeddingsInput input)
    {
        var check = CheckUserPermission(input.ChatFrom);
        if (!string.IsNullOrEmpty(check.error))
            return R.Error<EmbeddingsDto>(check.error);

        var _api = _apiFactory.GetService(input.ChatModel);
        var qc = new List<ChatContext.ChatContextContent>();
        bool multi = false;
        if (!string.IsNullOrEmpty(input.Question))
            qc.Add(ChatContext.NewContent(input.Question));
        else if (input.Questions != null && input.Questions.Length > 0)
        {
            foreach (var inputQuestion in input.Questions)
            {
                qc.Add(ChatContext.NewContent(inputQuestion));
            }
            multi = true;
        }
        else
        {
            return R.Error<EmbeddingsDto>("输入参数为空");
        }

        var resp = await _api.ProcessEmbeddings(qc, input.ForQuery);
        if (resp.resultType == ResultType.Error)
            return R.Error<EmbeddingsDto>(resp.error);

        var result = new EmbeddingsDto() { resultType = resp.resultType.ToString(), error = resp.error };
        if (multi)
            result.results = resp.result;
        else
            result.result = resp.result[0];
        return R.New(result);
    }
    

    [HttpPost("GetWssVoiceToTextUrl")]
    public MstApiResult<string> GetWssVoiceToTextUrl()
    {
        var check = CheckUserPermission(ChatFrom.Api);
        if (!string.IsNullOrEmpty(check.error))
            return R.Error<string>(check.error);
        var api = _serviceProvider.GetRequiredService<TencentAudioStreamClient>();
        return R.New(api.GetWssVoiceToTextUrl());
    }

    /// <summary>
    /// 提供websocket连接请求
    /// </summary>
    /// <param name="chatFrom"></param>
    /// <param name="token"></param>
    /// <param name="service">asr: audio stream recgonize</param>
    [Route("socket")]
    public async Task WebSocket(ChatFrom chatFrom, string token, string service = "asr", string provider = "tencent",
        string extraParams = "")
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            var check = CheckUserPermission(chatFrom);
            if (!string.IsNullOrEmpty(check.error))
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
            else
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var server = new AiWebSocketServer(webSocket, _serviceProvider, service, provider);
                await server.ProcessAsync(extraParams);
            }
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }
}