using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Proxy_Web.Apis.V2;

[ApiProvider("DoubaoImage")]
public class ApiDoubaoImageProvider : ApiProviderBase
{
    protected IHttpClientFactory _httpClientFactory;
    public ApiDoubaoImageProvider(ConfigHelper configHelper, IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory):base(configHelper,serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
    }

    private string accessKey;
    private string secretKey;
    public override void Setup(ApiClassAttribute attr)
    {
        base.Setup(attr);
        _chatUrl = _host + "images/generations";
        accessKey = configHelper.GetProviderConfig<string>(attr.Provider, "AccessKey"); //OCR用的
        secretKey = configHelper.GetProviderConfig<string>(attr.Provider, "SecretKey");
        extraOptionsList = new List<ExtraOption>()
        {
            new ExtraOption()
            {
                Type = "尺寸", Contents = new []
                {
                    new KeyValuePair<string, string>("自动", "2K"),
                    new KeyValuePair<string, string>("方形", "2048x2048"),
                    new KeyValuePair<string, string>("横屏", "2304x1728"),
                    new KeyValuePair<string, string>("竖屏", "1728x2304"),
                    new KeyValuePair<string, string>("超高清", "4K")
                }
            }
        };
    }
    
    /// <summary>
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input)
    {
        var op = GetExtraOptions(input.External_UserId)[0].CurrentValue;
        var qc = input.ChatContexts.Contexts.Last().QC;
        var prompt = qc.LastOrDefault(t => t.Type == ChatType.文本)?.Content ?? "";
        var images = qc.Any(t => t.Type == ChatType.图片Base64)
            ? qc.Where(t => t.Type == ChatType.图片Base64).Select(t =>
                $"data:{(string.IsNullOrEmpty(t.MimeType) ? "image/jpeg" : t.MimeType)};base64," + t.Content).ToArray()
            : null;
        
        var jSetting = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        return JsonConvert.SerializeObject(new
        {
            model = _modelName,
            prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
            size = op,
            stream = true,
            respnose_format = "url",
            watermark = false,
            image = images
        }, jSetting);
    }
    
    /// <summary>
    /// 普通请求接口
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {       
        HttpClient client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(300);
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _key);
        var url = _chatUrl;
        var msg = GetMsgBody(input);
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(msg, Encoding.UTF8, "application/json")
        }, HttpCompletionOption.ResponseHeadersRead);

        await foreach (var resp in ProcessStreamResponse(response))
            yield return resp;
    }

    public override async Task<Result> SendMessage(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }
    
    public async IAsyncEnumerable<Result> ProcessStreamResponse(HttpResponseMessage resp)
    {
        using (var stream = await resp.Content.ReadAsStreamAsync())
        using (StreamReader reader = new StreamReader(stream))
        {
            string line;
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                line = await reader.ReadToEndAsync();
                yield return Result.Error(resp.StatusCode + " : " + line);
                yield break;
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _key);
            while ((line = await reader.ReadLineAsync()) != null)
            {
                Console.WriteLine(line);
                if (line.StartsWith("event:"))
                    continue;
                if (line.StartsWith("data:"))
                    line = line.Substring("data:".Length);
                line = line.TrimStart();

                if (line == "[DONE]")
                {
                    break;
                }
                else if (line.StartsWith(":")) //通常用于返回注释
                {
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    var res = JObject.Parse(line);
                    if (res["type"] != null)
                    {
                        string type = res["type"].Value<string>();
                        if (type == "image_generation.partial_succeeded")
                        {
                            var image_url =  res["url"].Value<string>();
                            var bytes = await client.GetByteArrayAsync(image_url);
                            yield return FileResult.Answer(bytes, "jpg",
                                ResultType.ImageBytes);
                        }else if (type == "image_generation.partial_failed")
                        {
                            yield return Result.Error(line);
                        }
                    }
                    else
                    {
                        yield return Result.Error(line);
                    }
                }
            }
        }
    }

    
    
    
    #region Get Token
    private string Host = "visual.volcengineapi.com";
    const string Service = "cv";
    const string Region = "cn-north-1";
    const string Algorithm = "HMAC-SHA256";
    string NowDate;
    string NowTime;
    string dateTimeSignStr;
    protected static string ComputeHash256(string input, HashAlgorithm algorithm)
    {
        Byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        Byte[] hashedBytes = algorithm.ComputeHash(inputBytes);

        return ToHexString(hashedBytes);
    }


    private static byte[] hmacsha256(string text, byte[] secret)
    {
        //string signRet = string.Empty;
        byte[] hash;
        using (HMACSHA256 mac = new HMACSHA256(secret))
        {
            hash = mac.ComputeHash(Encoding.UTF8.GetBytes(text));
            // signRet = Convert.ToBase64String(hash);
        }
        return hash;

    }
    private string JoinString(string[] strings, string seperator)
    {
        StringBuilder builder = new StringBuilder();
        foreach (string s in strings)
        {
            builder.Append(s).Append(seperator);
        }
        builder.Remove(builder.Length - 1, 1);
        return builder.ToString();
    }

    public static string ToHexString(byte[] bytes) // 0xae00cf => "AE00CF "
    {
        string hexString = string.Empty;
        if (bytes != null)
        {
            StringBuilder strB = new StringBuilder();

            for (int i = 0; i < bytes.Length; i++)
            {
                strB.Append(bytes[i].ToString("X2"));
            }
            hexString = strB.ToString();
        }
        return hexString.ToLower();
    }
    private string GetSignedHeader(string dateTimeSignStr, string bodyHash, WebHeaderCollection headers, string urlQuery)
    {
        var H_INCLUDE = new List<string>();
        H_INCLUDE.Add("Content-Type");
        H_INCLUDE.Add("Content-Md5");
        H_INCLUDE.Add("Host");
        List<string> signedHeaders = new List<string>();

        for(int i=0;i< headers.Count; i++)
        {
            string headerName = headers.GetKey(i);

            if (H_INCLUDE.Contains(headerName) || headerName.StartsWith("X-"))
            {
                signedHeaders.Add(headerName.ToLower());
            }
        }
        signedHeaders.Add("host");
        signedHeaders.Sort();
            StringBuilder signedHeadersToSignStr = new StringBuilder();

            string headerValue;
            foreach (string signedHeader in signedHeaders)
            {
                if (signedHeader.Equals("host"))
                {
                    headerValue = Host;
                }
                else
                {
                    headerValue = headers.Get(signedHeader).Trim();
                }
                 
                signedHeadersToSignStr.Append(signedHeader).Append(":").Append(headerValue).Append("\n");
            }

            string signedHeadersStr = JoinString(signedHeaders.ToArray(), ";");

            string canonicalRequest = JoinString(new string[] {
                "POST",
                "/",
                urlQuery,
                signedHeadersToSignStr.ToString(),
                signedHeadersStr,
                bodyHash
                },
                "\n");
            //step 1
            string hashedCanonReq = ComputeHash256(canonicalRequest, new SHA256CryptoServiceProvider());
            //step 2
            String stringToSign = Algorithm + "\n" + 
                                    dateTimeSignStr + "\n" + 
                                    JoinString(
                                    new string[] {
                                        NowDate,
                                        Region,
                                        Service,
                                        "request"
                                    }, 
                                    "/") + "\n" +
                                    hashedCanonReq
                                    ;
            //step 3
            //String secretKey, String date, String region, String service
            byte[] kDate = hmacsha256(NowDate, Encoding.UTF8.GetBytes(secretKey));
            byte[] kRegion = hmacsha256(Region, kDate);
            byte[] kService = hmacsha256(Service, kRegion);
            byte[] signingKey = hmacsha256("request", kService);       

            byte[] signature = hmacsha256(stringToSign, signingKey);

            string AuthHeader = Algorithm + " Credential=" + accessKey + "/" +NowDate + "/" + Region+"/"+Service+"/request"+
                ", SignedHeaders=" + signedHeadersStr +
                ", Signature=" + ToHexString(signature);

            return AuthHeader;
    }

    #endregion
    
    /// <summary>
    /// 表格识别
    /// 文档地址 https://www.volcengine.com/docs/6790/117778
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async Task<(ResultType resultType, string[,]? result, string error)> OcrTableText(byte[] bytes)
    {
        DateTime dateTimeSign = DateTime.UtcNow;
        NowDate = dateTimeSign.ToString("yyyyMMdd");
        NowTime = dateTimeSign.ToString("hhmmss");
        dateTimeSignStr = NowDate + "T" + NowTime + "Z";
        var host = "https://visual.volcengineapi.com";

        var imageBase64 = Convert.ToBase64String(bytes);
        var msg = "image_base64=" + HttpUtility.UrlEncode(imageBase64);
        var query = "Action=OCRTable&Version=2021-08-23";
        var url = host + "?" + query;
        byte[] byteArray = Encoding.UTF8.GetBytes(msg);
        string bodyHash = ComputeHash256(msg, new SHA256CryptoServiceProvider());

        HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
        request.Method = "POST";
        request.ContentType = "application/x-www-form-urlencoded";
        request.ContentLength = byteArray.Length;
        request.Accept = "application/json";

        request.Host = host;
        request.Headers.Add("X-Date", dateTimeSignStr);
        request.Headers.Add("X-Content-Sha256", bodyHash);
        var authHeader = GetSignedHeader(dateTimeSignStr, bodyHash, request.Headers, query);
        request.Headers.Add("Authorization", authHeader);

        request.KeepAlive = false;
        using (Stream reqStream = request.GetRequestStream())
        {
            reqStream.Write(byteArray, 0, byteArray.Length);
        }

        using (HttpWebResponse webResponse = (HttpWebResponse)request.GetResponse())
        {
            using (StreamReader sr = new StreamReader(webResponse.GetResponseStream(), Encoding.UTF8))
            {
                var respondStr = sr.ReadToEnd();
                if (webResponse.StatusCode != HttpStatusCode.OK)
                {
                    return (ResultType.Error, null, respondStr);
                }
                else
                {
                   var o = JObject.Parse(respondStr);
                   var table = o["data"]["table_infos"][0];
                   var rows = table["row_cnt"].Value<int>();
                   var cols = table["col_cnt"].Value<int>();
                   var values = new string[rows, cols];
                   foreach (var cell in table["cell_infos"] as JArray)
                   {
                       for (int i = cell["start_row"].Value<int>()-1; i < cell["end_row"].Value<int>(); i++)
                       {
                           for (int j = cell["start_col"].Value<int>()-1; j < cell["end_col"].Value<int>(); j++)
                           {
                               values[i, j] = cell["cell_text"].Value<string>();
                           }
                       }
                   }

                   return (ResultType.Answer, values, string.Empty);
                }
            }
        }
    }
}
