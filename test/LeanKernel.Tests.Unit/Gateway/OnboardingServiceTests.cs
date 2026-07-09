using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Gateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LeanKernel.Tests.Unit.Gateway;

public class OnboardingServiceTests
{
    private readonly Mock<IKnowledgeService> _knowledgeServiceMock = new(MockBehavior.Strict);
    private readonly Mock<IOnboardingDetector> _detectorMock = new(MockBehavior.Strict);
    private readonly OnboardingService _sut;

    public OnboardingServiceTests()
    {
        _sut = new OnboardingService(
            _knowledgeServiceMock.Object,
            _detectorMock.Object,
            NullLogger<OnboardingService>.Instance);
    }

    // ===================== Helpers =====================

    private void SetupGetPage(string key, KnowledgePage? page)
    {
        _knowledgeServiceMock
            .Setup(m => m.GetPageAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);
    }

    private void SetupPutPage(string key)
    {
        _knowledgeServiceMock
            .Setup(m => m.PutPageAsync(key, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupPutPageThrows(string key, Exception ex)
    {
        _knowledgeServiceMock
            .Setup(m => m.PutPageAsync(key, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);
    }

    private void SetupDetectGaps(OnboardingResult result)
    {
        _detectorMock
            .Setup(m => m.DetectGapsAsync(It.IsAny<IdentityContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private void VerifyAllMocks()
    {
        _knowledgeServiceMock.VerifyAll();
        _detectorMock.VerifyAll();
    }

    private static KnowledgePage ProfilePage(Action<IDictionary<string, string>>? configureFrontmatter = null)
    {
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["page_type"] = "user-profile",
            ["user_id"] = "user1",
            ["preferred_name"] = "John",
            ["role"] = "Engineer",
            ["communication_style"] = "concise",
            ["timezone"] = "America/New_York",
            ["preferred_language"] = "en-US",
        };
        configureFrontmatter?.Invoke(frontmatter);

        var yaml = string.Join("\n", frontmatter.Select(kvp => $"{kvp.Key}: \"{kvp.Value}\""));
        return new KnowledgePage
        {
            Key = OnboardingService.UserProfilePageKey,
            Content = $"---\n{yaml}\n---\n\n# User Profile\n\n- Display name: John\n"
        };
    }

    private static KnowledgePage GoalsPage(Action<IDictionary<string, string>>? configureFrontmatter = null)
    {
        var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["page_type"] = "user-goals",
            ["user_id"] = "user1",
            ["recurring_goals"] = "learn C#; write tests",
        };
        configureFrontmatter?.Invoke(frontmatter);

        var yaml = string.Join("\n", frontmatter.Select(kvp => $"{kvp.Key}: \"{kvp.Value}\""));
        return new KnowledgePage
        {
            Key = OnboardingService.UserGoalsPageKey,
            Content = $"---\n{yaml}\n---\n\n# User Goals\n\n## Knowledge Domains\n- AI\n- Testing\n\n## Goals\n- learn C#\n- write tests\n\n## Other Goals\nBecome a better developer\n"
        };
    }

    private static readonly IdentityGap NameGap = new() { FieldName = "preferred_name", GapCode = "missing", Reason = "Not provided" };
    private static readonly IdentityGap StyleGap = new() { FieldName = "communication_style", GapCode = "missing", Reason = null };
    private static readonly IdentityGap UnsupportedGap = new() { FieldName = "unknown_field", GapCode = "missing", Reason = "Some reason" };

    // ===================== LoadAsync =====================

    [Fact]
    public async Task LoadAsync_null_userId_throws_ArgumentException()
    {
        var act = () => _sut.LoadAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_empty_userId_throws_ArgumentException()
    {
        var act = () => _sut.LoadAsync("", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_whitespace_userId_throws_ArgumentException()
    {
        var act = () => _sut.LoadAsync("   ", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_no_existing_pages_returns_empty_draft_with_gaps()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, null);
        SetupGetPage(OnboardingService.UserGoalsPageKey, null);
        SetupDetectGaps(new OnboardingResult { Gaps = [NameGap] });

        var result = await _sut.LoadAsync("user1", CancellationToken.None);

        result.HasExistingProfile.Should().BeFalse();
        result.Draft.DisplayName.Should().BeEmpty();
        result.Draft.RoleTitle.Should().BeEmpty();
        result.Draft.CommunicationStyle.Should().Be("balanced");
        result.Draft.Timezone.Should().BeEmpty();
        result.Draft.PreferredLanguage.Should().BeEmpty();
        result.Draft.Domains.Should().BeEmpty();
        result.Draft.Goals.Should().BeEmpty();
        result.Draft.OtherGoals.Should().BeEmpty();
        result.Gaps.Should().ContainSingle()
            .Which.FieldName.Should().Be("preferred_name");
    }

    [Fact]
    public async Task LoadAsync_with_profile_page_parses_frontmatter_into_draft_fields()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, ProfilePage());
        SetupGetPage(OnboardingService.UserGoalsPageKey, null);
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.LoadAsync("user1", CancellationToken.None);

        result.HasExistingProfile.Should().BeTrue();
        result.Draft.DisplayName.Should().Be("John");
        result.Draft.RoleTitle.Should().Be("Engineer");
        result.Draft.CommunicationStyle.Should().Be("concise");
        result.Draft.Timezone.Should().Be("America/New_York");
        result.Draft.PreferredLanguage.Should().Be("en-US");
    }

    [Fact]
    public async Task LoadAsync_with_profile_page_uses_fallback_keys_when_preferred_missing()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, ProfilePage(fm =>
        {
            fm.Remove("preferred_name");
            fm["name"] = "Alice";
        }));
        SetupGetPage(OnboardingService.UserGoalsPageKey, null);
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.LoadAsync("user1", CancellationToken.None);

        result.Draft.DisplayName.Should().Be("Alice");
        result.Draft.RoleTitle.Should().Be("Engineer");
    }

    [Fact]
    public async Task LoadAsync_with_goals_page_parses_body_sections()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, null);
        SetupGetPage(OnboardingService.UserGoalsPageKey, GoalsPage());
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.LoadAsync("user1", CancellationToken.None);

        result.HasExistingProfile.Should().BeTrue();
        result.Draft.Domains.Should().BeEquivalentTo(["AI", "Testing"]);
        result.Draft.Goals.Should().BeEquivalentTo(["learn C#", "write tests"]);
        result.Draft.OtherGoals.Should().Be("Become a better developer");
    }

    [Fact]
    public async Task LoadAsync_with_both_pages_merges_full_draft()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, ProfilePage());
        SetupGetPage(OnboardingService.UserGoalsPageKey, GoalsPage());
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.LoadAsync("user1", CancellationToken.None);

        result.HasExistingProfile.Should().BeTrue();
        result.Draft.DisplayName.Should().Be("John");
        result.Draft.RoleTitle.Should().Be("Engineer");
        result.Draft.CommunicationStyle.Should().Be("concise");
        result.Draft.Timezone.Should().Be("America/New_York");
        result.Draft.PreferredLanguage.Should().Be("en-US");
        result.Draft.Domains.Should().BeEquivalentTo(["AI", "Testing"]);
        result.Draft.Goals.Should().BeEquivalentTo(["learn C#", "write tests"]);
        result.Draft.OtherGoals.Should().Be("Become a better developer");
    }

    // ===================== DetectGapsAsync =====================

    [Fact]
    public async Task DetectGapsAsync_null_userId_throws_ArgumentException()
    {
        var act = () => _sut.DetectGapsAsync(null!, new OnboardingDraft(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DetectGapsAsync_null_draft_throws_ArgumentNullException()
    {
        var act = () => _sut.DetectGapsAsync("user1", null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DetectGapsAsync_returns_mapped_insights()
    {
        SetupDetectGaps(new OnboardingResult
        {
            Gaps = [NameGap, StyleGap]
        });

        var draft = new OnboardingDraft
        {
            DisplayName = "John",
            CommunicationStyle = "concise",
            Timezone = "America/New_York",
            PreferredLanguage = "en-US",
            Goals = ["learn C#"]
        };

        var result = await _sut.DetectGapsAsync("user1", draft, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].FieldName.Should().Be("preferred_name");
        result[0].Title.Should().Be("Add your display name");
        result[0].Detail.Should().StartWith("LeanKernel uses it");
        result[1].FieldName.Should().Be("communication_style");
        result[1].Title.Should().Be("Choose a communication style");
        result[1].Detail.Should().StartWith("This keeps replies");
    }

    [Fact]
    public async Task DetectGapsAsync_empty_result_returns_empty_list()
    {
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.DetectGapsAsync("user1", new OnboardingDraft(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectGapsAsync_unsupported_fields_are_filtered_out()
    {
        SetupDetectGaps(new OnboardingResult
        {
            Gaps = [NameGap, UnsupportedGap]
        });

        var result = await _sut.DetectGapsAsync("user1", new OnboardingDraft
        {
            DisplayName = "John"
        }, CancellationToken.None);

        result.Should().ContainSingle()
            .Which.FieldName.Should().Be("preferred_name");
    }

    [Fact]
    public async Task DetectGapsAsync_empty_draft_fields_are_excluded_from_identity_context()
    {
        var capturedContexts = new List<IdentityContext>();
        _detectorMock
            .Setup(m => m.DetectGapsAsync(It.IsAny<IdentityContext>(), It.IsAny<CancellationToken>()))
            .Callback<IdentityContext, CancellationToken>((ctx, _) => capturedContexts.Add(ctx))
            .ReturnsAsync(new OnboardingResult());

        var draft = new OnboardingDraft(); // all fields empty except CommunicationStyle defaults to "balanced"

        await _sut.DetectGapsAsync("user1", draft, CancellationToken.None);

        var context = capturedContexts.Should().ContainSingle().Subject;
        context.UserPreferences!.Fields.Keys.Should().ContainSingle("communication_style");
        context.UserPreferences.Fields["communication_style"].Value.Should().Be("balanced");
    }

    // ===================== SaveAsync =====================

    [Fact]
    public async Task SaveAsync_null_userId_throws_ArgumentException()
    {
        var act = () => _sut.SaveAsync(null!, new OnboardingDraft(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SaveAsync_null_draft_throws_ArgumentNullException()
    {
        var act = () => _sut.SaveAsync("user1", null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SaveAsync_no_existing_pages_writes_both_and_returns_success()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, null);
        SetupGetPage(OnboardingService.UserGoalsPageKey, null);
        SetupPutPage(OnboardingService.UserProfilePageKey);
        SetupPutPage(OnboardingService.UserGoalsPageKey);
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.SaveAsync("user1", new OnboardingDraft(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Profile saved to GBrain wiki pages.");
        result.Errors.Should().BeEmpty();

        _knowledgeServiceMock.Verify(m => m.PutPageAsync(OnboardingService.UserProfilePageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _knowledgeServiceMock.Verify(m => m.PutPageAsync(OnboardingService.UserGoalsPageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_with_changed_existing_content_calls_PutPageAsync_for_both_pages()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, new KnowledgePage
        {
            Key = OnboardingService.UserProfilePageKey,
            Content = "different content"
        });
        SetupGetPage(OnboardingService.UserGoalsPageKey, new KnowledgePage
        {
            Key = OnboardingService.UserGoalsPageKey,
            Content = "different content"
        });
        SetupPutPage(OnboardingService.UserProfilePageKey);
        SetupPutPage(OnboardingService.UserGoalsPageKey);
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.SaveAsync("user1", new OnboardingDraft(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _knowledgeServiceMock.Verify(m => m.PutPageAsync(OnboardingService.UserProfilePageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _knowledgeServiceMock.Verify(m => m.PutPageAsync(OnboardingService.UserGoalsPageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_profile_page_failure_captures_error_and_continues()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, null);
        SetupPutPageThrows(OnboardingService.UserProfilePageKey, new InvalidOperationException("Profile write failed"));
        SetupGetPage(OnboardingService.UserGoalsPageKey, null);
        SetupPutPage(OnboardingService.UserGoalsPageKey);
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.SaveAsync("user1", new OnboardingDraft(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("The user profile page could not be saved.");
        result.Errors.Should().ContainSingle()
            .Which.Should().Be("The user profile page could not be saved.");
    }

    [Fact]
    public async Task SaveAsync_goals_page_failure_captures_error()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, null);
        SetupPutPage(OnboardingService.UserProfilePageKey);
        SetupGetPage(OnboardingService.UserGoalsPageKey, null);
        SetupPutPageThrows(OnboardingService.UserGoalsPageKey, new InvalidOperationException("Goals write failed"));
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.SaveAsync("user1", new OnboardingDraft(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("The user goals page could not be saved.");
        result.Errors.Should().ContainSingle()
            .Which.Should().Be("The user goals page could not be saved.");
    }

    [Fact]
    public async Task SaveAsync_both_pages_fail_aggregates_errors()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, null);
        SetupPutPageThrows(OnboardingService.UserProfilePageKey, new InvalidOperationException("Profile failed"));
        SetupGetPage(OnboardingService.UserGoalsPageKey, null);
        SetupPutPageThrows(OnboardingService.UserGoalsPageKey, new InvalidOperationException("Goals failed"));
        SetupDetectGaps(new OnboardingResult());

        var result = await _sut.SaveAsync("user1", new OnboardingDraft(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("The onboarding profile could not be saved completely.");
        result.Errors.Should().HaveCount(2);
        result.Errors[0].Should().Be("The user profile page could not be saved.");
        result.Errors[1].Should().Be("The user goals page could not be saved.");
    }

    [Fact]
    public async Task SaveAsync_returns_gaps_from_detector()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, null);
        SetupGetPage(OnboardingService.UserGoalsPageKey, null);
        SetupPutPage(OnboardingService.UserProfilePageKey);
        SetupPutPage(OnboardingService.UserGoalsPageKey);
        SetupDetectGaps(new OnboardingResult
        {
            Gaps = [NameGap]
        });

        var result = await _sut.SaveAsync("user1", new OnboardingDraft(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Gaps.Should().ContainSingle()
            .Which.FieldName.Should().Be("preferred_name");
    }

    [Fact]
    public async Task SaveAsync_normalizes_draft_before_saving()
    {
        SetupGetPage(OnboardingService.UserProfilePageKey, null);
        SetupGetPage(OnboardingService.UserGoalsPageKey, null);

        string? capturedProfileContent = null;
        string? capturedGoalsContent = null;
        _knowledgeServiceMock
            .Setup(m => m.PutPageAsync(OnboardingService.UserProfilePageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) => capturedProfileContent = content)
            .Returns(Task.CompletedTask);
        _knowledgeServiceMock
            .Setup(m => m.PutPageAsync(OnboardingService.UserGoalsPageKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) => capturedGoalsContent = content)
            .Returns(Task.CompletedTask);
        SetupDetectGaps(new OnboardingResult());

        var draft = new OnboardingDraft
        {
            DisplayName = "  John  ",
            CommunicationStyle = "",
            Goals = ["learn  C#", "learn  c#"]
        };

        await _sut.SaveAsync("user1", draft, CancellationToken.None);

        // Normalization trims display name, sets communication style to "balanced", deduplicates goals
        capturedProfileContent.Should().NotBeNull();
        capturedProfileContent.Should().Contain("preferred_name: \"John\"");
        capturedProfileContent.Should().Contain("communication_style: \"balanced\"");

        capturedGoalsContent.Should().NotBeNull();
        // Duplicate "learn  c#" should be normalized to "learn C#" (first occurrence wins) and deduplicated
        capturedGoalsContent.Should().Contain("recurring_goals: \"learn C#\"");
    }
}
