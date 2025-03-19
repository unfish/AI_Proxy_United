using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Functions
{
    /// <summary>
    /// 用于类型化提交msg里面的tools参数的function类
    /// </summary>
    public class Function
    {
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }
        

        [JsonProperty("description", Required = Required.Default)]
        public string Description { get; set; }
        
        private JObject _parameters;
        
        [JsonProperty("parameters", Required = Required.Default)]
        public object Parameters
        {
            get
            {
                return _parameters;
            }
            set
            {
                try
                {
                    if (value is string jsonStringValue)
                    {
                        _parameters = JObject.Parse(jsonStringValue);
                    }
                    else if (value is JObject jObjectValue)
                    {
                        _parameters = jObjectValue;
                    }
                    else
                    {
                        var settings = new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Ignore
                        };
                        _parameters = JObject.FromObject(value, JsonSerializer.Create(settings));
                    }
                }
                catch (JsonException e)
                {
                    throw new ArgumentException("Could not convert the provided object into a JSON object. Make sure that the object is serializable and its structure matches the required schema.", e);
                }
            }
        }
        
        [JsonIgnore]
        public string? Prompt { get; set; }
        
        public Function(string name, string description, object parameters, string? prompt)
        {
            this.Name = name;
            this.Description = description;
            this.Parameters = parameters;
            this.Prompt = prompt;
        }

        public Function()
        {
        }
    }
}