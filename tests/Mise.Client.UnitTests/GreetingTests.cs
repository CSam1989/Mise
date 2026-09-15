using Bunit;
using Mise.Client.UnitTests.TestComponents;

namespace Mise.Client.UnitTests;

/// <summary>Proves the bUnit harness itself renders and queries by data-testid, before Mise.UI.Components exists.</summary>
public class GreetingTests : BunitContext
{
    [Fact]
    public void Render_WithName_ShowsGreetingByDataTestId()
    {
        var cut = Render<Greeting>(parameters => parameters.Add(p => p.Name, "Chef"));

        cut.Find("[data-testid=greeting]").TextContent.Should().Be("Hello, Chef!");
    }
}
