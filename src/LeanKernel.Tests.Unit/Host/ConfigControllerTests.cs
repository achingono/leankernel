using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Models.Admin;
using LeanKernel.Host.Services;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class ConfigControllerTests
{
    private static ConfigController MakeController(LeanKernelConfig? cfg = null, IRuntimeLeanKernelConfigStore? store = null)
    {
        var config = Options.Create(cfg ?? new LeanKernelConfig());
        return new ConfigController(config, store ?? new StubStore(cfg ?? new LeanKernelConfig()));
    }

    private sealed class StubStore : IRuntimeLeanKernelConfigStore
    {
        private LeanKernelConfig _current;
        public LeanKernelConfig? LastSaved { get; private set; }
        public StubStore(LeanKernelConfig initial) => _current = initial;
        public LeanKernelConfig GetCurrent() => _current;
        public Task SaveAsync(LeanKernelConfig config, CancellationToken ct)
        {
            LastSaved = config;
            _current = config;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void GetConfig_ReturnsOk()
    {
        var controller = MakeController();

        var result = controller.GetConfig();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void GetConfig_ReturnsAdminConfigResponse()
    {
        var controller = MakeController();

        var result = controller.GetConfig();
        var ok = Assert.IsType<OkObjectResult>(result);

        Assert.IsType<AdminConfigResponse>(ok.Value);
    }

    [Fact]
    public void GetConfig_MasksLiteLlmApiKey()
    {
        var controller = MakeController(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { ApiKey = "sk-test1234567890" }
        });

        var result = controller.GetConfig();
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminConfigResponse>(ok.Value);

        Assert.True(response.LiteLlm.ApiKey.Masked);
        Assert.NotEqual("sk-test1234567890", response.LiteLlm.ApiKey.Value?.ToString());
        Assert.StartsWith("sk-t", response.LiteLlm.ApiKey.Value?.ToString());
    }

    [Fact]
    public void GetConfig_ShortApiKey_FullyMasked()
    {
        var controller = MakeController(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { ApiKey = "short" }
        });

        var result = controller.GetConfig();
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminConfigResponse>(ok.Value);

        Assert.Equal("***", response.LiteLlm.ApiKey.Value?.ToString());
    }

    [Fact]
    public void GetConfig_EmptyApiKey_FullyMasked()
    {
        var controller = MakeController(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { ApiKey = "" }
        });

        var result = controller.GetConfig();
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminConfigResponse>(ok.Value);

        Assert.Equal("***", response.LiteLlm.ApiKey.Value?.ToString());
    }

    [Fact]
    public void BuildResponse_ExposesAllSections()
    {
        var cfg = new LeanKernelConfig();
        var response = ConfigController.BuildResponse(cfg);

        Assert.NotNull(response.LiteLlm);
        Assert.NotNull(response.Qdrant);
        Assert.NotNull(response.Signal);
        Assert.NotNull(response.Unstructured);
        Assert.NotNull(response.Wiki);
        Assert.NotNull(response.Agents);
        Assert.NotNull(response.Knowledge);
        Assert.NotNull(response.Context);
        Assert.NotNull(response.Scheduler);
        Assert.NotNull(response.Auth);
        Assert.NotNull(response.Routing);
        Assert.NotNull(response.Engagement);
        Assert.NotNull(response.Channels);
    }

    [Fact]
    public void BuildResponse_SignalSection_MasksAccount()
    {
        var cfg = new LeanKernelConfig
        {
            Signal = new SignalConfig { Account = "+15551234567" }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.True(response.Signal.Account.Masked);
        Assert.NotEqual("+15551234567", response.Signal.Account.Value?.ToString());
    }

    [Fact]
    public void BuildResponse_SignalSection_IncludesDaemonBaseUrl()
    {
        var cfg = new LeanKernelConfig
        {
            Signal = new SignalConfig { DaemonBaseUrl = "http://LeanKernel-signal:8080" }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal("http://LeanKernel-signal:8080", response.Signal.DaemonBaseUrl.Value?.ToString());
        Assert.False(response.Signal.DaemonBaseUrl.Masked);
    }

    [Fact]
    public void BuildResponse_SignalSection_IncludesAllowedSenders()
    {
        var cfg = new LeanKernelConfig
        {
            Signal = new SignalConfig { AllowedSenders = ["+15551111111", "+15552222222"] }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.NotNull(response.Signal.AllowedSenders.Value);
    }

    [Fact]
    public void BuildResponse_UnstructuredSection_ReflectsValues()
    {
        var cfg = new LeanKernelConfig
        {
            Unstructured = new UnstructuredConfig
            {
                Enabled = false,
                BaseUrl = "http://custom:8000",
                TimeoutSeconds = 60,
                SupportedMimeTypes = ["application/pdf", "image/png"],
                SupportedExtensions = [".pdf", ".png"]
            }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal(false, response.Unstructured.Enabled.Value);
        Assert.Equal("http://custom:8000", response.Unstructured.BaseUrl.Value?.ToString());
        Assert.Equal(60, response.Unstructured.TimeoutSeconds.Value);
        Assert.NotNull(response.Unstructured.SupportedMimeTypes.Value);
        Assert.NotNull(response.Unstructured.SupportedExtensions.Value);
    }

    [Fact]
    public void BuildResponse_RoutingSection_IncludesSpendGuard()
    {
        var cfg = new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                SpendGuard = new SpendGuardConfig
                {
                    DailyPaidRequestSoftLimit = 100,
                    DailyPaidRequestHardLimit = 200
                }
            }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal(100, response.Routing.SpendGuard.DailyPaidRequestSoftLimit.Value);
        Assert.Equal(200, response.Routing.SpendGuard.DailyPaidRequestHardLimit.Value);
    }

    [Fact]
    public void BuildResponse_AuthSection_IncludesOidcWithMaskedClientSecret()
    {
        var cfg = new LeanKernelConfig
        {
            Auth = new AuthConfig
            {
                Oidc = new OidcConfig { ClientId = "my-client", ClientSecret = "supersecret123" }
            }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal("my-client", response.Auth.Oidc.ClientId.Value?.ToString());
        Assert.True(response.Auth.Oidc.ClientSecret.Masked);
        Assert.True(response.Auth.Oidc.ClientSecret.EnvBacked);
        Assert.True(response.Auth.Oidc.ClientSecret.RestartRequired);
    }

    [Fact]
    public void BuildResponse_ChannelsSection_MasksSecrets()
    {
        var cfg = new LeanKernelConfig
        {
            SignalPhoneNumber = "+15551234567",
            SignalApiToken = "token-abc123456789",
            DiscordBotToken = "bot-xyz987654321",
            DiscordChannelId = "123456789",
            SignalServerUrl = "https://signal.example.com"
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.True(response.Channels.SignalPhoneNumber.Masked);
        Assert.True(response.Channels.SignalApiToken.Masked);
        Assert.True(response.Channels.DiscordBotToken.Masked);
        Assert.False(response.Channels.DiscordChannelId.Masked);
        Assert.Equal("https://signal.example.com", response.Channels.SignalServerUrl.Value?.ToString());
    }

    [Fact]
    public void BuildResponse_ChannelsSection_NullTokens_FullyMasked()
    {
        var cfg = new LeanKernelConfig
        {
            SignalPhoneNumber = null,
            SignalApiToken = null,
            DiscordBotToken = null
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal("***", response.Channels.SignalPhoneNumber.Value?.ToString());
        Assert.Equal("***", response.Channels.SignalApiToken.Value?.ToString());
        Assert.Equal("***", response.Channels.DiscordBotToken.Value?.ToString());
    }

    [Fact]
    public void BuildResponse_EngagementSection_IsNotMutable()
    {
        var cfg = new LeanKernelConfig();
        var response = ConfigController.BuildResponse(cfg);

        Assert.False(response.Engagement.SourceOfTruth.Mutable);
    }

    [Fact]
    public void BuildResponse_QdrantSection_IsRestartRequired()
    {
        var cfg = new LeanKernelConfig();
        var response = ConfigController.BuildResponse(cfg);

        Assert.True(response.Qdrant.RestartRequired);
        Assert.True(response.Qdrant.Host.RestartRequired);
        Assert.True(response.Qdrant.Port.RestartRequired);
    }

    [Fact]
    public void BuildResponse_ContextSection_ReflectsWeights()
    {
        var cfg = new LeanKernelConfig
        {
            Context = new ContextConfig
            {
                SemanticSimilarityWeight = 0.5,
                RecencyDecayWeight = 0.3,
                MinRelevanceThreshold = 0.7,
                MaxConversationTurns = 20,
                EntitySubjectBoost = 0.6,
                SupportingEntityThreshold = 0.4,
                EntityExpansionDepth = 2,
                LowConfidenceFallbackThreshold = 0.7,
                DeprioritizedRecallMaxResults = 30,
                AmbiguityLowConfidenceThreshold = 0.75,
                AmbiguityConfidenceGapThreshold = 0.12
            }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal(0.5, response.Context.SemanticSimilarityWeight.Value);
        Assert.Equal(0.3, response.Context.RecencyDecayWeight.Value);
        Assert.Equal(0.7, response.Context.MinRelevanceThreshold.Value);
        Assert.Equal(20, response.Context.MaxConversationTurns.Value);
        Assert.Equal(0.6, response.Context.EntitySubjectBoost.Value);
        Assert.Equal(0.4, response.Context.SupportingEntityThreshold.Value);
        Assert.Equal(2, response.Context.EntityExpansionDepth.Value);
        Assert.Equal(0.7, response.Context.LowConfidenceFallbackThreshold.Value);
        Assert.Equal(30, response.Context.DeprioritizedRecallMaxResults.Value);
        Assert.Equal(0.75, response.Context.AmbiguityLowConfidenceThreshold.Value);
        Assert.Equal(0.12, response.Context.AmbiguityConfidenceGapThreshold.Value);
    }

    [Fact]
    public void BuildResponse_AuthSection_ModeAsString()
    {
        var cfg = new LeanKernelConfig
        {
            Auth = new AuthConfig { Mode = AuthMode.Oidc }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal("Oidc", response.Auth.Mode.Value?.ToString());
        Assert.True(response.Auth.Mode.RestartRequired);
    }

    [Fact]
    public void BuildResponse_KnowledgeSection_IncludesAllFields()
    {
        var cfg = new LeanKernelConfig
        {
            Knowledge = new KnowledgeConfig
            {
                Enabled = false,
                CollectionName = "my_collection",
                DocumentsPath = "/custom/docs",
                DefaultDocumentTags = ["research", "internal"]
            }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal(false, response.Knowledge.Enabled.Value);
        Assert.Equal("my_collection", response.Knowledge.CollectionName.Value?.ToString());
        Assert.Equal("/custom/docs", response.Knowledge.DocumentsPath.Value?.ToString());
        Assert.NotNull(response.Knowledge.DefaultDocumentTags.Value);
    }

    [Fact]
    public void Field_Helper_SetsPropertiesCorrectly()
    {
        var field = ConfigController.Field("test-value", restartRequired: true, mutable: false, description: "test desc");

        Assert.Equal("test-value", field.Value);
        Assert.False(field.Masked);
        Assert.False(field.EnvBacked);
        Assert.True(field.RestartRequired);
        Assert.False(field.Mutable);
        Assert.Equal("test desc", field.Description);
    }

    [Fact]
    public void SecretField_Helper_MasksLongSecret()
    {
        var field = ConfigController.SecretField("sk-test123456", envBacked: true, restartRequired: true, description: "secret");

        Assert.True(field.Masked);
        Assert.True(field.EnvBacked);
        Assert.True(field.RestartRequired);
        Assert.NotEqual("sk-test123456", field.Value?.ToString());
        Assert.StartsWith("sk-t", field.Value?.ToString());
    }

    [Fact]
    public void SecretField_Helper_MasksShortSecret()
    {
        var field = ConfigController.SecretField("abc");

        Assert.True(field.Masked);
        Assert.Equal("***", field.Value?.ToString());
    }

    // ── PATCH / ApplyPatch ─────────────────────────────────────────────────

    [Fact]
    public void ApplyPatch_NullSections_ReturnsNoChanges()
    {
        var current = new LeanKernelConfig();
        var (updated, changes) = ConfigController.ApplyPatch(current, new AdminConfigPatchRequest());

        Assert.Empty(changes);
        Assert.Equal(current.LiteLlm.BaseUrl, updated.LiteLlm.BaseUrl);
    }

    [Fact]
    public void ApplyPatch_LiteLlmBaseUrl_RecordsChange()
    {
        var current = new LeanKernelConfig { LiteLlm = new LiteLlmConfig { BaseUrl = "http://old:4000" } };
        var patch = new AdminConfigPatchRequest
        {
            LiteLlm = new LiteLlmPatch { BaseUrl = "http://new:4000" }
        };

        var (updated, changes) = ConfigController.ApplyPatch(current, patch);

        Assert.Single(changes);
        Assert.Equal("LiteLlm", changes[0].Section);
        Assert.Equal("BaseUrl", changes[0].Field);
        Assert.Equal("http://old:4000", changes[0].OldValue);
        Assert.Equal("http://new:4000", changes[0].NewValue);
        Assert.Equal("http://new:4000", updated.LiteLlm.BaseUrl);
    }

    [Fact]
    public void ApplyPatch_LiteLlmApiKey_NotOverwritten()
    {
        var current = new LeanKernelConfig { LiteLlm = new LiteLlmConfig { ApiKey = "secret-key" } };
        var patch = new AdminConfigPatchRequest { LiteLlm = new LiteLlmPatch { BaseUrl = "http://x:4000" } };

        var (updated, _) = ConfigController.ApplyPatch(current, patch);

        // ApiKey must never be overwritten via PATCH
        Assert.Equal("secret-key", updated.LiteLlm.ApiKey);
    }

    [Fact]
    public void ApplyPatch_QdrantSection_MarksRestartRequired()
    {
        var current = new LeanKernelConfig { Qdrant = new QdrantConfig { Host = "qdrant", Port = 6334 } };
        var patch = new AdminConfigPatchRequest { Qdrant = new QdrantPatch { Host = "qdrant2" } };

        var (updated, changes) = ConfigController.ApplyPatch(current, patch);

        Assert.Single(changes);
        Assert.True(changes[0].RestartRequired);
        Assert.Equal("qdrant2", updated.Qdrant.Host);
    }

    [Fact]
    public void ApplyPatch_SignalSection_AllowedSendersUpdated()
    {
        var current = new LeanKernelConfig { Signal = new SignalConfig { AllowedSenders = ["+1"] } };
        var patch = new AdminConfigPatchRequest
        {
            Signal = new SignalPatch { AllowedSenders = ["+1", "+2"] }
        };

        var (updated, changes) = ConfigController.ApplyPatch(current, patch);

        Assert.Equal(["+1", "+2"], updated.Signal.AllowedSenders);
        Assert.Contains(changes, c => c.Field == "AllowedSenders");
    }

    [Fact]
    public void ApplyPatch_SignalAccount_NotOverwritten()
    {
        var current = new LeanKernelConfig { Signal = new SignalConfig { Account = "+15559999999" } };
        var patch = new AdminConfigPatchRequest { Signal = new SignalPatch { Enabled = false } };

        var (updated, _) = ConfigController.ApplyPatch(current, patch);

        Assert.Equal("+15559999999", updated.Signal.Account);
    }

    [Fact]
    public void ApplyPatch_UnstructuredSection_AppliesChanges()
    {
        var current = new LeanKernelConfig
        {
            Unstructured = new UnstructuredConfig
            {
                Enabled = true,
                TimeoutSeconds = 120,
                SupportedMimeTypes = ["application/pdf"],
                SupportedExtensions = [".pdf"]
            }
        };
        var patch = new AdminConfigPatchRequest
        {
            Unstructured = new UnstructuredPatch
            {
                Enabled = false,
                TimeoutSeconds = 60,
                SupportedMimeTypes = ["image/png", "image/jpeg"],
                SupportedExtensions = [".png", ".jpg"]
            }
        };

        var (updated, changes) = ConfigController.ApplyPatch(current, patch);

        Assert.False(updated.Unstructured.Enabled);
        Assert.Equal(60, updated.Unstructured.TimeoutSeconds);
        Assert.Equal(["image/png", "image/jpeg"], updated.Unstructured.SupportedMimeTypes);
        Assert.Equal([".png", ".jpg"], updated.Unstructured.SupportedExtensions);
        Assert.Equal(4, changes.Count);
    }

    [Fact]
    public void ApplyPatch_ContextWeights_AppliesChanges()
    {
        var current = new LeanKernelConfig { Context = new ContextConfig { SemanticSimilarityWeight = 0.4 } };
        var patch = new AdminConfigPatchRequest
        {
            Context = new ContextPatch
            {
                SemanticSimilarityWeight = 0.6,
                EntitySubjectBoost = 0.7,
                EntityExpansionDepth = 2,
                LowConfidenceFallbackThreshold = 0.66
            }
        };

        var (updated, changes) = ConfigController.ApplyPatch(current, patch);

        Assert.Equal(0.6, updated.Context.SemanticSimilarityWeight);
        Assert.Equal(0.7, updated.Context.EntitySubjectBoost);
        Assert.Equal(2, updated.Context.EntityExpansionDepth);
        Assert.Equal(0.66, updated.Context.LowConfidenceFallbackThreshold);
        Assert.Equal(4, changes.Count);
        Assert.Equal("Context", changes[0].Section);
    }

    [Fact]
    public void ApplyPatch_SameValue_NoChangeRecorded()
    {
        var current = new LeanKernelConfig { Scheduler = new SchedulerConfig { Enabled = true } };
        var patch = new AdminConfigPatchRequest { Scheduler = new SchedulerPatch { Enabled = true } };

        var (_, changes) = ConfigController.ApplyPatch(current, patch);

        Assert.Empty(changes);
    }

    [Fact]
    public void ApplyPatch_RoutingSpendGuard_AppliesLimits()
    {
        var current = new LeanKernelConfig
        {
            Routing = new RoutingConfig { SpendGuard = new SpendGuardConfig { DailyPaidRequestSoftLimit = 0 } }
        };
        var patch = new AdminConfigPatchRequest
        {
            Routing = new RoutingPatch { DailyPaidRequestSoftLimit = 100, DailyPaidRequestHardLimit = 200 }
        };

        var (updated, changes) = ConfigController.ApplyPatch(current, patch);

        Assert.Equal(100, updated.Routing.SpendGuard.DailyPaidRequestSoftLimit);
        Assert.Equal(200, updated.Routing.SpendGuard.DailyPaidRequestHardLimit);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void ApplyPatch_KnowledgeDefaultTags_AppliesChanges()
    {
        var current = new LeanKernelConfig { Knowledge = new KnowledgeConfig { DefaultDocumentTags = ["general"] } };
        var patch = new AdminConfigPatchRequest { Knowledge = new KnowledgePatch { DefaultDocumentTags = ["research", "internal"] } };

        var (updated, changes) = ConfigController.ApplyPatch(current, patch);

        Assert.Equal(["research", "internal"], updated.Knowledge.DefaultDocumentTags);
        Assert.Contains(changes, c => c.Field == "DefaultDocumentTags");
    }

    [Fact]
    public void ApplyPatch_PreservesChannelTokens()
    {
        var current = new LeanKernelConfig
        {
            DiscordBotToken = "bot-token-123456",
            SignalApiToken = "sig-token-789"
        };
        var patch = new AdminConfigPatchRequest { Scheduler = new SchedulerPatch { Enabled = false } };

        var (updated, _) = ConfigController.ApplyPatch(current, patch);

        Assert.Equal("bot-token-123456", updated.DiscordBotToken);
        Assert.Equal("sig-token-789", updated.SignalApiToken);
    }

    [Fact]
    public async Task PatchConfig_NoChanges_ReturnsOkWithEmptyChanges()
    {
        var cfg = new LeanKernelConfig();
        var store = new StubStore(cfg);
        var controller = MakeController(cfg, store);

        var result = await controller.PatchConfig(new AdminConfigPatchRequest(), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminConfigPatchResponse>(ok.Value);

        Assert.Empty(response.Changes);
        Assert.Null(store.LastSaved); // no save when no changes
    }

    [Fact]
    public async Task PatchConfig_WithChanges_SavesAndReturnsChanges()
    {
        var cfg = new LeanKernelConfig { LiteLlm = new LiteLlmConfig { BaseUrl = "http://old:4000" } };
        var store = new StubStore(cfg);
        var controller = MakeController(cfg, store);

        var result = await controller.PatchConfig(new AdminConfigPatchRequest
        {
            LiteLlm = new LiteLlmPatch { BaseUrl = "http://new:4000" }
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminConfigPatchResponse>(ok.Value);

        Assert.Single(response.Changes);
        Assert.NotNull(store.LastSaved);
        Assert.Equal("http://new:4000", store.LastSaved!.LiteLlm.BaseUrl);
        Assert.NotNull(response.UpdatedConfig);
    }
}
