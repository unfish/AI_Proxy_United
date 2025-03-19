using System.Collections.Concurrent;
using System.Reflection;
using AI_Proxy_Web.Apis.Base;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AI_Proxy_Web.Functions;

public static class DynamicCodeExecutor
{
    private static List<PortableExecutableReference> _references;
    private static ConcurrentDictionary<string, (object? instance, MethodInfo method)> _cache = new();

    static DynamicCodeExecutor()
    {
        _references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();
    }
    
    static (object? instance, MethodInfo method) CompileAndCache(IServiceProvider serviceProvider, string name, string code, string methodName)
    {
        var key = $"{name}_{methodName}";
        if (_cache.TryGetValue(key, out var cachedValue))
        {
            return cachedValue;
        }
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create(
            "Assembly_"+name, //每个动态函数放到不同的Assembly里避免类名冲突
            new[] { syntaxTree },
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var failures = result.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);
            foreach (var diagnostic in failures)
            {
                Console.Error.WriteLine($"{diagnostic.Id}: {diagnostic.GetMessage()}");
            }
            throw new InvalidOperationException("Compilation failed.");
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());

        var type = assembly.GetType("DN.DC");
        var method = type.GetMethod(methodName);

        var instance = Activator.CreateInstance(type, serviceProvider);
        (object? instance, MethodInfo method) compiledResult = (instance, method);
        _cache[key] = compiledResult;
        return compiledResult;
    }
    
    public static async Task<IAsyncEnumerable<Result>> ProcessChat(IServiceProvider serviceProvider, string name, string code, params object[] parameters)
    {
        var (instance, method) = CompileAndCache(serviceProvider, name, code, "ProcessChat");
        return (IAsyncEnumerable<Result>)(method.Invoke(instance, parameters));
    }

    public static async Task<Result> ProcessQuery(IServiceProvider serviceProvider, string name, string code, params object[] parameters)
    {
        var (instance, method) = CompileAndCache(serviceProvider, name, code, "ProcessQuery");
        var resultTask = (Task)method.Invoke(instance, parameters);
        await resultTask.ConfigureAwait(false);
        var resultProperty = resultTask.GetType().GetProperty("Result");
        return (Result)resultProperty?.GetValue(resultTask);
    }
}