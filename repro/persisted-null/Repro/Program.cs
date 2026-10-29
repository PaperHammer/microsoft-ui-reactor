using System.Reflection;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReproLog.Initialize(args);
AppDomain.CurrentDomain.UnhandledException += (_, error) =>
    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), error.ExceptionObject + Environment.NewLine);
if (args.Contains("--headless"))
{
    Environment.ExitCode = HeadlessRepro.Run();
    return;
}
ReactorApp.Run<ReproApp>($"UsePersisted optional filter - {ReproLog.Label}", width: 800, height: 600);

sealed class ReproApp : Component
{
    public override Element Render()
    {
        var (showTasks, setShowTasks) = UseState(true);
        return VStack(8,
            TextBlock($"Optional assignee filter | {ReproLog.Label}").FontSize(18),
            TextBlock("UsePersisted<string?>(\"tasks.assignee\", \"alice\", PersistedScope.Window)")
                .FontFamily("Consolas").FontSize(13),
            TextBlock("Steps: clear the filter, navigate away, then return."),
            HStack(10,
                Button("Navigate away", () =>
                {
                    ReproLog.Write("ACTION Navigate away");
                    setShowTasks(false);
                }),
                Button("Return to tasks", () =>
                {
                    var scope = ReactorApp.PrimaryWindow!.PersistedScope;
                    bool hit = scope.TryGet<string?>("tasks.assignee", out var value);
                    ReproLog.Write($"CACHE Count={scope.Count}; hit={hit}; value={ReproLog.Format(value)}");
                    ReproLog.Write("ACTION Return to tasks");
                    setShowTasks(true);
                })),
            showTasks ? Component<AssigneeFilter>() : TextBlock("Other page: task filter unmounted."),
            TextBlock("Expected after clear + return: All assignees; 4/4 tasks."),
            TextBlock("Actual output:"),
            TextBlock(ReproLog.Text).FontFamily("Consolas").FontSize(12).TextWrapping().Set(ReproLog.Attach)
        ).Padding(16);
    }
}

sealed class AssigneeFilter : Component
{
    private static readonly string[] AssigneeIds = ["alice", "bob"];
    private static readonly string[] AssigneeNames = ["Alice (current user)", "Bob"];

    public override Element Render()
    {
        var (assigneeId, setAssigneeId) = UsePersisted<string?>(
            "tasks.assignee", "alice", PersistedScope.Window);
        var visibleTasks = TaskData.Filter(assigneeId);
        UseEffect(() => ReproLog.Write(
            $"RENDER assigneeId={ReproLog.Format(assigneeId)}; visibleTasks={visibleTasks.Length}/{TaskData.All.Length}"), assigneeId);
        UseEffect(() => (Action)(() => ReproLog.Write("UNMOUNT Task filter")), Array.Empty<object>());

        return VStack(6,
            TextBlock("Assignee (optional)"),
            HStack(10,
                ComboBox(AssigneeNames, Array.IndexOf(AssigneeIds, assigneeId),
                    index => setAssigneeId(index < 0 ? null : AssigneeIds[index]))
                    .PlaceholderText("All assignees").AutomationName("Assignee filter").Width(230),
                Button("Clear filter", () =>
                {
                    ReproLog.Write("ACTION setAssigneeId(null)");
                    setAssigneeId(null);
                })),
            TextBlock($"Current assigneeId: {ReproLog.Format(assigneeId)}"),
            TextBlock($"Visible tasks: {visibleTasks.Length}/{TaskData.All.Length}").FontSize(16),
            TextBlock(string.Join(Environment.NewLine,
                visibleTasks.Select(task => $"- {task.Title} ({task.AssigneeId})")))
                .FontFamily("Consolas").FontSize(13).Height(72)
        );
    }
}

sealed record TaskItem(string Title, string AssigneeId);
static class TaskData
{
    public static readonly TaskItem[] All =
    [
        new("Review proposal", "alice"),
        new("Update documentation", "bob"),
        new("Fix login", "alice"),
        new("Add tests", "bob")
    ];
    public static TaskItem[] Filter(string? assigneeId) =>
        All.Where(task => assigneeId is null || task.AssigneeId == assigneeId).ToArray();
}
static class ReproLog
{
    private static readonly List<string> Lines = new();
    private static string _path = "";
    private static Microsoft.UI.Xaml.Controls.TextBlock? Output;
    public static void Attach(Microsoft.UI.Xaml.Controls.TextBlock output)
    {
        Output = output;
        RefreshOutput();
    }
    private static void RefreshOutput()
    {
        // Apply the latest log after reconciliation has assigned the element's Text snapshot.
        if (Output is { } output)
            output.DispatcherQueue.TryEnqueue(() => output.Text = Text);
    }
    public static string Label { get; } = typeof(ReproLog).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "ReproLabel").Value!;
    public static string Text => string.Join(Environment.NewLine, Lines);
    public static string Format<T>(T value) => value is null ? "null" : value is string text ? $"\"{text}\"" : value.ToString()!;
    public static void Initialize(string[] args)
    {
        int index = Array.IndexOf(args, "--log");
        _path = index >= 0 ? Path.GetFullPath(args[index + 1]) : Path.Combine(AppContext.BaseDirectory, "ui-output.log");
        File.WriteAllText(_path, $"Repro build: {Label}{Environment.NewLine}Reactor: {typeof(RenderContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}{Environment.NewLine}");
    }
    public static void Write(string message)
    {
        Lines.Add(message);
        File.AppendAllText(_path, message + Environment.NewLine);
        Console.WriteLine(message);
        RefreshOutput();
    }
}

static class HeadlessRepro
{
    private static readonly MethodInfo BeginRender = typeof(RenderContext).GetMethod("BeginRender",
        BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Action)], null)!;
    private static readonly MethodInfo RunCleanups = typeof(RenderContext).GetMethod("RunCleanups",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    public static int Run()
    {
        int failures = 0;
        foreach (var scope in new IPersistedStateScope[] { new WindowPersistedScope(8), new ApplicationPersistedScope(8) })
        {
            using (scope)
            {
                scope.Set<string?>("text", null);
                scope.Set<int?>("number", null);
                bool textHit = scope.TryGet<string?>("text", out var text);
                bool numberHit = scope.TryGet<int?>("number", out var number);
                ReproLog.Write($"{scope.GetType().Name}: Count={scope.Count}; string? hit={textHit}, value={ReproLog.Format(text)}; int? hit={numberHit}, value={ReproLog.Format(number)}");
                if (!textHit || text is not null) failures++;
                if (!numberHit || number is not null) failures++;
                if (scope.TryGet<string?>("missing", out _) || scope.TryGet<int>("number", out _))
                    throw new InvalidOperationException("Missing-key or non-nullable-type control failed.");
            }
        }
        failures += Roundtrip<string?>("assignee", "alice", null, id => $"visibleTasks={TaskData.Filter(id).Length}/{TaskData.All.Length}") ? 0 : 1;
        failures += Roundtrip<int?>("id", 7, null) ? 0 : 1;
        if (!Roundtrip<string?>("nonempty", "default", "saved") || !Roundtrip<string?>("empty", "default", "")
            || !Roundtrip<int?>("zero", 7, 0)) throw new InvalidOperationException("Non-null control failed.");
        ReproLog.Write($"RESULT: {failures} null-restoration failures; non-null and missing-key controls passed.");
        return failures == 0 ? 0 : 1;
    }
    private static bool Roundtrip<T>(string name, T initial, T saved, Func<T, string>? describe = null)
    {
        string key = "null-repro/" + name;
        ApplicationPersistedScope.Default.Remove(key);
        var first = new RenderContext();
        BeginRender.Invoke(first, [(Action)(() => { })]);
        first.UsePersisted(key, initial, PersistedScope.Application).Set(saved);
        BeginRender.Invoke(first, [(Action)(() => { })]);
        T before = first.UsePersisted(key, initial, PersistedScope.Application).Value;
        RunCleanups.Invoke(first, null);
        var second = new RenderContext();
        BeginRender.Invoke(second, [(Action)(() => { })]);
        T after = second.UsePersisted(key, initial, PersistedScope.Application).Value;
        string details = describe is null ? "" : $"; before: {describe(before)}; after: {describe(after)}";
        ReproLog.Write($"Hook {name}: saved={ReproLog.Format(saved)}, before unmount={ReproLog.Format(before)}, after remount={ReproLog.Format(after)}, expected={ReproLog.Format(saved)}{details}");
        RunCleanups.Invoke(second, null);
        ApplicationPersistedScope.Default.Remove(key);
        return EqualityComparer<T>.Default.Equals(before, saved) && EqualityComparer<T>.Default.Equals(after, saved);
    }
}