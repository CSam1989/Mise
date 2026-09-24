using Mise.E2ETests.Pages.Components;
using Mise.UI.Components;

namespace Mise.E2ETests.Pages;

public sealed class DesignGalleryPage(IPage page)
{
    public const string Path = "/design";

    public AppShell Shell { get; } = new(page);

    public ILocator Root => page.GetByTestId(TestIds.DesignGallery);

    public ILocator StatusPills => page.GetByTestId(TestIds.StatusPill);

    public ILocator StatusDots => page.GetByTestId(TestIds.StatusDot);

    public ILocator EmptyState => page.GetByTestId(TestIds.EmptyState);

    public ILocator Sections => page.GetByTestId(TestIds.GallerySegmented);

    public Stepper Stepper { get; } = new(page.GetByTestId(TestIds.GalleryStepper));

    public Dialog Drawer { get; } = new(page, TestIds.GalleryDrawer);

    public Dialog Modal { get; } = new(page, TestIds.GalleryModal);

    public ILocator Segment(string key) => page.GetByTestId(TestIds.Segment(key));

    public async Task<DesignGalleryPage> GotoAsync()
    {
        await page.GotoAsync(Path);
        return this;
    }

    public Task ShowToastAsync() => page.GetByTestId(TestIds.GalleryShowToast).ClickAsync();

    public async Task<Dialog> OpenDrawerAsync()
    {
        await page.GetByTestId(TestIds.GalleryOpenDrawer).ClickAsync();
        return Drawer;
    }

    public async Task<Dialog> OpenModalAsync()
    {
        await page.GetByTestId(TestIds.GalleryOpenModal).ClickAsync();
        return Modal;
    }
}
