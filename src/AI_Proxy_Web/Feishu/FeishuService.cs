using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace AI_Proxy_Web.Feishu;
public interface IFeishuService: IBaseFeishuService
{
    Task AskGpt(List<ChatContext.ChatContextContent> qc,
        string user_id, bool no_function = false, int specialModel = -1);

}

public class ChartData
{
    [JsonProperty("x")]
    public string X { get; set; }
    [JsonProperty("y")]
    public double Y { get; set; }
    [JsonProperty("series")]
    public string Series { get; set; }
}
public class FeishuService: BaseFeishuService, IFeishuService
{

    private IFeishuRestClient _restClient;
    private ILogRepository _logRepository;
    private IServiceProvider _serviceProvider;
    private IHttpClientFactory _httpClientFactory;
    private IApiFactory _apiFactory;
    public FeishuService(IFeishuRestClient restClient, ILogRepository logRepository, IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory, IApiFactory apiFactory, ConfigHelper configHelper):
        base(restClient, logRepository, configHelper, httpClientFactory)
    {
        _restClient = restClient;
        _logRepository = logRepository;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _apiFactory = apiFactory;
        
        AppId = configHelper.GetConfig<string>("FeiShu:Main:AppId");
        AppSecret = configHelper.GetConfig<string>("FeiShu:Main:AppSecret");
        TokenCacheKey = "FeiShuApiToken_DocGpt";
        contextCachePrefix = "fs";
    }

    #region 消息处理逻辑
    protected ChatContexts GetChatContexts(string user_id)
    {
        return ChatContexts.GetChatContexts(user_id, contextCachePrefix);
    }

    #endregion
    
    #region 生成各种卡片消息的消息体
    /// <summary>
    /// 生成模型选择卡片消息
    /// </summary>
    /// <param name="user_id"></param>
    /// <returns></returns>
    public string GetChatModelsCardMessage(string user_id)
    {
        var current = GetUserDefaultModel(user_id);
        var accountLevel = _logRepository.GetAccountLevel(user_id);
        var list = ChatModel.GetMenus(current, level: accountLevel);
        var actions = new List<object>();
        var groups = new List<object>();
        string curTip = "", curRemark="";
        foreach (var type in Enum.GetValuesAsUnderlyingType(typeof(ApiClassTypeEnum)))
        {
            var typeName = ((ApiClassTypeEnum)type).ToString();
            if (!list.Any(t => t.Type == typeName))
                continue;
            foreach (var menu in list.Where(t=>t.Type == typeName))
            {
                actions.Add(new
                {
                    tag = "button",
                    text = new { tag = "plain_text", content = menu.Label },
                    type = menu.Selected ? "primary" : "default",
                    value = new { action = menu.Value.ToString(), type = "menu_to" }
                });
                
                if (menu.Selected)
                {
                    curTip = $"您当前使用的模型是 {menu.Label}。";
                    curRemark = menu.Description;
                }
            }
            groups.Add(new
            {
                tag = "div", text = new{ content = typeName, tag = "lark_md" }
            });
            groups.Add(new
            {
                tag = "action", layout="bisected", actions = actions.ToArray()
            });
            actions.Clear();
        }
        actions.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "模型对比" },
            type = "danger",
            value = new { action = "", type = "menu_to_help" }
        });
        groups.Add(new
        {
            tag = "action", layout="bisected", actions = actions.ToArray()
        });
        groups.Add(new
        {
            tag = "div", text = new{ content = curRemark, tag = "lark_md" }
        });
        groups.Insert(0, new
        {
            tag = "div", text = new{ content = curTip, tag = "lark_md" }
        });
        var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""header"": {
                ""template"": ""green"",
                ""title"": {
                  ""content"": ""选择AI模型"",
                  ""tag"": ""plain_text""
                }
              },
              ""i18n_elements"": {
                ""zh_cn"": "+JsonConvert.SerializeObject(groups) +@"
              }
            }";
        return msg;
    }
    
    private class CardButton
    {
        [JsonProperty("tag")]
        public string Tag { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("text")]
        public ButtonText Text { get; set; }
        [JsonProperty("value")]
        public ButtonValue Value { get; set; }
        public class ButtonText
        {
            [JsonProperty("tag")]
            public string Tag { get; set; }
            [JsonProperty("content")]
            public string Content { get; set; }
        }
        public class ButtonValue
        {
            [JsonProperty("action")]
            public string Action { get; set; }
            [JsonProperty("type")]
            public string Type { get; set; }
        }
    }

    private string GetTrueFalseMark(bool func)
    {
        return func ? "\u2714" : "\u2718";
    }
    public string GetChatModelsFullInfoMessage(string user_id)
    {
        var def = GetUserDefaultModel(user_id);
        var accountLevel = _logRepository.GetAccountLevel(user_id);
        var list = ChatModel.GetFullList(def, level: accountLevel);
        var sb = new StringBuilder();
        sb.AppendLine("名称\t\t|类型\t|图文\t|多图\t|文件\t|Function\t|输入\t|输出\t|");
        foreach (var menu in list)
        {
            var name = menu.Name.Trim(new[] {'*', ' '});
            var tab = name.Length >= 8 || name == "商汤日日新" || name == "GPT4备用" ? "" : "\t";
            sb.AppendLine($"**{name}**{tab}|{menu.Type.ToString()}\t|{GetTrueFalseMark(menu.CanProcessImage)}\t\t|{GetTrueFalseMark(menu.CanProcessMultiImages)}\t\t|{GetTrueFalseMark(menu.CanProcessFile)}\t\t|{GetTrueFalseMark(menu.CanUseFunction)}\t\t|{menu.PriceIn}\t|{menu.PriceOut}\t|");
            sb.AppendLine($"<font color='grey'>{menu.Description}</font>\n");
        }
        var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""header"": {
                ""template"": ""green"",
                ""title"": {
                  ""content"": ""模型功能说明"",
                  ""tag"": ""plain_text""
                }
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": "+ JsonConvert.SerializeObject(sb.ToString().Trim()) +@",
                      ""tag"": ""lark_md""
                    }
                  }
                ]
              }
            }";
        return msg;
    }
    
    /// <summary>
    /// 高级功能卡片
    /// </summary>
    /// <param name="user_id"></param>
    /// <returns></returns>
    public async Task<string> GetAdvanceActionsCardMessage(string user_id)
    {
        var groups = new List<object>();
        var actions = new List<CardButton>();
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "会话创建云文档" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_export_doc" }
        });
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "保存会话断点" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_save_context" }
        });
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "导出会话为PDF" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_export_pdf" }
        });
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "导出会话为TXT" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_export_txt" }
        });
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "生成单条回复链接" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_export_singleurl" }
        });
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "生成完整会话链接" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_export_fullurl" }
        });
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "会话生成思维导图" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_ask_markmap" }
        });
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "帮我想几个问题" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_gettips" }
        });
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "重新发送原文" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_send_astxt" }
        });
        actions.Add(new CardButton()
        {
            Tag = "button",
            Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = "回复转语音发送" },
            Type = "primary",
            Value = new CardButton.ButtonValue(){ Action = "", Type = "menu_send_asvoice" }
        });
        groups.Add(new
        {
            tag = "action", actions = actions.ToArray(), layout="default"
        });
        groups.Add(new
        {
            tag="div", text= new
            {
                content = "导出会话为PDF和TXT只支持文本对话，创建云文档支持图片。保存会话断点功能可以暂存对话记录将来重新加载继续讨论。\n重新发送原文按钮可以把最后一条消息以普通文本消息格式重新发送Markdown原文。因为卡片消息复制出来的时候没有格式，缩进等内容会丢失，要复制消息发送给别人，或者重新发送给AI作为提问的时候可以使用这个功能重新发送一份Markdown格式版用来复制。", tag="lark_md"
            }
        });
        var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""header"": {
                ""template"": ""green"",
                ""title"": {
                  ""content"": ""高级功能"",
                  ""tag"": ""plain_text""
                }
              },
              ""i18n_elements"": {
                ""zh_cn"": " + JsonConvert.SerializeObject(groups) + @"
              }
            }";
        return msg;
    }
    
    /// <summary>
    /// 生成提示模板选择卡片消息
    /// </summary>
    /// <returns></returns>
    public async Task<string> GetPromptTemplatesCardMessage(string user_id)
    {
        var list = await GetChatPrompts();
        var groups = new List<object>();
        var g = "";
        var actions = new List<CardButton>();
        foreach (var menu in list)
        {
            if (g != menu.GroupName)
            {
                if (actions.Count > 0)
                {
                    groups.Add(new
                    {
                        tag="div", text= new
                        {
                            content = g, tag="lark_md"
                        }
                    });
                    groups.Add(new
                    {
                        tag = "action", actions = actions.ToArray(), layout="bisected"
                    });
                    actions.Clear();
                }
                g = menu.GroupName;
            }
            var name = menu.Label;
            actions.Add(new CardButton()
            {
                Tag = "button",
                Text = new CardButton.ButtonText(){ Tag = "plain_text", Content = name },
                Type = "primary",
                Value = new CardButton.ButtonValue(){ Action = menu.Name, Type = "menu_tpl" }
            });
        }
        groups.Add(new
        {
            tag = "action", actions = actions.ToArray()
        });
        groups.Add(new
        {
            tag="div", text= new
            {
                content = "使用系统提供的结构化提示词模板可以有效提升大模型的回复质量。", tag="lark_md"
            }
        });
        var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""header"": {
                ""template"": ""green"",
                ""title"": {
                  ""content"": ""高级提示词模板"",
                  ""tag"": ""plain_text""
                }
              },
              ""i18n_elements"": {
                ""zh_cn"": " + JsonConvert.SerializeObject(groups) + @"
              }
            }";
        return msg;
    }
    
    public string GetExtraOptionsCardMessage(string user_id, int chatModel, List<ExtraOption> options)
    {
        var buttons = new List<object>();
        string styleTip = "";
        var groups = new List<object>();
        foreach (var type in options)
        {
            foreach (var option in type.Contents)
            {
                buttons.Add(new
                {
                    tag = "button",
                    text = new { tag = "plain_text", content = option.Key },
                    type = option.Value==type.CurrentValue?"primary":"default",
                    value = new { action = option.Value, type = $"EO:{chatModel}:{type.Type}" }
                });
                if (option.Value==type.CurrentValue)
                {
                    styleTip = $"您当前选择的{type.Type}是 {option.Key}。";
                }
            }

            groups.Add(new
            {
                tag = "div", text = new { content = styleTip, tag = "lark_md" }
            });
            groups.Add(new
            {
                tag = "action", layout="bisected", actions=buttons.ToArray()
            });
            buttons.Clear();
        }
        var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""header"": {
                ""template"": ""green"",
                ""title"": {
                  ""content"": ""模型选项"",
                  ""tag"": ""plain_text""
                }
              },
              ""i18n_elements"": {
                ""zh_cn"": " + JsonConvert.SerializeObject(groups) + @"
              }
            }";
        return msg;
    }
    
    public string GetMidjourneyActionsCardMessage(string taskId, string[] actions, string tunnel)
    {
        var actionUs = new List<object>();
        var actionReroll = new List<object>();
        var actionVs = new List<object>();
        var actionOthers = new List<object>();
        var actionPans = new List<object>();
        
        foreach (var name in actions)
        {
            var ss = name.Split("::");
            switch (ss[2])
            {
                case "upsample":
                    actionUs.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = $"第 {ss[3]} 张"},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
                case "reroll":
                    actionReroll.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = $"全部重画"},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
                case "variation":
                    actionVs.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = $"第 {ss[3]} 张"},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
                case "high_variation":
                    actionOthers.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = $"超强变体"},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
                case "low_variation":
                    actionOthers.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = $"轻度变体"},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
                case "1":
                    if (ss[1] == "Inpaint") //局部重画，需要待定区域，目前不知道如何传参数，先忽略该按钮
                    {
                    }
                    break;
                case "50":
                    if (ss[1] == "Outpaint")
                        actionOthers.Add(new
                        {
                            tag = "button",
                            text = new {tag = "plain_text", content = $"扩展一倍"},
                            type = "primary",
                            value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                        });
                    break;
                case "75":
                    if (ss[1] == "Outpaint")
                        actionOthers.Add(new
                        {
                            tag = "button",
                            text = new {tag = "plain_text", content = $"扩展一半"},
                            type = "primary",
                            value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                        });
                    break;
                case "100":
                    if (ss[1] == "Outpaint")
                        actionOthers.Add(new
                        {
                            tag = "button",
                            text = new {tag = "plain_text", content = $"扩成方形"},
                            type = "primary",
                            value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                        });
                    break;
                case "pan_left":
                    actionPans.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = $"向左扩展"},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
                case "pan_right":
                    actionPans.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = $"向右扩展"},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
                case "pan_up":
                    actionPans.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = $"向上扩展"},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
                case "pan_down":
                    actionPans.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = $"向下扩展"},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
                default:
                    actionOthers.Add(new
                    {
                        tag = "button",
                        text = new {tag = "plain_text", content = ss[1] == "JOB" ? ss[2] : ss[1]},
                        type = "primary",
                        value = new {action = name, tunnel, taskid = taskId, type = "midj_action"}
                    });
                    break;
            }
        }

        if (actionVs.Count > 0)
        {
            var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""header"": {
                ""template"": ""green"",
                ""title"": {
                  ""content"": ""选择进一步操作"",
                  ""tag"": ""plain_text""
                }
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""对其中一张进行高清放大"",
                      ""tag"": ""lark_md""
                    }
                  },
                  {
                    ""tag"": ""action"", ""layout"":""bisected"",
                    ""actions"": " + JsonConvert.SerializeObject(actionUs) + @"
                  },
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""按其中一张的风格重画4张"",
                      ""tag"": ""lark_md""
                    }
                  },
                  {
                    ""tag"": ""action"", ""layout"":""bisected"",
                    ""actions"": " + JsonConvert.SerializeObject(actionVs) + @"
                  },
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""如果都不满意，可以全部重画"",
                      ""tag"": ""lark_md""
                    }
                  },
                  {
                    ""tag"": ""action"", 
                    ""actions"": " + JsonConvert.SerializeObject(actionReroll) + @"
                  }
                ]
              }
            }";
            return msg;
        }else if (actionOthers.Count > 0)
        {
            var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""header"": {
                ""template"": ""green"",
                ""title"": {
                  ""content"": ""选择进一步操作"",
                  ""tag"": ""plain_text""
                }
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""基于当前图片重画4张"",
                      ""tag"": ""lark_md""
                    }
                  },
                  {
                    ""tag"": ""action"", ""layout"":""bisected"",
                    ""actions"": " + JsonConvert.SerializeObject(actionOthers) + @"
                  },
                  {
                    ""tag"": ""action"", ""layout"":""bisected"",
                    ""actions"": " + JsonConvert.SerializeObject(actionPans) + @"
                  }
                ]
              }
            }";
            return msg;
        }

        return string.Empty;
    }
    
    public string GetAliWanXiangActionCardMessage(string ps)
    {
        var actionSizes = new List<object>();
        actionSizes.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "生成高清放大图片"},
            type = "primary",
            value = new { action = ps, type = "wanx_enlarge" }
        });
        var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""如果对图片满意，可以点击生成该图片的高清放大版："",
                      ""tag"": ""lark_md""
                    }
                  },
                  {
                    ""tag"": ""action"", ""layout"":""bisected"",
                    ""actions"": " + JsonConvert.SerializeObject(actionSizes) + @"
                  }
                ]
              }
            }";
        return msg;
    }
    
    /// <summary>
    /// 生成链接消息操作选择卡片
    /// </summary>
    /// <param name="linkUrl"></param>
    /// <returns></returns>
    public string GetLinkActionCardMessage(string linkUrl, bool withCopy = false)
    {
        var actionSizes = new List<object>();
        if (withCopy)
        {
            actionSizes.Add(new
            {
                tag = "button",
                text = new {tag = "plain_text", content = "搬到云文档"},
                type = "primary",
                value = new {action = linkUrl, type = "link_copytodoc"}
            });
            actionSizes.Add(new
            {
                tag = "button",
                text = new {tag = "plain_text", content = "搬到知识库"},
                type = "primary",
                value = new {action = linkUrl, type = "link_copytowiki"}
            });
        }

        actionSizes.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "总结全文"},
            type = "primary",
            value = new { action = linkUrl, type = "link_summarize" }
        });
        actionSizes.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "页面转PDF"},
            type = "primary",
            value = new { action = linkUrl, type = "link_topdf" }
        });
        actionSizes.Add(new
        {
            tag = "button",
            text = new { tag = "plain_text", content = "页面转截图"},
            type = "primary",
            value = new { action = linkUrl, type = "link_toimage" }
        });
        var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""选择要执行的操作：(总结后可以对文章内容继续提问)"",
                      ""tag"": ""lark_md""
                    }
                  },
                  {
                    ""tag"": ""action"", ""layout"":""bisected"",
                    ""actions"": " + JsonConvert.SerializeObject(actionSizes) + @"
                  }
                ]
              }
            }";
        return msg;
    }
    
    
    
    #endregion
    
    //用户点击选择模型，选择画图样式，和尺寸的卡片按钮的时候，走同步逻辑
    public override bool IsSyncAction(string type)
    {
        var syncActions = new[] {"menu_to", "recover_chatcontext", "clear_chatcontext"};
        return type.StartsWith("EO:") || syncActions.Contains(type);
    }
    public override string GetControllerPath()
    {
        return "feishu";
    }
    public ChatGptPrompt? GetPromptByKey(string key)
    {
        return _logRepository.GetPromptByKey(key);
    }

    public async Task<List<PromptTemplate>> GetChatPrompts()
    {
        return await _logRepository.GetChatPrompts();
    }

    #region 具体模型的处理接口

    /// <summary>
    /// 发送Midjourney继续按钮
    /// </summary>
    /// <param name="result"></param>
    /// <param name="user_id"></param>
    public void SendMidJourneyActions(MidJourneyClient.MidJourneyActions actions, string user_id)
    {
        if (actions != null && actions.Actions.Length > 0)
        {
            var msg = GetMidjourneyActionsCardMessage(actions.TaskId, actions.Actions, actions.Tunnel);
            if (!string.IsNullOrEmpty(msg))
                SendMessage(user_id, msg, FeishuMessageType.Interactive);
        }
    }

    /// <summary>
    /// 创建MidJourney继续处理的任务
    /// </summary>
    /// <param name="taskid"></param>
    /// <param name="customId"></param>
    /// <param name="tunnel"></param>
    /// <param name="user_id"></param>
    public async Task CreateMidJourneyActionTask(string taskid, string customId, string tunnel, string user_id)
    {
        var client = _serviceProvider.GetRequiredService<ApiMidJourney>();
        string msg_id = SendMessage(user_id, "任务处理中，请稍候...");
        await foreach (var res in client.ProcessAction(taskid, customId, tunnel))
        {
            if (res.resultType == ResultType.Waiting)
            {
                if (int.TryParse(res.ToString(), out var times))
                {
                    UpdateText(msg_id, $"任务处理中，请稍候...{times}");
                }
                else
                {
                    UpdateText(msg_id, res.ToString());
                }
            }
            else if (res.resultType == ResultType.Error)
            {
                UpdateText(msg_id, "任务失败：" + res.ToString());
            }
            else if (res.resultType == ResultType.ImageBytes)
            {
                var image_key = UploadImageToFeishu(((FileResult)res).result);
                if (!string.IsNullOrEmpty(image_key))
                {
                    SendMessage(user_id, image_key, FeishuMessageType.Image);
                }
            }
            else if (res.resultType == ResultType.MjActions)
            {
                SendMidJourneyActions(((MidjourneyActionsResult)res).result, user_id);
            }
        }
    }


    /// <summary>
    /// 发送阿里万相继续按钮
    /// </summary>
    /// <param name="result"></param>
    /// <param name="user_id"></param>
    public void SendAliWanXiangActions(string ps, string user_id)
    {
        var msg = GetAliWanXiangActionCardMessage(ps);
        if (!string.IsNullOrEmpty(msg))
            SendMessage(user_id, msg, FeishuMessageType.Interactive);
    }
    
    #endregion

    #region 核心方法，处理模型问答流程

    /// <summary>
    /// 快捷调用
    /// </summary>
    public async Task AskGpt(string text, string user_id, bool no_function = false, int specialModel = -1) {
        await AskGpt(ChatContext.NewContentList(text), user_id, no_function, specialModel);
    }

    /// <summary>
    /// 核心方法，处理GPT应答
    /// </summary>
    /// <param name="question"></param>
    /// <param name="contexts"></param>
    /// <param name="user_id"></param>
    public override async Task AskGpt(List<ChatContext.ChatContextContent> qc, 
        string user_id, bool no_function = false, int specialModel = -1)
    {
        var text = qc.FirstOrDefault(t => t.Type == ChatType.文本)?.Content ?? "";
        if (text.StartsWith("https://yesmro101.feishu.cn/docx/") ||
            text.StartsWith("https://yesmro101.feishu.cn/wiki/"))
        {
            var res = await GetFeiShuDocumentAndSummarize(user_id, text);
            if (!string.IsNullOrEmpty(res.message))
                SendMessage(user_id, res.message);
            return;
        }
        if (text.StartsWith("https://"))
        {
            SendMessage(user_id, GetLinkActionCardMessage(text, false),
                FeishuMessageType.Interactive);
            return;
        }
        
        var chatModel = specialModel >= 0 ? specialModel : GetUserDefaultModel(user_id);
        var accountId = _logRepository.GetAccountIdByFeishuUserId(user_id);
        var input = ApiChatInput.New() with
        {
            ChatFrom = ChatFrom.飞书, ChatModel = chatModel, QuestionContents = qc,
            UserId = accountId, ContextCachePrefix = contextCachePrefix,
            External_UserId = user_id, AudioFormat = "opus", WithFunctions = no_function ? new[] { "NoFunction" } : null
        };

        var sb = new StringBuilder();
        var _api = _apiFactory.GetService(input.ChatModel);
        var sender = new FeishuMessageSender(this, user_id);
        sender.Start();
        await foreach (var res in _api.ProcessChat(input))
        {
            ProcessResponseResult(sender, user_id, res, sb);
        }
        sender.Finish();
        sender.Wait();
    }

    private void ProcessResponseResult(FeishuMessageSender sender, string user_id, Result res, StringBuilder sb)
    {
        if (res.resultType == ResultType.Answer || res.resultType == ResultType.Reasoning)
        {
            sb.Append(res.ToString());
            sender.AddData(sb.ToString());
        }
        else if (res.resultType == ResultType.AnswerStarted)
        {
            sb.Clear();
        }
        else if (res.resultType == ResultType.AnswerFinished)
        {
            sender.Flush(sb.ToString());
            sb.Clear();
        }
        else if (res.resultType == ResultType.Error)
        {
            SendMessage(user_id, res.ToString());
        }
        else if (res.resultType == ResultType.FuncFrontend)
        {
            ProcessFuncFrontendMessage(user_id, res);
        }
        else if (res.resultType == ResultType.FuncStart)
        {
            var call = ((FunctionStartResult)res).result;
            sender.AddData($"调用外部工具: {call.Name}, 参数：{call.Arguments}");
        }
        else if (res.resultType == ResultType.Waiting)
        {
            if (sb.Length > 0)
            {
                sender = new FeishuMessageSender(this, user_id);
                sender.Start();
                sb.Clear();
            }
            if (int.TryParse(((StringResult)res).result, out var times))
            {
                sender.AddData("任务处理中，请稍候..." + times, true);
            }
            else
            {
                sender.AddData(((StringResult)res).result, true);
            }
        }
        else if (res.resultType == ResultType.ImageBytes)
        {
            var image_key = UploadImageToFeishu(((FileResult) res).result);
            if (!string.IsNullOrEmpty(image_key))
            {
                SendMessage(user_id, image_key, FeishuMessageType.Image);
            }
        }
        else if (res.resultType == ResultType.AudioBytes)
        {
            var file = ((FileResult) res).result;
            var file_key = UploadFileToFeishu(file, "audio.opus", ((FileResult) res).duration);
            if (!string.IsNullOrEmpty(file_key))
            {
                SendMessage(user_id, file_key, FeishuMessageType.Audio);
            }
        }
        else if (res.resultType == ResultType.VideoBytes)
        {
            var vs = (VideoFileResult) res;
            var file_key = UploadFileToFeishu(vs.result, vs.fileName, vs.duration);
            if (vs.cover_image == null)
            {
                try
                {
                    vs.cover_image = GetThumbFromVideoFile(vs.result, user_id);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            var image_key = vs.cover_image == null ? null : UploadImageToFeishu(vs.cover_image);
            var jSetting = new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore};
            var text = JsonConvert.SerializeObject(new
            {
                file_key, image_key
            }, jSetting);
            if (!string.IsNullOrEmpty(file_key))
            {
                SendMessage(user_id, text, FeishuMessageType.Media);
            }
        }
        else if (res.resultType == ResultType.FileBytes)
        {
            var file = ((FileResult) res).result;
            var file_key = UploadFileToFeishu(file, ((FileResult) res).fileName);
            if (!string.IsNullOrEmpty(file_key))
            {
                SendMessage(user_id, file_key, FeishuMessageType.File);
            }
        }
        else if (res.resultType == ResultType.MjActions)
        {
            SendMidJourneyActions(((MidjourneyActionsResult)res).result, user_id);
        }
        else if (res.resultType == ResultType.AliWanXiangAuxiliary)
        {
            SendAliWanXiangActions(res.ToString(), user_id);
        }
        else if (res.resultType == ResultType.GoogleSearchResult)
        {
            var list = ((GoogleSearchResult)res).result;
            if (list.Count == 0)
            {
                sb.Append("没有搜索到相关内容");
                return;
            }

            var arts = new StringBuilder();
            foreach (var t in list)
            {
                arts.Append($"<b>{t.title}</b>\n{t.content}\n");
                arts.Append(t.url + "\n");
                arts.Append("\n");
            }

            sb.Append(arts);
            SendMessage(user_id, sb.ToString());
        }
        else if (res.resultType == ResultType.FollowUp)
        {
            var msgs = ((FollowUpResult) res).result;
            sender.SendFollowUp(msgs);
        }
        else if (res.resultType == ResultType.LogSaved)
        {
            var log = ((LogSavedResult)res).result;
            var content = log.Content;
            if (content.Contains("```mermaid"))
                SendMermaidLink(log.Id, log.SessionId, user_id);
            else if (content.Contains("```markmap"))
            {
                SendMarkmapLink(log.Id, log.SessionId, user_id);
            }
            else if (content.Contains("```html"))
            {
                SendHtmlPageLink(log.Id, log.SessionId, user_id);
            }
            else if (content.Contains("<svg "))
            {
                ConvertAndSendSvg(content, user_id);
            }
            else
            {
                var regLatex1 = new Regex(@"\\\[(.*?)\\\]", RegexOptions.Singleline | RegexOptions.Compiled);
                var regLatex2 = new Regex(@"\$(.*?)\$", RegexOptions.Compiled);
                var regLatex3 = new Regex(@"\\\((.*?)\\\)", RegexOptions.Compiled);
                if (regLatex1.IsMatch(content) || regLatex2.IsMatch(content) || regLatex3.IsMatch(content))
                    SendLatexLink(log.Id, log.SessionId, user_id);
            }
        }
    }

    
    private void ProcessFuncFrontendMessage(string user_id, Result res)
    {
        var func = ((FrontFunctionResult) res).result;
        var funcName = func.Name;
        var args = func.Arguments;
        if (funcName == "CreateTask")
        {
            var ar = JObject.Parse(args);
            if (ar["summary"] == null)
            {
                SendMessage(user_id, "创建任务参数错误");
            }
            else
            {
                var endTime = DateTime.Now;
                if (ar["endDate"] != null)
                    DateTime.TryParse(ar["endDate"].Value<string>(), out endTime);
                if (ar["endTime"] != null)
                {
                    if(DateTime.TryParse("2023-01-01 "+ar["endTime"].Value<string>()+":00", out var d))
                    {
                        endTime = new DateTime(endTime.Year, endTime.Month, endTime.Day, d.Hour, d.Minute, 0);
                    }
                }

                CreateUserTask(user_id, ar["summary"].Value<string>(), endTime);
            }
        }
        else if (funcName == "CreateEvent")
        {
            var ar = JObject.Parse(args);
            if (ar["summary"] == null)
            {
                SendMessage(user_id, "创建日程参数错误");
            }
            else
            {
                var startTime = DateTime.Now;
                if (ar["startDate"] != null)
                    DateTime.TryParse(ar["startDate"].Value<string>(), out startTime);
                if (ar["startTime"] != null)
                {
                    if(DateTime.TryParse("2023-01-01 "+ar["startTime"].Value<string>()+":00", out var d))
                    {
                        startTime = new DateTime(startTime.Year, startTime.Month, startTime.Day, d.Hour, d.Minute, 0);
                    }
                }

                CreateUserCalendarEvent(user_id, ar["summary"].Value<string>(), startTime);
            }
        }
        else if (funcName == "ExportContext")
        {
            var ar = JObject.Parse(args);
            if (ar["type"] == null)
            {
                SendMessage(user_id, "导出会话参数错误: " + args);
            }
            else
            {
                switch (ar["type"].Value<string>())
                {
                    case "txt":
                        ExportContextToTxt(0, GetChatContexts(user_id), user_id);
                        break;
                    case "pdf":
                        ExportContextToPdf(GetChatContexts(user_id), user_id).Wait();
                        break;
                    case "云文档":
                        ExportContextToDocument(0, GetChatContexts(user_id), user_id);
                        break;
                    default:
                        SendMessage(user_id, "导出会话参数错误：" + ar["type"].Value<string>());
                        break;
                }
            }
        }
        else if (funcName == "DrawChart")
        {
            var ar = JObject.Parse(args);
            if (ar["data"] == null)
            {
                SendMessage(user_id, "图表参数错误: " + args);
            }
            else
            {
                SendMessage(user_id,
                    GetChartCardMessage(ar["title"]==null?"":ar["title"].Value<string>(), ar["charttype"]==null?"":ar["charttype"].Value<string>(), ar["data"].ToObject<ChartData[]>()), FeishuMessageType.Interactive);
            }
        }
        else if (funcName == "Statistics")
        {
            SendMessage(user_id,
                args +
                "\n[点击打开小程序查看报表](https://applink.feishu.cn/client/mini_program/open?appId=cli_a133823484b8d00e&mode=window-semi&relaunch=true&path=packageC%2Fpages%2Fdashboard-ai%2Fstatistics-ai%3Fargs%3D" +
                HttpUtility.UrlEncode(args)+")", FeishuMessageType.PlainText);
        }
        else
        {
            SendMessage(user_id, funcName+" Not found.");
        }
    }

    public void StartNewContext(string user_id, bool roolUp = true)
    {
        ChatContexts.ClearChatContexts(user_id, contextCachePrefix);
        var chatModel = GetUserDefaultModel(user_id);
        var _api = _apiFactory.GetService(chatModel);
        _api.StartNewContext(user_id);
        SendMessage(user_id, "新会话", FeishuMessageType.Divider, roolUp);
    }

    public override async Task ProcessMenuClickMessage(string user_id, JObject obj)
    {
        var event_key = obj["event"]["event_key"].Value<string>();
        if (event_key == "menu_startnewcontext")
        {
            StartNewContext(user_id);
        }
        else if (event_key == "menu_export_doc")
        {
            ExportContextToDocument(GetUserDefaultModel(user_id), GetChatContexts(user_id),
                user_id);
        }
        else if (event_key == "menu_save_context")
        {
            SaveContextToContinue(GetUserDefaultModel(user_id), GetChatContexts(user_id),
                user_id);
        }
        else if (event_key == "menu_send_astxt")
        {
            SendLastAnswerAsText(user_id);
        }
        else if (event_key == "menu_send_asvoice")
        {
            await SendLastAnswerAsVoice(user_id);
        }
        else if (event_key == "menu_to_all")
        {
            SendMessage(user_id, GetChatModelsCardMessage(user_id),
                FeishuMessageType.Interactive);
        }
        else if (event_key == "menu_to_help")
        {
            SendMessage(user_id, GetChatModelsFullInfoMessage(user_id),
                FeishuMessageType.Interactive);
        }
        else if (event_key == "menu_gettips")
        {
            await GetTips(user_id);
        }
        else if (event_key.StartsWith("menu_to_"))
        {
            var v = int.Parse(event_key.Replace("menu_to_", ""));
            var chatModel = ChatModel.GetModel(v);
            if (chatModel == null)
            {
                SendMessage(user_id, "系统错误，您选择的模型不存在。");
            }
            else
            {
                SetDefaultModel(user_id, chatModel.Id, true);
            }
        }
        else if (event_key == "menu_tpl_all")
        {
            SendMessage(user_id, await GetPromptTemplatesCardMessage(user_id),
                FeishuMessageType.Interactive);
        }
        else if (event_key == "menu_adv_actions")
        {
            SendMessage(user_id, await GetAdvanceActionsCardMessage(user_id),
                FeishuMessageType.Interactive);
        }else if (event_key == "menu_ask_markmap")
        {
            await AskGpt("将以上全部对话内容总结成markdown格式的思维导图（使用一个二级标题和不同等级缩进的无序列表来表示），并以```markmap代码块包裹。注意总结的层级关系，以及各节点之间的逻辑关系，层层递进，先总后分，同一层级节点不要重复和互相包含。节点内容使用陈述性内容，不要使用提问性问题。节点层级不限，请尽量细分到每个话题最细一层的内容，力争实现最完整最全局观的内容总结。\n输出示例：\n```markmap\n## IPD产品开发流程\n- 需求分析\n  - 市场调研\n  - 用户需求收集\n  - 需求分析报告\n- 竞品分析\n  - 竞品识别\n  - 竞品分析报告\n- 产品设计\n  - 功能设计\n  - 交互设计\n  - UI设计\n```\n", user_id);
        }
        else
        {
            var tpl = GetPromptByKey(event_key);
            if (tpl != null)
            {
                await SendPromptTemplate(user_id, tpl);
            }
            else
                SendMessage(user_id, "未识别的模板: " + event_key);
        }
    }
    
    private async Task GetTips(string user_id)
    {
        var contexts = GetChatContexts(user_id);
        if (contexts.IsEmpty())
        {
            SendMessage(user_id, "在聊天过程中如果一时不知道应该继续问什么问题，可以使用该功能获取三个问题提示。请不要在会话开始前使用该功能。");
            return;
        }
        string msg_id = SendMessage(user_id, "请稍候...");
        var question = "[Q]请针对上面对话内容，帮我设计3个可以继续用来提问的问题，用来对该话题做更深度的学习和挖掘。问题要有深度，有专业性。";
        question += "\n请以JSON格式返回，返回示例：[{\"question\":\"xxx\"},{\"question\":\"xxx\"}]";
        var chatModel = GetUserDefaultModel(user_id);
        var accountId = _logRepository.GetAccountIdByFeishuUserId(user_id);
        var input = ApiChatInput.New() with
        {
            ChatFrom = ChatFrom.飞书, ChatModel = chatModel,
            UserId = accountId, QuestionContents = ChatContext.NewContentList(question),
            External_UserId = user_id, IgnoreAutoContexts = true, ContextCachePrefix = contextCachePrefix
        };

        var sb = new StringBuilder();
        var _api = _apiFactory.GetService(input.ChatModel);
        await foreach (var res in _api.ProcessChat(input))
        {
            if (res.resultType == ResultType.Answer)
                sb.Append(res.ToString());
        }

        var summary = sb.ToString();
        var jsonIndex = summary.IndexOf("```json", StringComparison.Ordinal);
        if (jsonIndex >= 0)
        {
            summary = summary.Remove(jsonIndex, "```json".Length + 1);
            jsonIndex = summary.IndexOf("```", StringComparison.Ordinal);
            if(jsonIndex>0)
                summary = summary.Remove(jsonIndex, summary.Length - jsonIndex);
        }

        var arr = JArray.Parse(summary);
        var groups = new List<string>();
        var index = 1;
        foreach (var tk in arr)
        {
            if (tk["question"] != null && !string.IsNullOrEmpty(tk["question"].Value<string>()))
            {
                string q = tk["question"].Value<string>();
                var button = JsonConvert.SerializeObject(new
                {
                    tag = "action", layout = "default", actions = new[]
                    {
                        new
                        {
                            tag = "button",
                            text = new {tag = "plain_text", content = $"问题 {index} :"},
                            type = "primary",
                            value = new {action = q, type = "ask_question"}
                        }
                    }
                });
                var text = JsonConvert.SerializeObject(new
                {
                    tag = "div", text = new {content = q, tag = "lark_md"}
                });
                groups.Add(button);
                groups.Add(text);
                index++;
            }
        }

        if (groups.Count > 0)
        {
            var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                  {
                    ""tag"": ""div"",
                    ""text"": {
                      ""content"": ""以下是您可能感兴趣的问题，点击对应的按钮即可发起提问："",
                      ""tag"": ""lark_md""
                    }
                  }," + string.Join(",", groups) + @"
                ]
              }
            }";
            SendOrUpdateMessage(msg_id, user_id, msg, FeishuMessageType.Interactive);
        }
        else
        {
            SendOrUpdateMessage(msg_id, user_id, "问题选项生成失败，请重试，或者直接提问。");
        }
    }

    public override async Task<JObject> ProcessCardActionMessage(string user_id, JObject obj)
    {
        var value = obj["event"]["action"]["value"];
        var type = value["type"] != null ? value["type"].Value<string>() : "";
        var menu = value["action"] != null ? value["action"].Value<string>() : "";
        if (type == "menu_to") //切换模型
        {
            var chatModel = ChatModel.GetModel(int.Parse(menu));
            if (chatModel == null)
            {
                SendMessage(user_id, "系统错误，您选择的模型不存在。");
            }
            else
            {
                SetDefaultModel(user_id, chatModel.Id, false);
            }

            return GetCardActionSuccessMessage(GetChatModelsCardMessage(user_id));
        }
        else if (type == "menu_to_help")
        {
            SendMessage(user_id, GetChatModelsFullInfoMessage(user_id), FeishuMessageType.Interactive);
        }
        else if (type == "menu_tpl")
        {
            var tpl = GetPromptByKey(menu);
            if (tpl != null)
            {
                await SendPromptTemplate(user_id, tpl);
            }
            else
                SendMessage(user_id, "未识别的模板: " + menu);
        }
        else if (type == "menu_export_txt")
        {
            ExportContextToTxt(GetUserDefaultModel(user_id), GetChatContexts(user_id), user_id);
        }
        else if (type == "menu_export_doc")
        {
            ExportContextToDocument(GetUserDefaultModel(user_id), GetChatContexts(user_id),
                user_id);
        }
        else if (type == "menu_save_context")
        {
            SaveContextToContinue(GetUserDefaultModel(user_id), GetChatContexts(user_id),
                user_id);
        }
        else if (type == "menu_send_astxt")
        {
            SendLastAnswerAsText(user_id);
        }
        else if (type == "menu_send_asvoice")
        {
            await SendLastAnswerAsVoice(user_id);
        }
        else if (type == "menu_export_pdf")
        {
            var contexts = GetChatContexts(user_id);
            if (contexts.IsEmpty())
            {
                SendMessage(user_id, "当前会话中没有内容。");
            }
            else
            {
                var logId = _logRepository.GetChatLogIdBySession(contexts.SessionId);
                await ExportContextToPdf(GetUserDefaultModel(user_id), user_id,
                    contexts.SessionId, logId);
            }
        }
        else if (type == "menu_export_singleurl")
        {
            var contexts = GetChatContexts(user_id);
            if (contexts.IsEmpty())
            {
                SendMessage(user_id, "当前会话中没有内容。");
            }
            else
            {
                var logId = _logRepository.GetChatLogIdBySession(contexts.SessionId, false);
                var url = $"该条回复的查看链接：{SiteHost}api/ai/log/{logId}?sessionId={contexts.SessionId}";
                SendMessage(user_id, url, FeishuMessageType.PlainText);
            }
        }
        else if (type == "menu_export_fullurl")
        {
            var contexts = GetChatContexts(user_id);
            if (contexts.IsEmpty())
            {
                SendMessage(user_id, "当前会话中没有内容。");
            }
            else
            {
                var logId = _logRepository.GetChatLogIdBySession(contexts.SessionId);
                var url = $"本轮完整对话的查看链接：{SiteHost}api/ai/session/{contexts.SessionId}?id={logId}";
                SendMessage(user_id, url, FeishuMessageType.PlainText);
            }
        }
        else if (type == "ask_question")
        {
            SendMessage(user_id, $"提问：{menu}");
            await AskGpt(menu, user_id);
        }
        else if (type == "menu_ask_markmap")
        {
            await AskGpt("将以上全部对话内容总结成markdown格式的思维导图（使用一个二级标题和不同等级缩进的无序列表来表示），并以```markmap代码块包裹。注意总结的层级关系，以及各节点之间的逻辑关系，层层递进，先总后分，同一层级节点不要重复和互相包含。节点内容使用陈述性内容，不要使用提问性问题。节点层级不限，请尽量细分到每个话题最细一层的内容，力争实现最完整最全局观的内容总结。\n输出示例：\n```markmap\n## IPD产品开发流程\n- 需求分析\n  - 市场调研\n  - 用户需求收集\n  - 需求分析报告\n- 竞品分析\n  - 竞品识别\n  - 竞品分析报告\n- 产品设计\n  - 功能设计\n  - 交互设计\n  - UI设计\n```\n",  user_id);
        }
        else if (type == "menu_gettips")
        {
            await GetTips(user_id);
        }
        else if (type == "auto_continue")
        {
            await AskGpt(menu, user_id);
        }
        else if (type == "wanx_enlarge")
        {
            await AskGpt(ChatContext.NewContentList(menu, ChatType.阿里万相扩展参数), user_id,
                specialModel: (int)M.万相海报);
        }
        else if (type.StartsWith("EO:"))
        {
            var ss = type.Split(':');
            var model = int.Parse(ss[1]);
            var styleType = ss[2];
            var api = _apiFactory.GetService(model);
            api.SetExtraOptions(user_id, styleType, menu);
            return GetCardActionSuccessMessage(GetExtraOptionsCardMessage(user_id, model, api.GetExtraOptions(user_id)));
        }
        else if (type == "midj_action")
        {
            var taskid = value["taskid"] != null ? value["taskid"].Value<string>() : "";
            if (!string.IsNullOrEmpty(taskid) && !string.IsNullOrEmpty(menu))
            {
                var tunnel = value["tunnel"] == null ? "NORMAL" : value["tunnel"].Value<string>();
                await CreateMidJourneyActionTask(taskid, menu, tunnel, user_id);
            }
        }
        else if (type == "link_copytodoc")
        {
            await GetUrlContentAndCreateDocument(user_id, menu);
        }
        else if (type == "link_copytowiki")
        {
            await GetUrlContentAndCreateDocument(user_id, menu, "wiki");
        }
        else if (type == "link_summarize")
        {
            await GetUrlContentAndSummarize(user_id, menu);
        }
        else if (type == "link_topdf")
        {
            await ExportContextToPdf(user_id, menu);
        }
        else if (type == "link_toimage")
        {
            await ExportContextToPdf(user_id, menu, true);
        }
        else if (type == "mergemsg_copytodoc")
        {
            await GetMergeForwardMessageCreateDocument(user_id, menu, "wiki");
        }
        else if (type == "mergemsg_summarize")
        {
            await GetMergeForwardMessageSummarize(user_id, menu);
        }
        else if (type == "recover_chatcontext")
        {
            var result = SetContextToContinue(user_id, menu);
            if (result)
                return GetCardActionSuccessMessage();
            else
            {
                return JObject.Parse(@"{""toast"":{
                    ""type"":""success"",
                    ""content"":""恢复失败，上下文已被删除""
                }}");
            }
        }
        else if (type == "clear_chatcontext")
        {
            SetContextToCleared(user_id, menu);
            return JObject.Parse(@"{""toast"":{
                ""type"":""success"",
                ""content"":""删除成功""
            },""card"": {""type"": ""raw"", ""data"": " + GetChatContextClearedActionCardMessage() + "}}");
        }

        return new JObject();
    }

    public async Task GetMergeForwardMessageSummarize(string user_id, string msg_id)
    {
        var (success, title, blocks) = await GetMergeForwardMessageContent(msg_id);
        if (success)
        {
            var sb = new StringBuilder();
            sb.Append(
                "请对以下多人对话内容进行总结，提取其中每个人的关键的论点论据，输出一篇简洁有条理的文章摘要，但不要遗漏重点信息，如果有待办事项，请在最后单独列出。总结内容控制在500字以内：\n'''\n");
            foreach (var block in blocks)
            {
                if (block.Type != DocumentBlock.BlockType.Image && block.Type != DocumentBlock.BlockType.ImageB64)
                {
                    sb.Append(block.Content + "\n\n");
                }
            }

            sb.Append("'''");
            await AskGpt(sb.ToString(), user_id, no_function: true);
        }
        else
        {
            SendMessage(user_id, "获取消息内容出错。");
        }
    }
    
    
    public void SetDefaultModel(string user_id, int chatModel, bool sendMsg)
    {
        ChatContexts.ClearChatContexts(user_id, contextCachePrefix);
        var tips = ChatModel.SetDefaultModel(user_id, contextCachePrefix, chatModel);
        if(sendMsg)
            SendMessage(user_id, tips);
        var api = _apiFactory.GetService(chatModel);
        var options = api.GetExtraOptions(user_id);
        if (options != null && options.Count > 0)
        {
            SendMessage(user_id, GetExtraOptionsCardMessage(user_id, chatModel, options),
                FeishuMessageType.Interactive);
        }
    }

    protected async Task SendPromptTemplate(string user_id, ChatGptPrompt tpl)
    {
        var chatModel = GetUserDefaultModel(user_id);
        if (!ChatModel.IsTextModel(chatModel))
        {
            SendMessage(user_id, "只有语言模型可以使用系统提示词模板，请先切换到一个语言模型。");
            return;
        }
        if (tpl.Type == PromptTemplate.PromptType.Prompt)
        {
            ChatContexts.SaveChatContexts(user_id, contextCachePrefix, ChatContexts.New(tpl.Prompt, ChatType.System));
            SendMessage(user_id, tpl.Summary);
        }
        else if (tpl.Type == PromptTemplate.PromptType.Tips)
        {
            SendMessage(user_id, tpl.Prompt);
        }
    }

    public override async Task ProcessChatEnteredEvent(string user_id)
    {
        var chatModel = GetUserDefaultModel(user_id);
        if (ChatModel.IsTextModel(chatModel) && chatModel<100)
        {
            var contexts = GetChatContexts(user_id);
            if (contexts.IsEmpty())
            {
                var defaultPrompt = $"今天是{DateTime.Now:yyyy年MM月dd日}，请给我输出一句中英文对照的中外名人名言或古代谚语，只返回原文，不要解释其含义。";
                await AskGpt(defaultPrompt, user_id);
                StartNewContext(user_id, false);
            }
        }
    }
    
    private void SendLastAnswerAsText(string user_id)
    {
        var ctx = GetChatContexts(user_id);
        if (ctx.Contexts.Count > 0 && ctx.Contexts.Last().AC.Any(t => t.Type == ChatType.文本))
        {
            SendMessage(user_id, ctx.Contexts.Last().AC.Last(t => t.Type == ChatType.文本).Content,
                FeishuMessageType.PlainText);
        }
        else
        {
            SendMessage(user_id, "没有找到最后一条回复内容。");
        }
    }

    private async Task SendLastAnswerAsVoice(string user_id)
    {
        var ctx = GetChatContexts(user_id);
        if (ctx.Contexts.Count > 0 && ctx.Contexts.Last().AC.Any(t => t.Type == ChatType.文本))
        {
            var api = _apiFactory.GetService(M.语音服务);
            var resp = await api.ProcessQuery(ApiChatInput.New() with
            {
                QuestionContents =
                ChatContext.NewContentList(ctx.Contexts.Last().AC.Last(t => t.Type == ChatType.文本).Content),
                AudioFormat = "opus", IgnoreAutoContexts = true
            });
            if (resp.resultType == ResultType.AudioBytes)
            {
                var res = (FileResult)resp;
                var file_key = UploadFileToFeishu(res.result, "语音.opus", res.duration);
                SendMessage(user_id, file_key, FeishuMessageType.Audio);
            }
            else
            {
                Console.WriteLine(JsonConvert.SerializeObject(resp));
            }
        }
        else
        {
            SendMessage(user_id, "没有找到最后一条回复内容。");
        }
    }

    #endregion
    
    public void ConvertAndSendSvg(string svgContent, string user_id)
    {
        var index = svgContent.IndexOf("<svg ", StringComparison.Ordinal);

        while (index >= 0)
        {
            svgContent = svgContent.Substring(index);
            index = svgContent.IndexOf("</svg>", StringComparison.Ordinal);
            if (index > 0)
            {
                var sub = svgContent.Substring(0, index + "</svg>".Length);
                var bytes = ImageHelper.ConvertSvgToPng(sub);
                if (bytes != null)
                {
                    var fileId = UploadImageToFeishu(bytes);
                    SendMessage(user_id, fileId, FeishuMessageType.Image);
                }
                svgContent = svgContent.Substring(index + "</svg>".Length);
            }
            else
            {
                break;
            }
            index = svgContent.IndexOf("<svg ", StringComparison.Ordinal);
        }
    }

    #region 抓取公众号文章

    public async Task<(bool success, string title, List<DocumentBlock>? blocks)> GetWxPublicUrlContent(string url)
    {
        var client = _httpClientFactory.CreateClient();
        var html = client.GetStringAsync(url).Result;
        HtmlDocument doc = new HtmlDocument(); //使用Html Agility Pack提取HTML内容
        doc.LoadHtml(html);
        string title= doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim() ?? "";
        string content = doc.DocumentNode.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' rich_media_content ')]")?.InnerHtml ?? "";
        List<DocumentBlock> blocks = new List<DocumentBlock>();
        blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.Text, Content = $"原文链接：{url}"});
        if (!string.IsNullOrEmpty(content))
        {
            content = content.Replace("\n", "");
            content = Regex.Replace(content, "<img (.*?) data-src=\"(.*?)\"(.*?)>", "\n\nimage:$2\n\n");
            
            GetBlocksFromContent(content, blocks);

            return (true, title, blocks);
        }

        return (false, "正则解析内容错误", null);
    }
    
    
    public async Task<(bool success, string title, List<DocumentBlock>? blocks)> GetWoShiPMUrlContent(string url)
    {
        var client = _httpClientFactory.CreateClient();
        var html = client.GetStringAsync(url).Result;
        HtmlDocument doc = new HtmlDocument(); //使用Html Agility Pack提取HTML内容
        doc.LoadHtml(html);
        string title= doc.DocumentNode.SelectSingleNode("//h2")?.InnerText?.Trim() ?? "";
        string content = doc.DocumentNode.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' article--content ')]")?.InnerHtml ?? "";
        List<DocumentBlock> blocks = new List<DocumentBlock>();
        blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.Text, Content = $"原文链接：{url}"});
        if (!string.IsNullOrEmpty(content))
        {
            content = content.Replace("\n", "");
            content = Regex.Replace(content, "<img (.*?) src=\"(.*?)\"(.*?)>", "\n\nimage:$2\n\n");
            
            GetBlocksFromContent(content, blocks);

            return (true, title, blocks);
        }

        return (false, "正则解析内容错误", null);
    }

    public async Task<(bool success, string title, List<DocumentBlock>? blocks)> GetZhiHuUrlContent(string url)
    {
        if (!(url.StartsWith("https://zhuanlan.zhihu.com/p/") ||
              (url.Contains("/question/") && url.Contains("/answer/"))))
        {
            return (false, "只支持专栏和普通问答页面链接，问答页面请在想要搬家的回答下方的『编辑于XXX』或『发布于XXX』的时间上右键复制链接。", null);
        }

        var client = _httpClientFactory.CreateClient();
        var html = client.GetStringAsync(url).Result;
        HtmlDocument doc = new HtmlDocument(); //使用Html Agility Pack提取HTML内容
        doc.LoadHtml(html);
        string title= doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "";
        if (title.EndsWith("知乎"))
            title = title.Split(" - ")[0];
        string content = doc.DocumentNode.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' Post-RichText ')]")?.InnerHtml ?? "";
        List<DocumentBlock> blocks = new List<DocumentBlock>();
        blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.Text, Content = $"原文链接：{url}"});
        if (!string.IsNullOrEmpty(content))
        {
            content = content.Replace("\n", "");
            content = Regex.Replace(content, "<img src=\"data:(.*?)\"(.*?) data-original=\"(.*?)\"(.*?)>", "\n\nimage:$3\n\n");
            
            GetBlocksFromContent(content, blocks);

            return (true, title, blocks);
        }

        return (false, "正则解析内容错误", null);
    }

    private static void GetBlocksFromContent(string sb, List<DocumentBlock> blocks)
    {
        sb = sb.Replace("</li>", "_LI_");
        sb = sb.Replace("</p>", "\n\n");
        sb = sb.Replace("</ol>", "\n\n_EOL_");
        sb = sb.Replace("</ul>", "\n\n_EUL_");
        sb = sb.Replace("<strong>", "**");
        sb = sb.Replace("</strong>", "** ");
        sb = sb.Replace("<b>", "**");
        sb = sb.Replace("</b>", "** ");
        sb = Regex.Replace(sb, "<br(.*?)>", "\n");
        sb = Regex.Replace(sb, "<ol(.*?)>", "\n\n_OL_:");
        sb = Regex.Replace(sb, "<ul(.*?)>", "\n\n_UL_:");
        sb = Regex.Replace(sb, "<h(\\d+)>", "\n\n_HH_:");
        sb = Regex.Replace(sb, "</h(\\d+)>", "\n\n");
        var htmlReg = new Regex("<(.*?)>", RegexOptions.Singleline);
        sb = htmlReg.Replace(sb, "").TrimStart();
        
        var index = sb.IndexOf("\n\n");
        var textPara = new StringBuilder();
        while (index > -1)
        {
            var para = sb.Substring(0, index + 2);
            sb = sb.Substring(index + 2).TrimStart();
            if (para.StartsWith("image:") || para.StartsWith("_OL_:") || para.StartsWith("_UL_:") || para.StartsWith("_HH_:"))
            {
                if (textPara.Length > 0)
                {
                    blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.Text, Content = textPara.ToString()});
                    textPara.Clear();
                }

                if (para.StartsWith("image:"))
                    blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.Image, Content = para.Substring(6)});
                else if (para.StartsWith("_HH_:"))
                    blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.H4, Content = para.Substring(5)});
                else if (para.StartsWith("_OL_:")||para.StartsWith("_UL_:"))
                {
                    var endStr = para.StartsWith("_OL_:") ? "_EOL_" : "_EUL_";
                    var type = para.StartsWith("_OL_:") ? DocumentBlock.BlockType.OL : DocumentBlock.BlockType.UL;
                    var endIndex = sb.IndexOf(endStr);
                    if (endIndex >= 0)
                    {
                        para += sb.Substring(0, endIndex);
                        sb = sb.Substring(endIndex + 5);
                    }

                    var ss = para.Substring(5).Split(new[]{"_LI_","\n"}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var s in ss.Where(t=>t.StartsWith("image:")))
                    {
                        blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.Image, Content = s.Substring(6)});
                    }
                    blocks.Add(new DocumentBlock()
                        {Type = type, Content = string.Join("\n", ss.Where(t=>!t.StartsWith("image:")))});
                }
            }
            else if (para.Trim().Length > 0)
            {
                textPara.Append(para.Trim() + "\n");
            }

            index = sb.IndexOf("\n\n");
        }

        if (textPara.Length > 0)
            blocks.Add(new DocumentBlock() {Type = DocumentBlock.BlockType.Text, Content = textPara.ToString()});
    }

    public async Task<(bool success, string message)> GetUrlContentAndCreateDocument(string user_id, string url, string type="doc")
    {
        bool success;
        string title;
        List<DocumentBlock>? blocks;
        if (url.StartsWith("https://mp.weixin.qq.com/s/"))
        {
            (success, title, blocks) = await GetWxPublicUrlContent(url);
        }
        else if(url.StartsWith("https://www.woshipm.com/"))
        {
            (success, title, blocks) = await GetWoShiPMUrlContent(url);
        }else if (url.StartsWith("https://www.zhihu.com/question/")||url.StartsWith("https://zhuanlan.zhihu.com/p/"))
        {
            (success, title, blocks) = await GetZhiHuUrlContent(url);
        }
        else
        {
            return (false, "仅支持微信公众号、人人都是产品经理、知乎文章链接");
        }
        SendMessage(user_id, "处理中，请稍候...");
        if (success)
        {
            var doc_id = CreateUserDocument(user_id, title, blocks);
            if (!string.IsNullOrEmpty(doc_id))
            {
                if (type == "wiki")
                {
                    var node = "S1WfwdsfditPmnkYcLAcVIfInib"; // https://yesmro101.feishu.cn/wiki/S1WfwdsfditPmnkYcLAcVIfInib
                    var mr = await MoveDocToWiki(user_id, doc_id, node);
                    if (mr.success)
                    {
                        SendMessage(user_id, $"文档已创建成功。https://yesmro101.feishu.cn/wiki/{mr.message}", FeishuMessageType.PlainText);
                    }
                    else
                    {
                        SendMessage(user_id, mr.message);
                    }
                }
                else
                {
                    SendMessage(user_id, $"文档已创建成功。https://yesmro101.feishu.cn/docx/{doc_id}", FeishuMessageType.PlainText);
                }
            }

            return (!string.IsNullOrEmpty(doc_id), string.Empty);
        }
        else
        {
            SendMessage(user_id, title);
            return (success, title);
        }
    }
    
    private async Task<(bool success, string message)> MoveDocToWiki(string user_id, string doc_id, string wiki_node)
    {
        var token = GetUserAccessToken(user_id);
        var request = new RestRequest($"open-apis/wiki/v2/spaces/7047301435226062850/nodes/move_docs_to_wiki", Method.Post);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        request.AddJsonBody(new
        {
            parent_wiki_token= wiki_node,
            obj_type= "docx",
            obj_token= doc_id
        });
        var response = _restClient.GetClient().Execute(request, Method.Post);
        var o = JObject.Parse(response.Content);
        if (o["code"].Value<int>() == 0)
        {
            if (o["data"]["wiki_token"] != null)
            {
                return (true, o["data"]["wiki_token"].Value<string>());
            }
            else if (o["data"]["task_id"] != null)
            {
                var task_id = o["data"]["task_id"].Value<string>();
                for(var i=0;i<5;i++)
                {
                    Thread.Sleep(1000);
                    var res = await CheckMoveDocTask(token, task_id);
                    if (res.success)
                        return res;
                }

                return (false, "移动任务超时未成功，请手动确认结果。");
            }else
                return (false, o["msg"].Value<string>());
        }
        else
        {
            return (false, "移动到知识库失败，"+o["msg"].Value<string>());
        }
    }

    private async Task<(bool success, string message)> CheckMoveDocTask(string token, string task_id)
    {
        var request = new RestRequest($"open-apis/wiki/v2/tasks/{task_id}?task_type=move", Method.Get);
        request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
        var response = _restClient.GetClient().Execute(request, Method.Get);
        var o = JObject.Parse(response.Content);
        if (o["code"].Value<int>() == 0)
        {
            if (o["data"]["task"]["move_result"] != null && (o["data"]["task"]["move_result"] as JArray).Count>0)
            {
                var t = (o["data"]["task"]["move_result"] as JArray)[0];
                return (true, t["node"]["node_token"].Value<string>());
            }
        }
        return (false, o["msg"].Value<string>());
    }
    
    public async Task<(bool success, string message)> GetUrlContentAndSummarize(string user_id, string url)
    {
        bool success = false;
        string? title = null;
        List<DocumentBlock>? blocks = null;
        if (url.StartsWith("https://mp.weixin.qq.com/s/"))
        {
            (success, title, blocks) = await GetWxPublicUrlContent(url);
        }
        else if(url.StartsWith("https://www.woshipm.com/"))
        {
            (success, title, blocks) = await GetWoShiPMUrlContent(url);
        }
        else if (url.StartsWith("https://www.zhihu.com/question/")||url.StartsWith("https://zhuanlan.zhihu.com/p/"))
        {
            (success, title, blocks) = await GetZhiHuUrlContent(url);
        }
        else
        {
            var api2 = _apiFactory.GetService(DI.GetApiClassAttributeId(typeof(ApiJinaAi)));
            var res2 = await api2.ProcessQuery(ApiChatInput.New() with
            {
                QuestionContents = ChatContext.NewContentList(url), IgnoreAutoContexts = true
            });

            if (res2.resultType == ResultType.JinaArticle)
            {
                var con = ((JinaArticleResult) res2).result;
                success = true;
                title = con.Title;
                blocks = new List<DocumentBlock>()
                {
                    new DocumentBlock()
                    {
                        Type = DocumentBlock.BlockType.Text, Content = con.Content
                    }
                };
            }
        }
        if (success)
        {
            var sb = new StringBuilder();
            sb.Append("请对以下文章内容进行总结，提取其中关键的论点论据，输出一篇简洁有条理的文章摘要，但不要遗漏重点信息，控制在500字以内：\n'''\n");
            sb.Append($"标题：{title}\n");
            foreach (var block in blocks)
            {
                if (block.Type != DocumentBlock.BlockType.Image)
                {
                    sb.Append(block.Content + "\n");
                }
            }
            sb.Append("'''");
            await AskGpt(sb.ToString(), user_id, no_function:true);
            return (true, string.Empty);
        }
        else
        {
            return (success, title);
        }
    }
    
    public async Task<(bool success, string message)> GetFeiShuDocumentAndSummarize(string user_id, string url)
    {
        if (!url.StartsWith("https://yesmro101.feishu.cn/docx/") && !url.StartsWith("https://yesmro101.feishu.cn/wiki/"))
            return (false, "仅支持飞书云文档和知识库云文档类型");
        var token = GetUserAccessToken(user_id);
        if (string.IsNullOrEmpty(token))
            return (false, "");
        
        var docId = "";
        if (url.StartsWith("https://yesmro101.feishu.cn/docx/"))
        {
            docId = url.Substring("https://yesmro101.feishu.cn/docx/".Length);
            if (docId.IndexOf("?") > 0)
                docId = docId.Substring(0, docId.IndexOf("?"));
        }
        else
        {
            var wikiId = url.Substring("https://yesmro101.feishu.cn/wiki/".Length);
            if (wikiId.IndexOf("?") > 0)
                wikiId = wikiId.Substring(0, wikiId.IndexOf("?"));
            var request = new RestRequest($"open-apis/wiki/v2/spaces/get_node?token={wikiId}", Method.Get);
            request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
            var response = _restClient.GetClient().Execute(request, Method.Get);
            var o = JObject.Parse(response.Content);
            if (o["code"].Value<int>() == 0)
            {
                if (o["data"]["node"]["obj_type"].Value<string>() == "docx")
                    docId = o["data"]["node"]["obj_token"].Value<string>();
                else
                    return (false, "仅支持飞书云文档和知识库云文档类型");
            }
            else
            {
                return (false, "获取知识库信息失败，"+o["msg"].Value<string>());
            }
        }
        if (!string.IsNullOrEmpty(docId))
        {
            var request = new RestRequest($"open-apis/docx/v1/documents/{docId}/raw_content", Method.Get);
            request.AddParameter("Authorization", $"Bearer {token}", ParameterType.HttpHeader);
            var response = _restClient.GetClient().Execute(request, Method.Get);
            var o = JObject.Parse(response.Content);
            if (o["code"].Value<int>() == 0)
            {
                var content = o["data"]["content"].Value<string>();
                var sb = new StringBuilder("请对以下文章内容进行总结，提取其中关键的论点论据，输出一篇简洁有条理的文章摘要，但不要遗漏重点信息，控制在500字以内：\n'''\n");
                sb.Append(content);
                sb.Append("'''");
                await AskGpt(sb.ToString(), user_id, no_function:true);
                return (true, string.Empty);
            }
            else
            {
                return (false, "获取文档正文失败，"+o["msg"].Value<string>());
            }
        }
        else
        {
            return (false, "链接格式不正确");
        }
    }
    #endregion
    
    #region 消息卡片图表
    
    public string GetChartCardMessage(string title, string chartType, ChartData[] data)
    {
        var _type = "bar";
        if (chartType == "折线图")
            _type = "line";
        else if (chartType == "饼图")
            _type = "pie";
        else if (chartType == "瀑布图")
            _type = "waterfall";

        var msg = @"{
              ""config"": {
                ""width_mode"": ""fill""
              },
              ""header"": {
                ""template"": ""green"",
                ""title"": {
                  ""content"": " + JsonConvert.SerializeObject(string.IsNullOrEmpty(title) ? "图表" : title) + @",
                  ""tag"": ""plain_text""
                }
              },
              ""i18n_elements"": {
                ""zh_cn"": [
                    {
                      ""tag"": ""chart"",
                      ""chart_spec"": {
                        ""type"": """ + _type + @""",
                        ""color"": [""#1664FF"", ""#19A7CE"", ""#FF8A00""],
                        ""data"": {
                          ""values"": " + JsonConvert.SerializeObject(data) + @"
                        },
                        ""xField"": " + (_type == "bar" ? @"[""x"", ""series""]" : @"""x""") + @",
                        ""yField"": ""y"",
                        ""seriesField"": ""series"",
                        ""total"": {
                            ""type"": ""end"",
                            ""text"": ""总计""
                        }
                      }
                    }
                ]
              }
            }";
        return msg;
    }

    #endregion
}