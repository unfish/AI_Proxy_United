using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace AI_Proxy_Web.Feishu;

public class FeishuMessageSender
{
    private readonly BlockingCollection<string> _dataQueue = new BlockingCollection<string>();
    private readonly BaseFeishuService _feishuService;
    private string flushSign = "[[FLUSH]]";
    private string followSign = "[[FOLLOW]]";
    public FeishuMessageSender(BaseFeishuService feishuService, string user_id)
    {
        _feishuService = feishuService;
        this.user_id = user_id;
    }

    private string user_id;
    private string send_msg_id = ""; //当前使用的卡片消息ID
    private string send_card_id = ""; //当前使用的卡片ID
    private int sequence = 1;
    private string last_msg_id = ""; //最后一次使用的卡片消息ID（用于发送气泡消息）
    private Task? task = null;
    private DateTime start = DateTime.Now; //控制消息修改接口频率
    private bool flushed = false; //消息是否被完结过，如果没有，要确保最后一条消息发送出去（因为有时间差限制，最后一次未必会被发送）
    private string last_msg_content = ""; //用来保存最后一条消息，配合上面的逻辑
    private int lengthLimit = 20 * 1024; //飞书卡片有30K的大小限制，所以长内容需要自己做截断。目前只有GPT o1和mini单次回复会超过这个长度，其它的非大模型接口，比如搜索或文章原文提取也有可能
    private string lengthLimitHolder = ""; //保存截断后的内容，后续的每次发送都要先截断这部分内容，避免重复计算和每次计算的不一致
    public void Start()
    {
        task = Task.Run(async () =>
        {
            start = DateTime.Now;
            lengthLimitHolder = "";
            foreach (var data in _dataQueue.GetConsumingEnumerable())
            {
                if (!string.IsNullOrEmpty(data))
                {
                    if (data == flushSign) //发送最后一条并清除send_msg_id，关闭当前卡片
                    {
                        if (flushMsgs.TryTake(out var flushMsg) && !string.IsNullOrEmpty(flushMsg))
                        {
                            if (!flushed)
                            {
                                var text = GetSafeSendText(flushMsg);
                                FinishCardMessage(text);
                                flushed = true;
                                lengthLimitHolder = "";
                            }
                        }
                    }
                    else if (data == followSign) //发送一条泡泡信息，只能使用最后一条消息的ID来发送
                    {
                        if (!string.IsNullOrEmpty(last_msg_id) && followUpMsgs != null)
                            _feishuService.SendFollowUpMessage(last_msg_id, followUpMsgs);
                    }
                    else
                    {
                        var text = GetSafeSendText(data);

                        if (text.Split("```").Length % 2 == 0) //代码块分隔符是单数，还没有发送完当前代码块，临时补全结尾代码块
                        {
                            text += "\n```";
                        }
                        SendOrUpdateCardMessage(text);
                        start = DateTime.Now;
                        flushed = false;
                    }
                }
            }

            if (!flushed && !string.IsNullOrEmpty(last_msg_content)) //如果用户添加了新消息没有调用Flush方法就调了Finish，要确保最后一条消息被发送出去。如果调用过Flush以后没有添加新消息，就不能再发了
            {
                var text = GetSafeSendText(last_msg_content);
                FinishCardMessage(text);
            }
        });
    }

    private void SendOrUpdateCardMessage(string text)
    {
        if (_feishuService.UsePartialMessage)
        {
            if (string.IsNullOrEmpty(send_card_id))
            {
                send_card_id = _feishuService.CreatePartialCardMessage();
                if (string.IsNullOrEmpty(send_card_id))
                {
                    _feishuService.SendMessage(user_id, "创建消息卡片异常");
                    Finish();
                }
                else
                {
                    send_msg_id = _feishuService.SendMessage(user_id, send_card_id, FeishuMessageType.CardId);
                    last_msg_id = send_msg_id;
                    _feishuService.UpdatePartialCardText(send_card_id, text, sequence++);
                }
            }
            else
            {
                _feishuService.UpdatePartialCardText(send_card_id, text, sequence++);
            }
        }
        else
        {
            send_msg_id = _feishuService.SendOrUpdateMessage(send_msg_id, user_id, text);
            if(!string.IsNullOrEmpty(send_msg_id))
                last_msg_id = send_msg_id;
        }
    }

    private void FinishCardMessage(string text)
    {
        if (_feishuService.UsePartialMessage && !string.IsNullOrEmpty(send_card_id))
        {
            _feishuService.FinishPartialCardText(send_card_id, text, sequence++);
            send_card_id = string.Empty;
            send_msg_id = string.Empty;
        }
        else
        {
            _feishuService.SendOrUpdateMessage(send_msg_id, user_id, text);
            send_msg_id = string.Empty;
        }
    }

    private string GetSafeSendText(string data)
    {
        var text = data;
        while (text.Length > lengthLimit) //超长内容需要循环截断，直到小于长度限制为止
        {
            if (lengthLimitHolder.Length > 0 && text.StartsWith(lengthLimitHolder))
            {
                text = text.Remove(0, lengthLimitHolder.Length);
            }

            if (text.Length > lengthLimit)
            {
                var index = text.LastIndexOf("\n\n", lengthLimit, StringComparison.Ordinal);
                if (index > 0)
                {
                    index += 2;
                    var split = text.Substring(0, index);
                    if (split.Split("```").Length % 2 == 0) //代码块分隔符是单数，说明截断了代码块，截断位置往前移
                    {
                        var index2 = text.LastIndexOf("```", lengthLimit, StringComparison.Ordinal);
                        if (index2 > 0 && text.Length - index2 < lengthLimit) //防护措施，如果整个代码块加起来还是太大，就走原来的位置截断
                        {
                            index = index2;
                            split = text.Substring(0, index);
                        }
                    }
                    FinishCardMessage(split);
                    lengthLimitHolder += split;
                    text = text.Remove(0, index);
                }
            }
        }
        text = CodeMarkdownRegex.Replace(text, "");
        return ImageMarkdownRegex.Replace(text, "$1 $2");
    }
    private static readonly Regex CodeMarkdownRegex = new Regex(@"(?m)^\s+(?=`{3})", RegexOptions.Compiled);
    private static readonly Regex ImageMarkdownRegex = new Regex(@"!\[(.*?)\]\((.*?)\)", RegexOptions.Compiled);
    
    public void AddData(string data, bool noWaiting = false)
    {
        if (noWaiting || (data.Length >= 10 && (DateTime.Now - start).TotalMilliseconds > 200))
        {
            _dataQueue.Add(data);
            start = DateTime.Now;
        }
        last_msg_content = data;
    }

    private ConcurrentBag<string> flushMsgs = new ConcurrentBag<string>();
    public void Flush(string data)
    {
        flushMsgs.Add(data);
        _dataQueue.Add(flushSign);
    }

    private string[]? followUpMsgs;
    public void SendFollowUp(string[] msgs)
    {
        followUpMsgs = msgs;
        _dataQueue.Add(followSign);
    }

    public void Wait()
    {
        task?.Wait();
    }

    public void Finish()
    {
        _dataQueue.CompleteAdding();
    }
}