using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<Demo>("UseResource dependency collision", width: 660, height: 300);

internal sealed class Demo : Component
{
    private int _calls;

    public override Element Render()
    {
        var (query, setQuery) = UseState((Workspace: 1, Ticket: 32));
        var result = UseResource(_ =>
        {
            _calls++;
            return Task.FromResult($"{query.Workspace}/{query.Ticket}");
        }, deps: [query.Workspace, query.Ticket],
            options: new ResourceOptions(StaleTime: TimeSpan.FromMinutes(5)));

        return VStack(12,
            Button("Switch query to [2, 1]", () => setQuery((2, 1))),
            TextBlock($"Selected: [{query.Workspace}, {query.Ticket}]").FontSize(22),
            TextBlock($"Returned: {result}").FontSize(22),
            TextBlock($"Fetcher calls: {_calls}").FontSize(22)
        ).Padding(24);
    }
}
