using System.Text.RegularExpressions;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json;

namespace AI_Proxy_Web.Models;

public class ChatContexts
{
    public string? SystemPrompt { get; set; } = $"当前时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n你是一位聪明的个人助理，名字叫易小智。除非用户主动问你，否则不用自我介绍，直接回答用户的问题。\n";
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public List<ChatContext> Contexts { get; set; } = new List<ChatContext>();

    public List<KeyValuePair<string, string>> AgentResults { get; set; } = new List<KeyValuePair<string, string>>();
    
    /// <summary>
    /// 初始化一个空的上下文
    /// </summary>
    /// <returns></returns>
    public static ChatContexts New() => new ChatContexts();

    /// <summary>
    /// 使用多个提问内容初始化一个新的上下文
    /// </summary>
    /// <param name="qc"></param>
    /// <returns></returns>
    public static ChatContexts New(List<ChatContext.ChatContextContent> qc)
    {
        var e = New();
        e.AddQuestions(qc);
        return e;
    }
    /// <summary>
    /// 使用单个问题初始化一个新的上下文
    /// </summary>
    /// <param name="question"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    public static ChatContexts New(string question, ChatType type = ChatType.文本)
    {
        var e = New();
        if(type== ChatType.System)
            e.SystemPrompt += question;
        else
           e.AddQuestion(question, type);
        return e;
    }
    
    /// <summary>
    /// 使用多个问题（如图片+文字）添加到当前上下文的新对话
    /// </summary>
    /// <param name="qc"></param>
    public void AddQuestions(List<ChatContext.ChatContextContent> qc)
    {
        foreach (var q in qc)
        {
            if (q.Type == ChatType.VirtualContexts)
            {
                AddVirtualContexts(q.Content);
            }else if (q.Type == ChatType.System)
            {
                if (string.IsNullOrEmpty(SystemPrompt)|| !SystemPrompt.Contains(q.Content))
                    AddQuestion(q.Content, ChatType.System);
            }else if (q.Type == ChatType.提示模板)
            {
                if (Contexts.Count == 0)
                    AddQuestion(q.Content, ChatType.提示模板);
            }
            else if (q.Type == ChatType.FunctionCall) //如果是function call，把结果赋值给前一个Answer里面的function
            {
                var call = JsonConvert.DeserializeObject<FunctionCall>(q.Content);
                var last = Contexts.LastOrDefault()?.AC.LastOrDefault(t => t.Type == ChatType.FunctionCall);
                if (last != null)
                {
                    var fcs = JsonConvert.DeserializeObject<List<FunctionCall>>(last.Content);
                    var fc = fcs.FirstOrDefault(t => t.Id == call.Id);
                    if (fc != null)
                    {
                        fc.Result = call.Result;
                        last.Content = JsonConvert.SerializeObject(fcs);
                    }
                }
            }
            else
            {
                AddQuestion(q.Content, q.Type, q.MimeType, q.FileName, q.Bytes);
            }
        }
    }
    
    /// <summary>
    /// 使用单个问题添加到当前上下文的新对话
    /// </summary>
    public void AddQuestion(string question, ChatType type = ChatType.文本, string mimeType = "", string fileName = "", byte[] bytes = null)
    {
        if(type== ChatType.System)
            SystemPrompt += question;
        else
        {
            if (Contexts.Count > 0 && Contexts.Last().AC.Count == 0)
                Contexts.Last().QC.Add(ChatContext.NewContent(question, type, mimeType, fileName, bytes));
            else
                Contexts.Add(ChatContext.New(question, type, mimeType, fileName, bytes));
        }
    }

    // 正则表达式匹配问答对，问答内容可以是多行
    private static Regex regex = new Regex(@"Q[:：]\s*(?<question>[\s\S]*?)\nA[:：]\s*(?<answer>[\s\S]*?)(?=\nQ[:：]|\Z)", RegexOptions.Multiline);
    public void AddVirtualContexts(string virtualContexts)
    {
        if (!string.IsNullOrEmpty(virtualContexts))
        {
            foreach (Match match in regex.Matches(virtualContexts))
            {
                AddQuestion(match.Groups["question"].Value.Trim());
                AddAnswer(match.Groups["answer"].Value.Trim());
            }
        }
    }
    
    /// <summary>
    /// 对当前上下文的最后一个问题添加模型回复内容
    /// </summary>
    /// <param name="answer"></param>
    /// <param name="type"></param>
    /// <param name="mimeType"></param>
    /// <param name="fileName"></param>
    public void AddAnswer(string answer, ChatType type = ChatType.文本, string mimeType = "", string fileName = "")
    {
        Contexts.Last().AC.Add(ChatContext.NewContent(answer, type, fileName, mimeType));
    }

    public bool IsEmpty()
    {
        return Contexts.Count == 0;
    }

    public bool HasImage()
    {
        return Contexts.Any(t => t.QC.Any(x => x.Type == ChatType.图片Base64 || x.Type == ChatType.图片Url));
    }
    
    public bool HasFile()
    {
        return Contexts.Any(t =>
            t.QC.Any(x => x.Type == ChatType.文件Url || x.Type == ChatType.文件Bytes));
    }
    
    public bool HasAudio()
    {
        return Contexts.Any(t =>
            t.QC.Any(x => x.Type == ChatType.语音Url || x.Type == ChatType.语音Base64));
    }
    
    /// <summary>
    /// 获取用户缓存的当前上下文
    /// </summary>
    /// <returns></returns>
    public static ChatContexts GetChatContexts(string owner_id, string contextCachePrefix)
    {
        var chatContextCacheKey = $"{owner_id}_{contextCachePrefix}_ai_context";
        var contexts = CacheService.BGet<ChatContexts>(chatContextCacheKey);
        if (contexts == null)
            contexts = ChatContexts.New();
        return contexts;
    }
    
    /// <summary>
    /// 更新用户缓存的当前上下文
    /// </summary>
    public static void SaveChatContexts(string owner_id, string contextCachePrefix, ChatContexts contexts)
    {
        var chatContextCacheKey = $"{owner_id}_{contextCachePrefix}_ai_context";
        CacheService.BSave(chatContextCacheKey, contexts,
            (int)(DateTime.Now.Date.AddDays(1) - DateTime.Now).TotalSeconds);
    }
    
    /// <summary>
    /// 清空当前用户的聊天上下文
    /// </summary>
    public static void ClearChatContexts(string owner_id, string contextCachePrefix)
    {
        SaveChatContexts(owner_id, contextCachePrefix, ChatContexts.New());
    }
}

/// <summary>
/// 对话上下文
/// </summary>
public class ChatContext
{
    public static ChatContext New(List<ChatContext.ChatContextContent> qc, List<ChatContext.ChatContextContent>? ac = null)
    {
        return new ChatContext() { QC = qc, AC = ac ?? new List<ChatContext.ChatContextContent>() };
    }
    
    public static ChatContext New(string question, ChatType type = ChatType.文本, string mimeType = "", string fileName = "", byte[]? bytes = null)
    {
        return New(new List<ChatContextContent>() { ChatContextContent.New(question, type, mimeType, fileName, bytes) });
    }
    
    public static List<ChatContextContent> NewContentList(string question, ChatType type = ChatType.文本, string mimeType = "", string fileName = "", byte[]? bytes = null)
    {
        return new List<ChatContextContent>() { ChatContextContent.New(question, type, mimeType, fileName, bytes) };
    }
    
    public static ChatContextContent NewContent(string question, ChatType type = ChatType.文本, string mimeType = "", string fileName = "", byte[]? bytes = null)
    {
        return ChatContextContent.New(question, type, mimeType, fileName, bytes);
    }
    
    public class ChatContextContent
    {
        public static ChatContextContent New(string content, ChatType type = ChatType.文本, string mimeType = "", string fileName = "", byte[]? bytes = null)
        {
            return new ChatContextContent() { Content = content, Type = type, MimeType = mimeType, FileName = fileName, Bytes = bytes};
        }
        public string Content { get; set; } = string.Empty;
        public ChatType Type { get; set; } = ChatType.None;
        public string MimeType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public byte[]? Bytes { get; set; } = null;
    }
    
    //用户提问内容
    public List<ChatContextContent> QC { get; set; } = new List<ChatContextContent>();
    //模型回复内容
    public List<ChatContextContent> AC { get; set; } = new List<ChatContextContent>();
    
}

public enum ChatType
{
    文本 = 0,
    提示模板 = 1,
    图片Base64 = 2,
    图片Url = 3,
    MultiResult = 4, //将List<Result>序列化保存在上下文的Content中，无法直接反序列化成List对象，需要自己解析JSON
    
    FunctionCall = 5,
    VirtualContexts = 6, //虚拟问答历史，需要使用指定格式
    System = 7,
    Reasoning = 8, //思考过程
    
    图书全文 = 10,
    文件Bytes = 11,
    文件Url = 12,
    语音Base64 = 13,
    语音Url = 14,
    视频Base64 = 15,
    视频Url = 16,
    缓存ID = 17,
    坐标 = 18,
    
    商品搜索参数 = 21,
    阿里万相扩展参数 = 22,
    万能助理参数 = 23,
    None = -1,
}