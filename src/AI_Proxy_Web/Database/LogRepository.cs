using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AI_Proxy_Web.Database;

public class LogRepository:ILogRepository
{
    private LogDbContext _dbContext;
    private ConfigHelper _configHelper;
    public LogRepository(LogDbContext context,ConfigHelper configHelper)
    {
        _dbContext = context;
        _configHelper = configHelper;
    }

    #region 聊天相关
    public int AddChatLog(int userId, ChatFrom chatFrom, int chatModel, string question, string result,
        ChatType qType, ChatType aType,
        string feishu_userid = "", string sessionId = "", bool isFirstChat = false)
    {
        if (string.IsNullOrEmpty(question))
            question = "";
        if (string.IsNullOrEmpty(result))
            result = "";
        var log = new ChatGptLog()
        {
            CreatedOn = DateTime.Now,
            UserId = userId,
            Question = question,
            Result = result,
            QuestionType = 0,
            ChatFrom = chatFrom,
            ChatModel = chatModel,
            FeishuUserId = feishu_userid,
            SessionId = sessionId,
            IsFirstChat = isFirstChat,
            QType = qType,
            AType = aType,
        };
        _dbContext.ChatGptLogs.Add(log);
        _dbContext.SaveChanges();
        return log.Id;
    }
    public async Task<List<ChatGptLog>> GetChatLogsBySession(string sessionId)
    {
        var list = await _dbContext.ChatGptLogs.Where(t => t.SessionId==sessionId).OrderBy(t=>t.Id).ToListAsync();
        foreach (var log in list)
        {
            log.ChatModelLabel = ChatModel.GetModel(log.ChatModel)?.Name??"";
        }

        return list;
    }
    
    public int GetChatLogIdBySession(string sessionId, bool asc = true)
    {
        var q = _dbContext.ChatGptLogs.Where(t => t.SessionId == sessionId).OrderBy(t => t.Id).Select(t => t.Id);
            
        if(asc)
            return q.FirstOrDefault();
        else
            return q.LastOrDefault();
    }
    
    public ChatGptLog GetChatLogById(int id)
    {
        var log = _dbContext.ChatGptLogs.Find(id);
        log.ChatModelLabel = ChatModel.GetModel(log.ChatModel)?.Name??"";
        return log;
    }
    
    public async Task<List<ChatGptLog>> GetChatLogs(int userId, ChatFrom chatFrom, int count, int page=1, string sessionId="", bool firstSession=true)
    {
        var q = _dbContext.ChatGptLogs.Where(t =>
            t.UserId == userId && t.ChatFrom == chatFrom);
        List<ChatGptLog> list;
        if (string.IsNullOrEmpty(sessionId))
        {
            if(firstSession)
                list = await q.Where(t => t.IsFirstChat == true).OrderByDescending(t => t.CreatedOn).Skip((page-1)*count).Take(count).ToListAsync();
            else
                list = await q.OrderByDescending(t => t.CreatedOn).Skip((page-1)*count).Take(count).ToListAsync();
        }
        else
        {
            list = await q.Where(t => t.SessionId == sessionId).OrderBy(t => t.CreatedOn).Skip((page-1)*count).Take(count).ToListAsync();
        }
        foreach (var log in list)
        {
            log.ChatModelLabel = ChatModel.GetModel(log.ChatModel)?.Name??"";
        }
        return list;
    }
    
    public async Task<List<PromptTemplate>> GetChatPrompts()
    {
        var list = await _dbContext.ChatGptPrompts.Where(t => !t.Disabled)
            .OrderBy(t => t.SortFeed).ThenBy(t=>t.Id).ToListAsync();
        var dtos = list.Select(t => new PromptTemplate()
        {
            Name = t.Key, Label = t.Name, 
            Content = t.Type == PromptTemplate.PromptType.Tips ? t.Prompt : t.Summary,
            GroupName = t.GroupName, Type = t.Type
        }).ToList();
        return dtos;
    }

    public  ChatGptPrompt? GetPromptByKey(string key)
    {
        return _dbContext.ChatGptPrompts.FirstOrDefault(t => t.Key == key);
    }
    

    #endregion

    #region 账号相关

    public bool AddOrUpdateUserAccessToken(string app_id, string user_id, string access_token, string refresh_token,
        int access_token_expires_in, int refresh_token_expires_in)
    {
        var e = _dbContext.FeiShuUserAccessTokens.FirstOrDefault(t => t.app_id == app_id && t.user_id == user_id);
        if (e == null)
        {
            e = new FeiShuUserAccessToken() {app_id = app_id, user_id = user_id};
            _dbContext.FeiShuUserAccessTokens.Add(e);
        }

        e.access_token = access_token;
        e.refresh_token = refresh_token;
        e.access_token_expiredon = DateTime.Now.AddSeconds(access_token_expires_in - 10);
        e.refresh_token_expiredon = DateTime.Now.AddSeconds(refresh_token_expires_in - 10);
        e.updatedon = DateTime.Now;
        return _dbContext.SaveChanges() > 0;
    }

    public (string access_token, string refresh_token) GetUserAccessToken(string app_id, string user_id)
    {
        var e = _dbContext.FeiShuUserAccessTokens.FirstOrDefault(t => t.app_id == app_id && t.user_id == user_id);
        if (e == null)
        {
            return (string.Empty, string.Empty);
        }

        return (e.access_token_expiredon > DateTime.Now ? e.access_token : string.Empty,
            e.refresh_token_expiredon > DateTime.Now ? e.refresh_token : string.Empty);
    }
    
    public int GetAccountLevel(string user_id)
    {
        return _configHelper.GetConfig<int>("UserLevel:" + user_id);
    }

    public int GetAccountIdByFeishuUserId(string user_id)
    {
        return 1;
    }
    
    public string GenerateTokenByFeishuUserId(string user_id)
    {
        return "xx";
    }
    #endregion

}