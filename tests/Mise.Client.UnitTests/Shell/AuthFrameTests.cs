using Bunit;
using Mise.UI.Components;
using Mise.UI.Components.Shell;

namespace Mise.Client.UnitTests.Shell;

public class AuthFrameTests : MiseComponentTestContext
{
    [Fact]
    public void Render_ShowsRestaurantAndFrontOfHouse()
    {
        var cut = Render<AuthFrame>(p => p
            .Add(c => c.RestaurantName, "Lilshof")
            .AddChildContent("<p data-testid=\"auth-body\">Body</p>"));

        cut.Find($"[data-testid={TestIds.RestaurantName}]").TextContent.Should().Be("Lilshof · Front of House");
        cut.Find("[data-testid=auth-body]").Should().NotBeNull();
    }

    [Fact]
    public void Render_Dutch_ShowsZaal()
    {
        UseCulture("nl-BE");

        var cut = Render<AuthFrame>(p => p.Add(c => c.RestaurantName, "Lilshof"));

        cut.Find($"[data-testid={TestIds.RestaurantName}]").TextContent.Should().Be("Lilshof · Zaal");
    }
}

public class AuthCardTests : MiseComponentTestContext
{
    [Fact]
    public void Render_WithEyebrow_ShowsEyebrowAndTitle()
    {
        var cut = Render<AuthCard>(p => p
            .Add(c => c.Eyebrow, "Back office")
            .Add(c => c.Title, "Sign in to Mise")
            .Add(c => c.TitleTestId, TestIds.LoginHeading));

        cut.Find(".mise-eyebrow").TextContent.Should().Be("Back office");
        cut.Find($"[data-testid={TestIds.LoginHeading}]").TextContent.Should().Be("Sign in to Mise");
    }

    [Fact]
    public void Render_WithoutEyebrow_OmitsIt()
    {
        var cut = Render<AuthCard>(p => p.Add(c => c.Title, "Enter your PIN"));

        cut.FindAll(".mise-eyebrow").Should().BeEmpty();
        cut.Find($"[data-testid={TestIds.PageTitle}]").TextContent.Should().Be("Enter your PIN",
            because: "without an explicit TitleTestId the card title uses the shared page-title id.");
    }
}
