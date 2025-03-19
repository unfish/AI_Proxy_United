using CSRedis;
using MessagePack;
using Newtonsoft.Json;

namespace AI_Proxy_Web.Helpers;

public abstract class CacheService
{
    public static T? Get<T>(string key) where T : class
    {
        try
        {
            var myString = RedisHelper.Get(key);
            if (typeof(T) == typeof(string))
                return (T)Convert.ChangeType(myString.Trim('"'), typeof(string));
            
            if (!string.IsNullOrEmpty(myString))
            {
                return JsonConvert.DeserializeObject<T>(myString);
            }
            else
            {
                return null;
            }
        }
        catch (Exception)
        {
            // Log Exception
            return null;
        }
    }
    
    private static string SerializeContent(object value)
    {
        return JsonConvert.SerializeObject(value);
    }
    
    public static bool Save<T>(string key, T value, DateTime expireTime ) where T : class
    {
        var timespan= expireTime - DateTime.Now;
        if(typeof(T) == typeof(string))
            return RedisHelper.Set(key, value, timespan);
        else
        {
            var stringContent = SerializeContent(value);
            return RedisHelper.Set(key, stringContent, timespan);
        }
    }
    
    public static bool Save<T>(string key, T value, int seconds) where T : class
    {
        if(typeof(T) == typeof(string))
            return RedisHelper.Set(key, value, seconds);
        else
        {
            var stringContent = SerializeContent(value);
            return RedisHelper.Set(key, stringContent, seconds);
        }
    }
    
    public static bool Delete(string key)
    {
        return RedisHelper.Del(new []{key})>0;
    }
        
    public static bool GetLock(string key, int seconds=-1)
    {
        var stringContent = "true";
        return RedisHelper.Set(key, stringContent, seconds, RedisExistence.Nx);
    }
    
    public static bool BSave<T>(string key, T value, DateTime expireTime ) where T : class
    {
        var timespan= expireTime - DateTime.Now;
        var v = MessagePackSerializer.Serialize(value);
        return RedisHelper.Set(key, v, timespan);
    }
    
    public static bool BSave<T>(string key, T value, int seconds) where T : class
    {
        var v = MessagePackSerializer.Serialize(value);
        return RedisHelper.Set(key, v, seconds);
    }
    
    public static T? BGet<T>(string key) where T : class
    {
        try
        {
            var value = RedisHelper.Get<byte[]>(key);
            if (value!=null)
            {
                return MessagePackSerializer.Deserialize<T>(value);
            }
            else
            {
                return null;
            }
        }
        catch (Exception)
        {
            // Log Exception
            return null;
        }
    }
}