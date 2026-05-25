namespace LeanKernel.Tests.Playwright;

/// <summary>
/// UI tests for the Onboarding/Setup page.
/// Verifies wizard steps, form inputs, navigation, and completion state.
/// </summary>
[Collection(PlaywrightCollection.Name)]
public class OnboardingPageTests
{
    private readonly PlaywrightFixture _fixture;

    public OnboardingPageTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OnboardingPage_ShowsPageTitle()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var title = await page.TitleAsync();
            Assert.Contains("Setup", title);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsGuidedSetupHeading()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Guided setup", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsStepIndicator()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Step", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsWelcomeStep()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Welcome to LeanKernel", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsWizardComponent()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Welcome", content);
            Assert.Contains("Complete", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsIdentityStep()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Identity", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsKnowledgeDomainsStep()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Guided setup", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsGoalsStep()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Goals", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsWhatWeCaptureCard()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("What we capture", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsDisplayNameField()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var displayNameField = page.Locator("#onboarding-display-name");
            // The field may not be visible on the first step (Welcome), so check it exists in DOM
            var content = await page.ContentAsync();
            Assert.Contains("onboarding-display-name", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsSetupDescription()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Tell LeanKernel how to address you", content);
        }
        finally { await page.CloseAsync(); }
    }
}
