using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

public sealed class ResourceCollisionReproTests(ITestOutputHelper output)
{
    private sealed class InlineDispatcher : IHookDispatcher
    {
        public void Post(Action action) => action();
    }

    [Fact]
    public void Changing_Query_From_1_32_To_2_1_Must_Fetch_The_New_Result()
    {
        using var cache = new QueryCache();
        var ctx = new RenderContext();
        int calls = 0;
        AsyncValue<string> Render(int workspaceId, int ticketId)
        {
            ctx.BeginRender(() => { });
            var result = ctx.UseResource(_ =>
            {
                calls++;
                return Task.FromResult($"{workspaceId}/{ticketId}");
            }, cache, [workspaceId, ticketId],
                new ResourceOptions(StaleTime: TimeSpan.FromMinutes(5)), new InlineDispatcher());
            ctx.FlushEffects();
            output.WriteLine($"query=[{workspaceId},{ticketId}], returned={result}, fetcher calls={calls}");
            return result;
        }
        try
        {
            Assert.Equal(new AsyncValue<string>.Data("1/32"), Render(1, 32));
            Assert.Equal(new AsyncValue<string>.Data("2/1"), Render(2, 1));
            Assert.Equal(2, calls);
        }
        finally { ctx.RunCleanups(); }
    }
}
