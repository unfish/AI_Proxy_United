using System.ComponentModel;
using AI_Proxy_Web.Apis;
using AI_Proxy_Web.Apis.Base;
using AI_Proxy_Web.Helpers;

namespace AI_Proxy_Web.Models;

public class ChatModel
{
    /// <summary>
    /// 哪些模型可以使用function call功能
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool CanUseFunction(int value)
    {
        return DI.GetApiClassAttribute(value).CanUseFunction;
    }
    
    /// <summary>
    /// 哪些模型可以处理文件消息
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool CanProcessFile(int value)
    {
        return DI.GetApiClassAttribute(value).CanProcessFile;
    }
    
    /// <summary>
    /// 哪些模型可以处理图片消息，不是画图
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool CanProcessImage(int value)
    {
        return DI.GetApiClassAttribute(value).CanProcessImage;
    }
    
    /// <summary>
    /// 哪些模型可以处理多图片
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool CanProcessMultiImages(int value)
    {
        return DI.GetApiClassAttribute(value).CanProcessMultiImages;
    }
    
    public static List<ChatModelDto> GetMenus(int selectedValue, bool withAll = true, int level = 0)
    {
        var q = allModels.Where(t => !t.Hidden && t.NeedLevel <= level);
        if (!withAll)
            q = q.Where(t =>t.Type == ApiClassTypeEnum.问答模型 || t.Type == ApiClassTypeEnum.推理模型);
        var list = q.OrderBy(t=>t.Type).ThenBy(t=>t.Order).Select(t => new ChatModelDto()
        {
            Label = t.Name, Value = t.Id, Selected = t.Id == selectedValue, Description = t.Description, Type = t.Type.ToString()
        }).ToList();
        return list;
    }

    public static List<ApiClassAttribute> GetFullList(int selectedValue, bool withAll = true, int level = 0)
    {
        var q = allModels.Where(t => !t.Hidden && t.NeedLevel <= level);
        if (!withAll)
            q = q.Where(t =>t.Type == ApiClassTypeEnum.问答模型 || t.Type== ApiClassTypeEnum.推理模型);
        var list = q.OrderBy(t=>t.Type).ThenBy(t=>t.Order).ToList();
        return list;
    }
    
    private static List<ApiClassAttribute> allModels
    {
        get
        {
            return DI.GetApiClassAttributes();
        }
    }
    
    public static int GetUserDefaultModel(string ownerId, string prefix)
    {
        var chatModelCacheKey = $"{ownerId}_{prefix}_ai_model";
        var model = CacheService.Get<string>(chatModelCacheKey);
        if (!string.IsNullOrEmpty(model))
            return int.Parse(model);
        else
            return GetSysDefaultModel();
    }

    public static int GetSysDefaultModel()
    {
        return (int)M.GPT4o_Mini;
    }

    public static bool IsModel(int value, Type apiClass)
    {
        return DI.IsApiClass(value, apiClass);
    }
    
    public static ApiClassAttribute? GetModel(int value)
    {
        return DI.GetApiClassAttribute(value);
    }

    public static int GetModelId(Type apiClass)
    {
        return DI.GetApiClassAttributeId(apiClass);
    } 
    
    public static int GetModelIdByName(string name)
    {
        return allModels.FirstOrDefault(t => t.Name == name)?.Id ?? -1;
    }
    
    public static string SetDefaultModel(string ownerId, string prefix, int chatModel)
    {
        if (DI.IsApiClass(chatModel))
        {
            var chatModelCacheKey = $"{ownerId}_{prefix}_ai_model";
            CacheService.Save(chatModelCacheKey, chatModel.ToString(), DateTime.Now.AddDays(30));
            var attr = DI.GetApiClassAttribute(chatModel);
            return $"您的AI助理已切换为{attr.Name}。\n{attr.Description}";
        }
        else
        {
            return $"模型{chatModel}不存在或不可用";
        }
    }

    /// <summary>
    /// 是否是语言模型，才可以支持提示模板功能
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool IsTextModel(int value)
    {
        var type = DI.GetApiClassAttribute(value).Type;
        return type == ApiClassTypeEnum.问答模型 || type == ApiClassTypeEnum.推理模型;
    }
}

public class ChatModelDto
{
    public string Type { get; set; }
    public string Label { get; set; }
    public int Value { get; set; }
    public string Description { get; set; }
    public bool Selected { get; set; }
}