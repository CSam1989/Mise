namespace Mise.E2ETests;

[Trait("Category", "E2E")]
[Collection(MiseE2ECollection.Name)]
public class DesignGalleryE2ETests(MiseE2EFixture fixture) : BrowserTest(fixture)
{
    private async Task<DesignGalleryPage> OpenGalleryAsync() =>
        await new DesignGalleryPage(await OpenPageAsync(fixture.ManagerSession)).GotoAsync();

    [Fact]
    public async Task Gallery_InDevelopment_RendersEveryStatusAndAnEmptyState()
    {
        var gallery = await OpenGalleryAsync();

        await Assertions.Expect(gallery.Root).ToBeVisibleAsync();
        await Assertions.Expect(gallery.StatusPills).ToHaveCountAsync(10);
        await Assertions.Expect(gallery.StatusDots).ToHaveCountAsync(5);
        await Assertions.Expect(gallery.EmptyState).ToBeVisibleAsync();
    }

    [Fact]
    public async Task StepperAndSegments_Change_AndTheToastReflectsThem()
    {
        var gallery = await OpenGalleryAsync();

        await gallery.Stepper.Increase.ClickAsync();
        await gallery.Segment("patio").ClickAsync();
        await gallery.ShowToastAsync();

        await Assertions.Expect(gallery.Stepper.Value).ToHaveTextAsync("3");
        await Assertions.Expect(gallery.Stepper.Root).ToHaveRoleAsync(AriaRole.Group);
        await Assertions.Expect(gallery.Stepper.Increase).ToHaveAccessibleNameAsync("Increase Party");
        await Assertions.Expect(gallery.Segment("patio")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(gallery.Shell.Toast).ToHaveTextAsync("Party of 3 · patio");
        await Assertions.Expect(gallery.Shell.Toast).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Drawer_Opened_IsANamedDialogThatTrapsTabAndClosesOnEscape()
    {
        var gallery = await OpenGalleryAsync();
        var page = gallery.Drawer.Root.Page;

        var drawer = await gallery.OpenDrawerAsync();

        await Assertions.Expect(drawer.Root).ToHaveRoleAsync(AriaRole.Dialog);
        await Assertions.Expect(drawer.Root).ToHaveAccessibleNameAsync("M12 · Main room");
        await Assertions.Expect(drawer.Root).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(drawer.CloseButton).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(drawer.Root).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(drawer.CloseButton).ToBeFocusedAsync();

        await drawer.CloseWithEscapeAsync();

        await Assertions.Expect(drawer.Root).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Modal_ClickOutside_Closes()
    {
        var gallery = await OpenGalleryAsync();

        var modal = await gallery.OpenModalAsync();
        await Assertions.Expect(modal.Root).ToHaveAccessibleNameAsync("New reservation");

        await modal.CloseByClickingOutsideAsync();

        await Assertions.Expect(modal.Root).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Drawer_CloseButton_HasAnAccessibleNameAndCloses()
    {
        var gallery = await OpenGalleryAsync();
        var drawer = await gallery.OpenDrawerAsync();

        await Assertions.Expect(drawer.CloseButton).ToHaveAccessibleNameAsync("Close");
        await drawer.CloseButton.ClickAsync();

        await Assertions.Expect(drawer.Root).ToHaveCountAsync(0);
    }
}
