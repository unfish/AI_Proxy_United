
using System.ComponentModel.DataAnnotations.Schema;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AI_Proxy_Web.Database;

public class ChatGptLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int QuestionType { get; set; }
    public ChatFrom ChatFrom { get; set; }
    public int ChatModel { get; set; }
    public string Question { get; set; }
    public string Result { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? FeishuUserId { get; set; }
    /// <summary>
    /// 用来标识一组对话
    /// </summary>
    public string? SessionId { get; set; }
    /// <summary>
    /// 是否是一组里的首次对话
    /// </summary>
    public bool? IsFirstChat { get; set; }
    
    /// <summary>
    /// 问题类型，跟contexts保持一致
    /// </summary>
    public ChatType QType { get; set; }
    /// <summary>
    /// 回复类型，跟contexts保持一致
    /// </summary>
    public ChatType AType { get; set; }
    
    [NotMapped]
    public string? ChatModelLabel { get; set; }
}
