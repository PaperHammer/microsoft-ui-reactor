using System.Reflection;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Hooks;

int passed = 0, failed = 0;

try
{
    Console.WriteLine("UseDataSource PageSize repro (zero-based indexes)");
    Console.WriteLine($"Reactor: {typeof(RenderContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");

    Console.WriteLine("\n1. Request PageSize=10, options omitted");
    InitialPageSize(options: null, "Options omitted");
    InitialPageSize(new InfiniteResourceOptions(PageSize: 10), "Control: matching options");

    Console.WriteLine("\n2. Same hook changes both page sizes from 10 to 20");
    foreach (bool forceRestart in new[] { false, true })
    {
        using var cache = new QueryCache();
        var source = CreateSource();
        using var hook = new HookSession(cache, source);
        var before = hook.Render(10, new InfiniteResourceOptions(PageSize: 10));
        CheckRows("Control: initial size 10", before, 0, 9);
        // Whitespace changes a dependency but ListDataSource ignores it as a filter.
        var after = hook.Render(20, new InfiniteResourceOptions(PageSize: 20), forceRestart ? " " : null);
        Console.WriteLine($"  Other dependency changed: {forceRestart}; resource reused: {ReferenceEquals(before, after)}");
        CheckRows(forceRestart ? "Changed size with dependency restart" : "Changed size only", after, 10, 19);
    }
    {
        using var cache = new QueryCache();
        using var hook = new HookSession(cache, CreateSource());
        CheckRows("Control: fresh hook at size 20", hook.Render(20, new InfiniteResourceOptions(PageSize: 20)), 10, 19);
    }

    Console.WriteLine("\n3. Two hooks share a prefix but use different page sizes");
    foreach (bool sharePrefix in new[] { false, true })
    {
        using var cache = new QueryCache();
        var source = CreateSource();
        using var first = new HookSession(cache, source);
        using var second = new HookSession(cache, source);
        var a = first.Render(10, new InfiniteResourceOptions(PageSize: 10, CacheKeyPrefix: "rows-a"));
        CheckRows("Control: first hook at size 10", a, 0, 9);
        var b = second.Render(20, new InfiniteResourceOptions(PageSize: 20,
            CacheKeyPrefix: sharePrefix ? "rows-a" : "rows-b"));
        CheckRows(sharePrefix ? "Shared prefix, size-20 hook" : "Control: distinct prefixes", b, 0, 19);
    }

    Console.WriteLine($"\nSummary: {passed} passed, {failed} failed.");
    Console.WriteLine("Exit 1: incorrect row values detected. Exit 0: all row expectations met. Exit 2: setup/runtime exception.");
    return failed == 0 ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 2;
}

static ListDataSource<Row> CreateSource()
    => new(Enumerable.Range(0, 100).Select(id => new Row(id)), row => row.Id);

void InitialPageSize(InfiniteResourceOptions? options, string label)
{
    using var cache = new QueryCache();
    using var hook = new HookSession(cache, CreateSource());
    CheckRows(label, hook.Render(10, options), 0, 99);
}

void CheckRows(string label, InfiniteResource<Row> resource, int first, int last)
{
    resource.EnsureRange(first, last);
    var mismatches = new List<string>();
    for (int index = first; index <= last; index++)
    {
        int? actual = resource.ItemAt(index)?.Id;
        if (actual != index)
            mismatches.Add($"index {index}: expected {index}, actual {actual?.ToString() ?? "null"}");
    }
    bool correct = mismatches.Count == 0;
    if (correct) passed++; else failed++;
    Console.WriteLine($"  {(correct ? "PASS" : "FAIL")} {label}: {mismatches.Count} mismatched rows in [{first}, {last}]");
    foreach (string mismatch in mismatches.Take(3))
        Console.WriteLine($"    {mismatch}");
    if (first == 0 && last == 99)
        Console.WriteLine($"    index 50: expected 50, actual {resource.ItemAt(50)?.Id.ToString() ?? "null"}");
}

sealed record Row(int Id);

sealed class InlineDispatcher : IHookDispatcher
{
    public void Post(Action action) => action();
}

// These three lifecycle methods are internal. Reflection only drives the same
// hook lifecycle used by Reactor.Tests; data fetching and indexing use public APIs.
sealed class HookSession(QueryCache cache, IDataSource<Row> source) : IDisposable
{
    private static readonly MethodInfo BeginRender = typeof(RenderContext).GetMethod("BeginRender",
        BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Action)], null)!;
    private static readonly MethodInfo FlushEffects = typeof(RenderContext).GetMethod("FlushEffects",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo RunCleanups = typeof(RenderContext).GetMethod("RunCleanups",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly RenderContext _context = new();

    public InfiniteResource<Row> Render(int pageSize, InfiniteResourceOptions? options, string? search = null)
    {
        BeginRender.Invoke(_context, [(Action)(() => { })]);
        var resource = _context.UseDataSource(source,
            new DataRequest { PageSize = pageSize, SearchQuery = search }, cache,
            options, new InlineDispatcher());
        FlushEffects.Invoke(_context, null);
        return resource;
    }

    public void Dispose() => RunCleanups.Invoke(_context, null);
}
