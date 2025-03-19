using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Feishu;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Controllers;

[Route("api/ai/feishu")]
public class FeishuController : BaseController
{
    private IFeishuService _feishuService;
    private IServiceProvider _serviceProvider;
    public FeishuController(IFeishuService feishuService, IServiceProvider serviceProvider)
    {
        _feishuService = feishuService;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 飞书机器人通知接口
    /// </summary>
    /// <returns></returns>
    [HttpPost("event")]
    public virtual async Task<JObject> FeiShuEventProcess()
    {
        using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            var body = reader.ReadToEndAsync().Result;
            var obj = JObject.Parse(body);
            if (obj.ContainsKey("challenge"))
            {
                return new JObject() { new JProperty("challenge", obj["challenge"].Value<string>()) };
            }
            else if (obj.ContainsKey("header")) //2.0版本消息
            {
                var event_type = obj["header"]["event_type"].Value<string>();
                return await _feishuService.ProcessEventCallback(event_type, obj);
            }
            else if (obj.ContainsKey("type") && obj["type"].Value<string>() == "event_callback") //1.0版本消息
            {
                var event_type = obj["event"]["type"].Value<string>();
                return await _feishuService.ProcessEventCallback(event_type, obj);
            }
        }

        return new JObject();
    }

    /// <summary>
    /// 获取登录用户授权
    /// </summary>
    [HttpGet("login")]
    public virtual string LoginByCode(string code, string state)
    {
        var res = _feishuService.GetUserAccessTokenByCode(code);
        return res.success ? "授权成功，请重新执行您需要的操作" : res.message;
    }
    
    [HttpPost("TestAskGpt")]
    public async Task<string> TestAskGpt(string question, int model=-1, bool noContext = false)
    {
        var userId = CurrentUserId();
        if (userId != 1)
        {
            return "权限错误";
        }
        var user_id = "642b1e44";
        if (noContext)
        {
            ChatContexts.ClearChatContexts(user_id, "fs");
        }
        await _feishuService.AskGpt(ChatContext.NewContentList(question), user_id, specialModel: model);
        return $"DONE!";
    }
}