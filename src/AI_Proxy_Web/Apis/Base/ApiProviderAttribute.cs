namespace AI_Proxy_Web.Apis.Base;

public class ApiProviderAttribute : Attribute
{
    public string Name { get; set; }

    public ApiProviderAttribute(string name)
    {
        Name = name;
    }
}