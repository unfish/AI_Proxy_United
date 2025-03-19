namespace AI_Proxy_Web.Functions;

public interface IBoyerMooreMatchService
{
    /// <summary>
    /// 匹配算法
    /// </summary>
    /// <param name="text">输入的文本句子</param>
    /// <param name="patterns">要查找的词的数组</param>
    /// <param name="maxCount">最多匹配多少个结果，默认只找1个就返回，避免后续的无效查找</param>
    /// <returns>0命中的词的索引和词本身的列表</returns>
    List<BoyerMooreMatchService.SearchResult> Search(string text, string[] patterns, int maxCount = 1);
}