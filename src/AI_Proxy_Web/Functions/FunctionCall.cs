using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Database;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions;

/// <summary>
/// 用于接收模型返回的function call的参数，执行并获取结果，并保存在ChatContexts中，后续对话需要重新添加到提交参数里
/// </summary>
public class FunctionCall
{
    /// <summary>
    /// The id/call_id of the function.
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; }
    
    /// <summary>
    /// The itemid for openai
    /// </summary>
    [JsonProperty("itemid")]
    public string ItemId { get; set; }
    
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("type")]
    public FunctionType Type { get; set; }
    
    /// <summary>
    /// 模型返回该function时是否需要自动重新发起提交对话
    /// </summary>
    [JsonProperty("recall")]
    public bool NeedRecall { get; set; }
    
    [JsonProperty("arguments")]
    public string Arguments { get; set; }
    
    [JsonProperty("resultStr")]
    public string ResultStr { get; set; }
    
    private Result? _result { get; set; }
    
    /// <summary>
    /// 它是个计算属性，通过上面的ResultStr来保存和还原Result结果类型，所以本身不需要保存到JSON
    /// </summary>
    [JsonIgnore]
    public Result? Result {
        get
        {
            if (_result == null && !string.IsNullOrEmpty(ResultStr))
            {
                _result = JsonConvert.DeserializeObject<Result>(ResultStr, new ResultJsonConverter());
            }
            return _result;
        }
        set
        {
            _result = value;
            ResultStr = JsonConvert.SerializeObject(value);
        } 
    }
    
    /// <summary>
    /// 替换原来的提示
    /// </summary>
    [JsonIgnore]
    public string? Prompt { get; set; }
}


public class ResultJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(Result);
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        JObject? jobj = serializer.Deserialize<JObject>(reader);
        var tp = (ResultType)jobj["resultType"].Value<int>();
        object? o;
        if (tp == ResultType.Answer|| tp == ResultType.Error)
        {
            o =  Result.New(tp, jobj["result"].Value<string>());  
        }
        else if (tp == ResultType.ImageBytes)
        {
            o =  FileResult.Answer(Convert.FromBase64String(jobj["result"].Value<string>()), "", ResultType.ImageBytes);
        }
        else
        {
            throw new NotImplementedException();
        }
        return o;
    }
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        string s = JsonConvert.SerializeObject(value);
        writer.WriteValue(s);   
    }
}