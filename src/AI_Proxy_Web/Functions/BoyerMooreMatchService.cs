namespace AI_Proxy_Web.Functions;

/// <summary>
/// BoyerMoore快速匹配算法，在一个字符串里匹配另一个字符串数组中的值，返回命中的词的索引和具体的词
/// </summary>
public class BoyerMooreMatchService:IBoyerMooreMatchService
{
    private int[] lastOccurrence;

    /// <summary>
    /// 匹配算法
    /// </summary>
    /// <param name="text">输入的文本句子</param>
    /// <param name="patterns">要查找的词的数组</param>
    /// <param name="maxCount">最多匹配多少个结果，默认只找1个就返回，避免后续的无效查找</param>
    /// <returns>0命中的词的索引和词本身的列表</returns>
    public List<SearchResult> Search(string text, string[] patterns, int maxCount = 1)
    {
        if(lastOccurrence==null)
            lastOccurrence = new int[65536]; //用到的时候再初始化，避免类被无效引用的时候增加压力
        
        List<SearchResult> results = new List<SearchResult>();

        foreach (string pattern in patterns)
        {
            int patternLength = pattern.Length;
            int textLength = text.Length;

            if (patternLength == 0 || textLength == 0 || patternLength > textLength)
                continue;

            Preprocess(pattern);
            
            int i = patternLength - 1;
            int j = patternLength - 1;

            while (i < textLength)
            {
                if (pattern[j] == text[i])
                {
                    if (j == 0)
                    {
                        results.Add(new SearchResult(i, pattern));
                        break;
                    }
                    else

                    {
                        i--;
                        j--;
                    }
                }
                else
                {
                    i += patternLength - Math.Min(j, 1 + lastOccurrence[text[i]]);
                    j = patternLength - 1;
                }
            }
            if(results.Count>=maxCount) //最多匹配两个结果就可以了
                break;
        }

        return results;
    }

    private void Preprocess(string pattern)
    {
        for (int i = 0; i < lastOccurrence.Length; i++)
        {
            lastOccurrence[i] = -1;
        }

        for (int i = 0; i < pattern.Length; i++)
        {
            lastOccurrence[pattern[i]] = i;
        }
    }
    
    public class SearchResult
    {
        public int Index { get; set; }
        public string Word { get; set; }

        public SearchResult(int index, string word)
        {
            this.Index = index;
            this.Word = word;
        }
    }
}