using System.Security.Cryptography;
using System.Text;

namespace AI_Proxy_Web.Helpers;

public class HashHelper
{
    public static string GetSha1Str(string key, string msg)
    {
        var sha = new HMACSHA1(Encoding.UTF8.GetBytes(key));
        var t2 = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(msg)));
        return t2;
    }
    
    /// <summary>
    /// SHA256编码，并返回 BASE64编码结果
    /// </summary>
    /// <param name="key"></param>
    /// <param name="msg"></param>
    /// <returns></returns>
    public static string GetSha256Str(string key, string msg)
    {
        var sha = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var t2 = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(msg)));
        return t2;
    }
    
    /// <summary>
    /// SHA256编码，并返回小写16进制字符串
    /// </summary>
    /// <param name="key"></param>
    /// <param name="msg"></param>
    /// <returns></returns>
    public static string GetSha256HEX(string key, string msg)
    {
        var sha = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(msg));
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < hash.Length; i++)
        {
            builder.Append(hash[i].ToString("x2"));
        }
        return builder.ToString();
    }
    
    public static string GetMd5Str(string str)
    {
        var md5 = MD5.Create();
        return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(str))).Replace("-", null).ToLower();
    }
    
    public static string GetMd5_16Str(string str)
    {
        var md5 = MD5.Create();
        return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(str))).Replace("-", null).ToLower().Substring(0, 16);
    }
    public static string GetMd5_16Str(byte[] file)
    {
        var md5 = MD5.Create();
        return BitConverter.ToString(md5.ComputeHash(file)).Replace("-", null).ToLower().Substring(0, 16);
    }
}