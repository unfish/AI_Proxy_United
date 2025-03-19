using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.WebSockets;
using System.Numerics;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Functions;
using AI_Proxy_Web.Helpers;
using AI_Proxy_Web.Models;
using AI_Proxy_Web.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebHeaderCollection = System.Net.WebHeaderCollection;
using WebSocketState = System.Net.WebSockets.WebSocketState;

namespace AI_Proxy_Web.Apis;

[ApiClass(M.豆包画图, "豆包画图", "豆包是字节新推出的画图模型，得分很高。", 212, type: ApiClassTypeEnum.画图模型, priceIn: 0, priceOut: 0.2)]
public class ApiDoubaoImage:ApiBase
{
    protected DoubaoImageClient _client;
    public ApiDoubaoImage(IServiceProvider serviceProvider):base(serviceProvider)
    {
        _client = serviceProvider.GetRequiredService<DoubaoImageClient>();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async IAsyncEnumerable<Result> DoProcessChat(ApiChatInputIntern input)
    {
        input.IgnoreAutoContexts = true;
        await foreach (var resp in _client.SendMessageStream(input))
        {
            yield return resp;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    protected override async Task<Result> DoProcessQuery(ApiChatInputIntern input)
    {
        return Result.Error("画图接口不支持Query调用");
    }
}


/// <summary>
/// 火山引擎平台其它接口，需要走老的认证机制
/// </summary>
public class DoubaoImageClient: IApiClient
{
    public DoubaoImageClient(ConfigHelper configHelper)
    {
        accessKey = configHelper.GetConfig<string>("Service:DoubaoImage:AccessKey");
        secretKey = configHelper.GetConfig<string>("Service:DoubaoImage:SecretKey");
    }
    private static string hostUrl = "https://visual.volcengineapi.com";
    private string accessKey;
    private string secretKey;

    #region Get Token
    public string Host = "visual.volcengineapi.com";
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
    /// 要增加上下文功能通过input里面的history数组变量，数组中每条记录是user和bot的问答对
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public string GetMsgBody(ApiChatInputIntern input)
    {
        return JsonConvert.SerializeObject(new
        {
            req_key = "high_aes_general_v21_L",
            prompt = input.ChatContexts.Contexts.Last().QC.Last().Content,
            width = 768,
            height = 768,
            use_pre_llm = true
        });
    }

    /// <summary>
    /// 文生图
    /// 文档地址 https://www.volcengine.com/docs/6791/1366783
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public async IAsyncEnumerable<Result> SendMessageStream(ApiChatInputIntern input)
    {
        DateTime dateTimeSign = DateTime.UtcNow;
        NowDate = dateTimeSign.ToString("yyyyMMdd");
        NowTime = dateTimeSign.ToString("hhmmss");
        dateTimeSignStr = NowDate + "T" + NowTime + "Z";

        var msg = GetMsgBody(input);
        var query = "Action=CVProcess&Version=2022-08-31";
        var url = hostUrl + "?" + query;
        byte[] byteArray = Encoding.UTF8.GetBytes(msg);
        string bodyHash = ComputeHash256(msg, new SHA256CryptoServiceProvider());

        HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
        request.Method = "POST";
        request.ContentType = "application/json";
        request.ContentLength = byteArray.Length;
        request.Accept = "application/json";

        request.Host = Host;
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
                    yield return Result.Error(respondStr);
                }
                else
                {
                    var o = JObject.Parse(respondStr);
                    if (o["status"].Value<int>() == 10000)
                    {
                        var pe = o["data"]["pe_result"].Value<string>();
                        yield return Result.Answer(pe);
                        var b64 = o["data"]["binary_data_base64"][0].Value<string>();
                        yield return FileResult.Answer(Convert.FromBase64String(b64), "png", ResultType.ImageBytes);
                    }
                }
            }
        }
    }
    
    
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

        var imageBase64 = Convert.ToBase64String(bytes);
        var msg = "image_base64=" + HttpUtility.UrlEncode(imageBase64);
        var query = "Action=OCRTable&Version=2021-08-23";
        var url = hostUrl + "?" + query;
        byte[] byteArray = Encoding.UTF8.GetBytes(msg);
        string bodyHash = ComputeHash256(msg, new SHA256CryptoServiceProvider());

        HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
        request.Method = "POST";
        request.ContentType = "application/x-www-form-urlencoded";
        request.ContentLength = byteArray.Length;
        request.Accept = "application/json";

        request.Host = Host;
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
