using System.Net;
using System.Text;
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

[Route("api/ai/bookfeishu")]
public class BookFeishuController : FeishuController
{
    private IBookFeishuService _feishuService;
    private IServiceProvider _serviceProvider;
    public BookFeishuController(IBookFeishuService feishuService, IServiceProvider serviceProvider) : base(feishuService, serviceProvider)
    {
        _feishuService = feishuService;
        _serviceProvider = serviceProvider;
    }
    
}