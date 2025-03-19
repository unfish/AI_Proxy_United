namespace AI_Proxy_Web.Database;

/// <summary>
/// 飞书用户账号映射表
/// </summary>
public class FeiShuUserAccessToken
{
    public string app_id { get; set; }
    public string user_id { get; set; }
    public string access_token { get; set; }
    public string refresh_token { get; set; }
    public DateTime access_token_expiredon { get; set; }
    public DateTime refresh_token_expiredon { get; set; }
    public DateTime updatedon { get; set; }
}