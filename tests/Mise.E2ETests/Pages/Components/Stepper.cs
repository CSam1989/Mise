using Mise.UI.Components;

namespace Mise.E2ETests.Pages.Components;

public sealed class Stepper(ILocator root)
{
    public ILocator Root => root;

    public ILocator Value => root.GetByTestId(TestIds.StepperValue);

    public ILocator Increase => root.GetByTestId(TestIds.StepperIncrease);

    public ILocator Decrease => root.GetByTestId(TestIds.StepperDecrease);
}
