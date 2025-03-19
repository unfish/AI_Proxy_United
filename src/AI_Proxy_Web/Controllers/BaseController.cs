using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Controllers;

public class BaseController : Controller
{
    #region 用户身份
    protected const string COOKIE_NAME = "admin-token";
    protected string MasterToken { get; set; } //超级权限账号
    protected string CurrentToken()
    {
        var headersToken = HttpContext.Request.Headers["x-access-token"].ToString();
        var cookiesToken = HttpContext.Request.Cookies[COOKIE_NAME];
        var paramsToken = HttpContext.Request.Query["token"].ToString();
        string token = !string.IsNullOrWhiteSpace(headersToken) ? headersToken : cookiesToken;
        token = string.IsNullOrWhiteSpace(token) ? paramsToken : token;
        return token;
    }

    protected virtual int GetUserId(string token)
    {
        if (CurrentToken() == MasterToken)
            return 1;
        
        return 0;
    }

    protected int CurrentUserId()
    {
        if (HttpContext.Items.ContainsKey("CurrentUserId") && HttpContext.Items["CurrentUserId"] != null)
        {
            return (int)HttpContext.Items["CurrentUserId"];
        }

        string token = CurrentToken();
        var userId = GetUserId(token);
        if(userId>0)
            HttpContext.Items["CurrentUserId"] = userId;
        return userId;
    }

    protected string CurrentIP()
    {
        var headerIp = HttpContext.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(headerIp))
            return headerIp.Split(',')[0];
        return HttpContext.Connection.RemoteIpAddress.ToString();
    }
    
    /// <summary>
    /// 给前端用户ID按照不同的token生成不同的ExternalUserId，用来保存和读取聊天上下文
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="token"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    protected string GetExternalUserId(string userId, string token)
    {
        return $"{userId}_{token.Substring(0, 6)}";
    }

    /// <summary>
    /// 检查权限，根据用户Token获取用户的ID及对应的飞书用户ID
    /// </summary>
    /// <param name="chatFrom"></param>
    /// <returns></returns>
    protected virtual (string error, WebUserDto? user) CheckUserPermission(ChatFrom chatFrom)
    {
        var userId = CurrentUserId();
        if (userId == 0)
        {
            return ("权限错误，请联系系统管理员", null);
        }

        return (string.Empty, new() { UserId = userId, Name = "XX", FeishuId = "XXX" });
    }

    #endregion

    protected string GetMimeType(string fileName)
    {
        if (fileName.EndsWith(".jpg") || fileName.EndsWith(".jpeg"))
            return "image/jpeg";
        if (fileName.EndsWith(".png"))
            return "image/png";
        if (fileName.EndsWith(".mp3") || fileName.EndsWith(".m4a"))
            return "audio/mpeg";
        if (fileName.EndsWith(".opus"))
            return "audio/opus";
        if (fileName.EndsWith(".mp4"))
            return "video/mp4";
        return "file";
    }

}