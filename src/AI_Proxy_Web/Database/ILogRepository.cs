using AI_Proxy_Web.Models;

namespace AI_Proxy_Web.Database;

public interface ILogRepository
{
    int AddChatLog(int userId, ChatFrom chatFrom, int chatModel, string question, string result, ChatType qType, ChatType aType,
        string feishu_userid = "", string sessionId = "", bool isFirstChat = false);
    Task<List<ChatGptLog>> GetChatLogsBySession(string sessionId);
    ChatGptLog GetChatLogById(int id);
    int GetChatLogIdBySession(string sessionId, bool asc = true);
    Task<List<ChatGptLog>> GetChatLogs(int userId, ChatFrom chatFrom, int count, int page=1, string sessionId="", bool firstSession=true);
    
    Task<List<PromptTemplate>> GetChatPrompts();

    ChatGptPrompt? GetPromptByKey(string key);

    bool AddOrUpdateUserAccessToken(string app_id, string user_id, string access_token, string refresh_token,
        int access_token_expires_in, int refresh_token_expires_in);

    (string access_token, string refresh_token) GetUserAccessToken(string app_id, string user_id);

    int GetAccountLevel(string user_id);
    int GetAccountIdByFeishuUserId(string user_id);

    string GenerateTokenByFeishuUserId(string user_id);
}