namespace AI_Proxy_Web.Apis.Base;

public enum ApiClassTypeEnum
{
    问答模型,
    推理模型,
    画图模型,
    视频模型,
    搜索模型,
    辅助模型
}

/// <summary>
/// 所有的子类实例添加这个属性，可以实现自动注入
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ApiClassAttribute : Attribute
{ 
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int Order { get; set; }
    public ApiClassTypeEnum Type { get; set; }
    public bool CanUseFunction { get; set; }
    public bool Hidden { get; set; }
    public bool CanProcessFile { get; set; }
    public bool CanProcessImage { get; set; }
    public bool CanProcessMultiImages { get; set; }
    public bool CanProcessAudio { get; set; }
    public int NeedLevel { get; set; } //需要指定等级以上的客户才能使用这个模型
    public decimal PriceIn { get; set; } //百万Token输入价格
    public decimal PriceOut { get; set; } //百万Token输出价格，画图模型是单张价格
    public ApiClassAttribute(M id, string name, string description, int order, ApiClassTypeEnum type = ApiClassTypeEnum.问答模型, 
        bool canUseFunction = false, bool canProcessFile = false, bool canProcessImage = false, bool canProcessMultiImages = false, bool canProcessAudio = false,
        double priceIn = -1, double priceOut = -1)
    {
        this.Id = (int)id;
        this.Name = name;
        this.Description = description;
        this.Order = order;
        this.Type = type;
        this.CanUseFunction = canUseFunction;
        this.CanProcessFile = canProcessFile;
        this.CanProcessImage = canProcessImage;
        this.CanProcessMultiImages = canProcessMultiImages;
        this.CanProcessAudio = canProcessAudio;
        this.PriceIn = (decimal)priceIn;
        this.PriceOut = (decimal)priceOut;
    }
}
