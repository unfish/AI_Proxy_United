namespace AI_Proxy_Web.Models;

public enum ChatFrom
{
    Api = 0, 
    飞书 = 1,
    云文档 = 2,
    小程序 = 3,
    Internal = 100, //被内部函数调用
}