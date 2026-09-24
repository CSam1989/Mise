using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;

namespace Mise.UI.Components.Primitives;

public abstract class OverlayBase : ComponentBase
{
    private bool _focusPending;
    private bool _wasOpen;

    [Inject]
    protected IStringLocalizer<UiStrings> L { get; set; } = default!;

    [Parameter]
    public bool IsOpen { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    [Parameter, EditorRequired]
    public string Title { get; set; } = default!;

    [Parameter, EditorRequired]
    public string TestId { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public RenderFragment? Footer { get; set; }

    protected ElementReference Panel { get; set; }

    protected string TitleId { get; } = $"overlay-title-{Guid.NewGuid():N}";

    protected override void OnParametersSet()
    {
        if (IsOpen && !_wasOpen)
        {
            _focusPending = true;
        }

        _wasOpen = IsOpen;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusPending && IsOpen)
        {
            _focusPending = false;
            await Panel.FocusAsync();
        }
    }

    protected async Task FocusPanelAsync() => await Panel.FocusAsync();

    protected Task CloseAsync() => OnClose.InvokeAsync();

    protected Task HandleKeyDownAsync(KeyboardEventArgs e) => e.Key == "Escape" ? CloseAsync() : Task.CompletedTask;
}
