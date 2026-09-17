namespace Mise.E2ETests;

/// <summary>Shared by every E2E test that needs a signed-in staff member before it can reach
/// a protected page (Phase 3 — every page but Home now requires sign-in).</summary>
internal static class LoginSteps
{
    public static async Task SignInAsync(IPage page, string baseUrl, string username, string password)
    {
        await page.GotoAsync($"{baseUrl}/login");
        await page.Locator("[data-testid=input-username]").FillAsync(username);
        await page.Locator("[data-testid=input-password]").FillAsync(password);
        await page.Locator("[data-testid=btn-login]").ClickAsync();

        // Assert arrival, never timing (CLAUDE.md/plan.md's real-time testing convention
        // applies just as well to a plain redirect): the nav only shows the signed-in user
        // once the post-login forceLoad navigation has actually completed.
        await Assertions.Expect(page.Locator("[data-testid=nav-current-user]")).ToBeVisibleAsync();
    }
}
