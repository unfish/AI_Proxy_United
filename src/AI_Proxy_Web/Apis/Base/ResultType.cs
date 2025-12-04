using AI_Proxy_Web.Apis.V2.Extra;
using AI_Proxy_Web.Functions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.Base;


/// <summary>
/// 返回类型，不同的类型的返回值格式也不一样
/// </summary>
public enum ResultType
{
    Answer, //普通文本响应，问答返回的是增量片断
    AnswerSummation, //文本响应，返回本次结果的累加内容，非增量内容，用于流式语音识别返回完整文本，因为识别内容可能会自动修正
    Translation, //文本响应，返回语音识别实时翻译的文本内容，累加内容非增量，因为翻译结果可能会自动修正
    AnswerStarted, //普通文本响应开始，用来控制事件响应顺序
    AnswerFinished, //普通文本响应结束，只是个事件通知，不返回文字内容，一个过程中多次返回不同结果的时候使用，用来控制事件响应顺序
    Reasoning, //思考模型的思考过程，如果返回结果能区分，就使用这个结果返回思考过程，思考过程的内容在多轮会话中不需要重复提交给模型
    Error, //错误消息
    Waiting, //运行中等待响应，流式接口中可用
    ImageUrl, //返回单张图片
    ImageBytes, //返回单张图片二进制或Base64
    AudioUrl,
    AudioBytes, //音频的二进制或Base64
    VideoUrl,
    VideoBytes,
    FileUrl,
    FileBytes,
    FuncFrontend, //返回需要前端执行的函数，返回FrontFunctionResult
    FuncFrontendMulti, //返回GPT识别到需要执行前端函数的结果，Query调用的时候可能同时返回多个函数，返回FunctionsResult
    FunctionCalls, //GPT返回的原始Function call结果，以上几种是判断类型以后向外传递用的
    FunctionResult, //其它函数调用时返回该类型的信息，可以让GPT继续对结果进行提问，其它类型的信息则直接返回给前端
    FuncStart, //开始执行函数，用于截断消息，或发送提醒
    FuncFinished, //需要多轮对话的复杂函数执行完成消息
    SearchResult, //返回Google搜索结果列表List<SearchResultDto>
    JinaArticle, //Jina.ai的文章采集结果
    MultiMediaResult, //图文混排结果集
    FollowUp, //消息结束后发送可自动提问的气泡消息
    LogSaved, //本次聊天日志已保存，返回日志ID和SessionId，可以用来获取该条日志
    ThoughtSignature, //思维链签名，Gemini专用
}

public class Result
{
    public ResultType resultType { get; set; }
    public static Result New(ResultType type)
    {
        return new Result() {resultType = type};
    }
    public static StringResult New(ResultType type, string result)
    {
        return new StringResult() {resultType = type, result = result};
    }
    public static StringResult Error(string res)
    {
        return new StringResult() {resultType = ResultType.Error, result = res};
    }
    
    public static StringResult Answer(string res)
    {
        return new StringResult() {resultType = ResultType.Answer, result = res};
    }
    
    public static StringResult Reasoning(string res)
    {
        return new StringResult() {resultType = ResultType.Reasoning, result = res};
    }
    
    public static StringResult Waiting(string res)
    {
        return new StringResult() {resultType = ResultType.Waiting, result = res};
    }
    
    public override string ToString()
    {
        if (this is StringResult)
            return ((StringResult) this).result;
        else
            return JsonConvert.SerializeObject(this);
    }
}

public class StringResult : Result
{
    public string result { get; set; }
    
}

public class FileResult : Result
{
    public byte[] result { get; set; }
    public string fileExt { get; set; }
    public string fileName { get; set; }
    public int duration { get; set; }//音频时长
    public string thoughtSignature { get; set; }//Google思考签名
    public static FileResult Answer(byte[] bytes, string ext, ResultType type = ResultType.FileBytes, string fileName = "", int duration = 0,  string thoughtSignature = "")
    {
        return new FileResult() {resultType = type, result = bytes, fileExt = ext, fileName = fileName, duration = duration,  thoughtSignature = thoughtSignature};
    }

    public override string ToString()
    {
        return Convert.ToBase64String(result);
    }
}

public class VideoFileResult : Result
{
    public byte[] result { get; set; }
    public byte[]? cover_image { get; set; }
    public string fileExt { get; set; }
    public string fileName { get; set; }
    public int duration { get; set; }//音频时长
    public static VideoFileResult Answer(byte[] bytes, string ext, string fileName = "", byte[]? cover = null, int duration = 6000)
    {
        return new VideoFileResult() {resultType = ResultType.VideoBytes, result = bytes, duration = duration, fileExt = ext, fileName = fileName, cover_image = cover};
    }

    public override string ToString()
    {
        return Convert.ToBase64String(result);
    }
}

public class FunctionsResult : Result
{
    public List<FunctionCall> result { get; set; }
    public static FunctionsResult Answer(List<FunctionCall> calls, ResultType type=ResultType.FunctionCalls)
    {
        return new FunctionsResult() {resultType = type, result = calls};
    }
    public override string ToString()
    {
        return JsonConvert.SerializeObject(result);
    }
}

public class FrontFunctionResult : Result
{
    public FunctionCall result { get; set; }
    public static FrontFunctionResult Answer(FunctionCall call)
    {
        return new FrontFunctionResult() {resultType = ResultType.FuncFrontend, result = call};
    }
    public override string ToString()
    {
        return JsonConvert.SerializeObject(result);
    }
}

public class FunctionStartResult : Result
{
    public FunctionCall result { get; set; }
    public static FunctionStartResult Answer(FunctionCall call)
    {
        return new FunctionStartResult() {resultType = ResultType.FuncStart, result = call};
    }
    public override string ToString()
    {
        return JsonConvert.SerializeObject(result);
    }
}

public class MultiMediaResult : Result
{
    public List<Result> result { get; set; }
    public static MultiMediaResult Answer(List<Result> results, ResultType type=ResultType.MultiMediaResult)
    {
        return new MultiMediaResult() {resultType = type, result = results};
    }
    public override string ToString()
    {
        return JsonConvert.SerializeObject(result);
    }
}

public class SearchResult : Result
{
    public List<SearchResultDto> result { get; set; }
    public static SearchResult Answer(List<SearchResultDto> list)
    {
        return new SearchResult() {resultType = ResultType.SearchResult, result = list};
    }
    public override string ToString()
    {
        return JsonConvert.SerializeObject(result);
    }
}

public class JinaArticleResult : Result
{
    public JinaArticle result { get; set; }
    public static JinaArticleResult Answer(JinaArticle article)
    {
        return new JinaArticleResult() {resultType = ResultType.JinaArticle, result = article};
    }
    public override string ToString()
    {
        return JsonConvert.SerializeObject(result);
    }
}

public class LogSavedResult : Result
{
    public ChatLog result { get; set; }
    public static LogSavedResult Answer(ChatLog chatLog)
    {
        return new LogSavedResult() {resultType = ResultType.LogSaved, result = chatLog};
    }
    public override string ToString()
    {
        return JsonConvert.SerializeObject(result);
    }
    
    public class ChatLog
    {
        public int Id { get; set; }
        public string SessionId { get; set; }
        public string Content { get; set; }
    }
}

public class FollowUpResult : Result
{
    public string[] result { get; set; }
    public static FollowUpResult Answer(string[] chats)
    {
        return new FollowUpResult() {resultType = ResultType.FollowUp, result = chats};
    }

    public override string ToString()
    {
        return string.Join(";", result);
    }
}