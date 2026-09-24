using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Mise.UI.Components;
using Mise.UI.Components.Gallery;

namespace Mise.Client.UnitTests.Gallery;

public class DesignGalleryTests : MiseComponentTestContext
{
    [Fact]
    public void Render_ShowsEveryTableAndReservationStatus()
    {
        var cut = Render<DesignGallery>();

        cut.FindAll($"[data-testid={TestIds.StatusPill}]").Should().HaveCount(10, because: "the gallery is the one place all five table and five reservation colour sets are visible side by side.");
        cut.FindAll($"[data-testid={TestIds.StatusDot}]").Should().HaveCount(5);
        cut.FindAll($"[data-testid={TestIds.EmptyState}]").Should().HaveCount(1);
    }

    [Fact]
    public void ClickOpenDrawer_OpensTheDrawer_AndCloseDismissesIt()
    {
        var cut = Render<DesignGallery>();

        cut.Find($"[data-testid={TestIds.GalleryOpenDrawer}]").Click();
        cut.FindAll($"[data-testid={TestIds.GalleryDrawer}]").Should().HaveCount(1);

        cut.Find($"[data-testid={TestIds.GalleryDrawer}] [data-testid={TestIds.Close}]").Click();
        cut.FindAll($"[data-testid={TestIds.GalleryDrawer}]").Should().BeEmpty();
    }

    [Fact]
    public void ClickOpenModal_OpensTheModal()
    {
        var cut = Render<DesignGallery>();

        cut.Find($"[data-testid={TestIds.GalleryOpenModal}]").Click();

        cut.Find($"[data-testid={TestIds.GalleryModal}] [data-testid={TestIds.OverlayTitle}]").TextContent.Should().Be("New reservation");
    }

    [Fact]
    public void StepperAndSegmentedControl_AreTwoWayBound_IntoTheToast()
    {
        var cut = Render<DesignGallery>();

        cut.Find($"[data-testid={TestIds.GalleryStepper}] [data-testid={TestIds.StepperIncrease}]").Click();
        cut.Find($"[data-testid={TestIds.Segment("patio")}]").Click();
        cut.Find($"[data-testid={TestIds.GalleryShowToast}]").Click();

        cut.Find($"[data-testid={TestIds.GalleryStepper}] [data-testid={TestIds.StepperValue}]").TextContent.Should().Be("3");
        cut.Find($"[data-testid={TestIds.Segment("patio")}]").GetAttribute("aria-pressed").Should().Be("true");
        Services.GetRequiredService<Mise.UI.Components.Feedback.ToastService>().Current!.Text.Should().Be("Party of 3 · patio");
    }
}
