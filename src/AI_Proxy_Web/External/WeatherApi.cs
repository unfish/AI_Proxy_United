using System.Security.Cryptography;
using System.Text;
using System.Web;
using AI_Proxy_Web.Helpers;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.External;

public interface IWeatherApi
{
    object GetWeather(string city);
}

/// <summary>
/// 心知天气接口，免费的，申请个Secret就行
/// </summary>
public class WeatherApi:IWeatherApi
{
    private IHttpClientFactory _httpClientFactory;

    public WeatherApi(IHttpClientFactory httpClientFactory, ConfigHelper configHelper)
    {
        _httpClientFactory = httpClientFactory;
        AppKey = configHelper.GetConfig<string>("Service:Seniverse:Key");
        Secret = configHelper.GetConfig<string>("Service:Seniverse:Secret");
    }
    
    private string AppKey;
    private string Secret;

    public object GetWeather(string city)
    {
        var url = $"https://api.seniverse.com/v3/weather/now.json?key={Secret}&location={city}";
        //var url = $"https://api.seniverse.com/v3/pro/weather/grid/moment.json?key={Secret}&location={city}&language=zh-Hans&unit=c&advanced=2.1";
        var client = _httpClientFactory.CreateClient();
        var now = client.GetStringAsync(url).Result;
        var o = JObject.Parse(now);
        var nowObj = o["results"][0]["now"];
        
        url = $"https://api.seniverse.com/v3/weather/daily.json?key={Secret}&location={city}&language=zh-Hans&unit=c&start=0&days=5";
        var w5 = client.GetStringAsync(url).Result;
        o = JObject.Parse(w5);
        var w5Obj = o["results"][0]["daily"];
        
        url = $"https://api.seniverse.com/v3/life/suggestion.json?key={Secret}&location={city}&language=zh-Hans&days=5";
        var day5 = client.GetStringAsync(url).Result;
        o = JObject.Parse(day5);
        var day5Obj = o["results"][0]["suggestion"];
        return new
        {
            现在 = new
            {
                天气 = nowObj["text"].Value<string>(), 温度 = nowObj["temperature"].Value<string>() + "摄氏度"
                /*
                   天气 = nowObj["text"].Value<string>(),
                   温度 = nowObj["temperature"].Value<string>()+"摄氏度" ,
                   体感温度 = nowObj["feels_like"].Value<string>()+"摄氏度" ,
                   相对湿度 = nowObj["humidity"].Value<string>()+"%" ,
                   风向 = nowObj["wind_direction"].Value<string>(),
                   风力 = nowObj["wind_scale"].Value<string>()+"级" ,
                 */
            },
            今天 = new
            {
                白天天气 = w5Obj[0]["text_day"].Value<string>(),
                夜间天气 = w5Obj[0]["text_night"].Value<string>(),
                最高温度 = w5Obj[0]["high"].Value<string>() + "摄氏度",
                最低温度 = w5Obj[0]["low"].Value<string>() + "摄氏度",
                风向 = w5Obj[0]["wind_direction"].Value<string>(),
                风力 = w5Obj[0]["wind_scale"].Value<string>() + "级",
                空调 = day5Obj[0]["ac"]["details"].Value<string>(),
                空气污染扩散条件 = day5Obj[0]["air_pollution"]["details"].Value<string>(),
                晾晒 = day5Obj[0]["airing"]["details"].Value<string>(),
                体感 = day5Obj[0]["comfort"]["details"].Value<string>(),
                穿衣建议 = day5Obj[0]["dressing"]["details"].Value<string>(),
                防晒 = day5Obj[0]["sunscreen"]["details"].Value<string>(),
                紫外线 = day5Obj[0]["uv"]["details"].Value<string>(),
            },
            明天 = new
            {
                白天天气 = w5Obj[1]["text_day"].Value<string>(),
                夜间天气 = w5Obj[1]["text_night"].Value<string>(),
                最高温度 = w5Obj[1]["high"].Value<string>() + "摄氏度",
                最低温度 = w5Obj[1]["low"].Value<string>() + "摄氏度",
                风向 = w5Obj[1]["wind_direction"].Value<string>(),
                风力 = w5Obj[1]["wind_scale"].Value<string>() + "级",
                空调 = day5Obj[1]["ac"]["details"].Value<string>(),
                空气污染扩散条件 = day5Obj[1]["air_pollution"]["details"].Value<string>(),
                晾晒 = day5Obj[1]["airing"]["details"].Value<string>(),
                体感 = day5Obj[1]["comfort"]["details"].Value<string>(),
                穿衣建议 = day5Obj[1]["dressing"]["details"].Value<string>(),
                防晒 = day5Obj[1]["sunscreen"]["details"].Value<string>(),
                紫外线 = day5Obj[1]["uv"]["details"].Value<string>(),
            },
            后天 = new
            {
                白天天气 = w5Obj[2]["text_day"].Value<string>(),
                夜间天气 = w5Obj[2]["text_night"].Value<string>(),
                最高温度 = w5Obj[2]["high"].Value<string>() + "摄氏度",
                最低温度 = w5Obj[2]["low"].Value<string>() + "摄氏度",
                风向 = w5Obj[2]["wind_direction"].Value<string>(),
                风力 = w5Obj[2]["wind_scale"].Value<string>() + "级",
                空调 = day5Obj[2]["ac"]["details"].Value<string>(),
                空气污染扩散条件 = day5Obj[2]["air_pollution"]["details"].Value<string>(),
                晾晒 = day5Obj[2]["airing"]["details"].Value<string>(),
                体感 = day5Obj[2]["comfort"]["details"].Value<string>(),
                穿衣建议 = day5Obj[2]["dressing"]["details"].Value<string>(),
                防晒 = day5Obj[2]["sunscreen"]["details"].Value<string>(),
                紫外线 = day5Obj[2]["uv"]["details"].Value<string>(),
            }
        };
    }
}