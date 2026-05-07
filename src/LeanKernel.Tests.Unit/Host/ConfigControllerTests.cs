using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Models.Admin;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class ConfigControllerTests
{
    [Fact]
    public void GetConfig_ReturnsOk()
    {
        var config = Options.Create(new LeanKernelConfig());
        var controller = new ConfigController(config);

        var result = controller.GetConfig();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void GetConfig_ReturnsAdminConfigResponse()
    {
        var config = Options.Create(new LeanKernelConfig());
        var controller = new ConfigController(config);

        var result = controller.GetConfig();
        var ok = Assert.IsType<OkObjectResult>(result);

        Assert.IsType<AdminConfigResponse>(ok.Value);
    }

    [Fact]
    public void GetConfig_MasksLiteLlmApiKey()
    {
        var config = Options.Create(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { ApiKey = "sk-test1234567890" }
        });
        var controller = new ConfigController(config);

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
        var config = Options.Create(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { ApiKey = "short" }
        });
        var controller = new ConfigController(config);

        var result = controller.GetConfig();
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminConfigResponse>(ok.Value);

        Assert.Equal("***", response.LiteLlm.ApiKey.Value?.ToString());
    }

    [Fact]
    public void GetConfig_EmptyApiKey_FullyMasked()
    {
        var config = Options.Create(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { ApiKey = "" }
        });
        var controller = new ConfigController(config);

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
            Unstructured = new UnstructuredConfig { Enabled = false, BaseUrl = "http://custom:8000", TimeoutSeconds = 60 }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal(false, response.Unstructured.Enabled.Value);
        Assert.Equal("http://custom:8000", response.Unstructured.BaseUrl.Value?.ToString());
        Assert.Equal(60, response.Unstructured.TimeoutSeconds.Value);
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
                MaxConversationTurns = 20
            }
        };
        var response = ConfigController.BuildResponse(cfg);

        Assert.Equal(0.5, response.Context.SemanticSimilarityWeight.Value);
        Assert.Equal(0.3, response.Context.RecencyDecayWeight.Value);
        Assert.Equal(0.7, response.Context.MinRelevanceThreshold.Value);
        Assert.Equal(20, response.Context.MaxConversationTurns.Value);
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
}

