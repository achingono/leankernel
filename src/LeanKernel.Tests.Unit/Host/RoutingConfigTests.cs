using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Models.Routing;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class RoutingConfigTests
{
    // ── LiteLlmRoutingConfigService tests ─────────────────────────────────────

    private static LiteLlmRoutingConfig SimpleConfig() => new()
    {
        Providers = new Dictionary<string, ProviderSpec>
        {
            ["groq"] = new ProviderSpec
            {
                Keys = [new KeySlot { Source = "env", Name = "GROQ_API_KEY" }],
                Models = [new ModelSpec { Id = "scout", Name = "llama-4-scout", MaxTokens = 8192 }]
            },
            ["gemini"] = new ProviderSpec
            {
                Keys = [new KeySlot { Source = "env", Name = "GEMINI_API_KEY" }],
                Models =
                [
                    new ModelSpec { Id = "flash", Name = "gemini-2.5-flash", MaxTokens = 8192 },
                    new ModelSpec { Id = "embedding_2", Name = "gemini-embedding-2", Mode = "embedding", Dimensions = 1536 }
                ]
            }
        },
        Routes = new Dictionary<string, List<RouteEntry>>
        {
            ["small"] =
            [
                new RouteEntry { Provider = "groq", Model = "scout", Keys = ["groq1"], Order = 1 }
            ],
            ["embedding-large"] =
            [
                new RouteEntry { Provider = "gemini", Model = "embedding_2", Keys = ["gemini1"], Order = 1 }
            ]
        },
        Aliases = new Dictionary<string, string>
        {
            ["gpt-4o-mini"] = "small"
        },
        Router = new RouterPolicy { RoutingStrategy = "least-busy", NumRetries = 3 }
    };

    [Fact]
    public void Validate_ValidConfig_ReturnsNoErrors()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");
        var config = SimpleConfig();

        var errors = service.Validate(config);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_UnknownProvider_ReturnsError()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");
        var config = SimpleConfig();
        config.Routes["small"].Add(new RouteEntry { Provider = "openai", Model = "gpt4", Keys = [], Order = 2 });

        var errors = service.Validate(config);

        Assert.Contains(errors, e => e.Code == "UNKNOWN_PROVIDER");
    }

    [Fact]
    public void Validate_UnknownModel_ReturnsError()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");
        var config = SimpleConfig();
        config.Routes["small"].Add(new RouteEntry { Provider = "groq", Model = "nonexistent", Keys = [], Order = 2 });

        var errors = service.Validate(config);

        Assert.Contains(errors, e => e.Code == "UNKNOWN_MODEL");
    }

    [Fact]
    public void Validate_InvalidAliasTarget_ReturnsError()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");
        var config = SimpleConfig();
        config.Aliases["bad-alias"] = "nonexistent-route";

        var errors = service.Validate(config);

        Assert.Contains(errors, e => e.Code == "INVALID_ALIAS_TARGET");
    }

    [Fact]
    public void Validate_DuplicateOrder_ReturnsWarning()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");
        var config = SimpleConfig();
        config.Routes["small"].Add(new RouteEntry { Provider = "groq", Model = "scout", Keys = ["groq1"], Order = 1 }); // duplicate order=1

        var errors = service.Validate(config);

        Assert.Contains(errors, e => e.Code == "DUPLICATE_ORDER" && e.Severity == "warning");
    }

    [Fact]
    public void GetKeyStatuses_ReturnsOneStatusPerKeySlot()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");
        var config = SimpleConfig();

        var statuses = service.GetKeyStatuses(config);

        // SimpleConfig has 2 providers with 1 key slot each → 2 statuses
        Assert.Equal(2, statuses.Count);
    }

    [Fact]
    public void GetKeyStatuses_EnvVarPresent_MarksAsConfigured()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");
        var config = new LiteLlmRoutingConfig
        {
            Providers = new Dictionary<string, ProviderSpec>
            {
                ["test"] = new ProviderSpec
                {
                    Keys = [new KeySlot { Source = "env", Name = "LEANKERNEL_TEST_ADM04_KEY" }],
                    Models = []
                }
            }
        };

        Environment.SetEnvironmentVariable("LEANKERNEL_TEST_ADM04_KEY", "dummy");
        var statuses = service.GetKeyStatuses(config);
        Environment.SetEnvironmentVariable("LEANKERNEL_TEST_ADM04_KEY", null);

        Assert.Single(statuses);
        Assert.True(statuses[0].Configured);
    }

    [Fact]
    public void GetKeyStatuses_EnvVarMissing_MarksAsNotConfigured()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");
        var config = new LiteLlmRoutingConfig
        {
            Providers = new Dictionary<string, ProviderSpec>
            {
                ["test"] = new ProviderSpec
                {
                    Keys = [new KeySlot { Source = "env", Name = "LEANKERNEL_ADM04_NOT_SET_9999" }],
                    Models = []
                }
            }
        };

        var statuses = service.GetKeyStatuses(config);

        Assert.Single(statuses);
        Assert.False(statuses[0].Configured);
    }

    [Fact]
    public void GenerateYaml_ProducesNonEmptyString()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");
        var config = SimpleConfig();

        var yaml = service.GenerateYaml(config);

        Assert.NotEmpty(yaml);
        Assert.Contains("groq", yaml);
    }

    [Fact]
    public void ComputeDiff_IdenticalYaml_ReturnsEmpty()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");

        var diff = service.ComputeDiff("line1\nline2", "line1\nline2");

        Assert.Empty(diff.Trim());
    }

    [Fact]
    public void ComputeDiff_ChangedLine_ShowsMinusAndPlus()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");

        var diff = service.ComputeDiff("line1\nold", "line1\nnew");

        Assert.Contains("- old", diff);
        Assert.Contains("+ new", diff);
    }

    [Fact]
    public void ComputeDiff_AddedLine_ShowsPlus()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/litellm-test.yaml");

        var diff = service.ComputeDiff("line1", "line1\nadded");

        Assert.Contains("+ added", diff);
    }

    [Fact]
    public void ParseYaml_ValidYaml_ParsesProviders()
    {
        const string yaml = """
            providers:
              groq:
                keys:
                  - source: env
                    name: GROQ_API_KEY
                models:
                  - id: scout
                    name: meta-llama/llama-4-scout
                    max_tokens: 8192
            routes:
              small:
                - provider: groq
                  model: scout
                  keys: [groq1]
                  order: 1
            aliases:
              gpt-4o-mini: small
            router:
              routing_strategy: least-busy
              num_retries: 7
            """;

        var config = LiteLlmRoutingConfigService.ParseYaml(yaml);

        Assert.Single(config.Providers);
        Assert.True(config.Providers.ContainsKey("groq"));
        Assert.Single(config.Providers["groq"].Models);
        Assert.Equal("scout", config.Providers["groq"].Models[0].Id);
        Assert.Single(config.Routes["small"]);
        Assert.Equal("groq", config.Routes["small"][0].Provider);
        Assert.Equal("gpt-4o-mini", config.Aliases.Keys.First());
        Assert.Equal(7, config.Router.NumRetries);
    }

    [Fact]
    public void ParseYaml_Empty_ReturnsDefaultConfig()
    {
        var config = LiteLlmRoutingConfigService.ParseYaml("");

        Assert.NotNull(config);
    }

    [Fact]
    public async Task SaveAsync_WritesYamlToDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"LeanKernel-test-{Guid.NewGuid()}.yaml");
        try
        {
            var service = new LiteLlmRoutingConfigService(path);
            var config = SimpleConfig();

            await service.SaveAsync(config, CancellationToken.None);

            Assert.True(File.Exists(path));
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("groq", content);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsEmptyConfig()
    {
        var service = new LiteLlmRoutingConfigService("/tmp/definitely-does-not-exist-LeanKernel.yaml");

        var config = service.Load();

        Assert.NotNull(config);
        Assert.Empty(config.Providers);
    }

    // ── RoutingConfigController tests ─────────────────────────────────────────

    [Fact]
    public void GetConfig_ReturnsOkWithConfig()
    {
        var service = new StubRoutingService(SimpleConfig());
        var controller = new RoutingConfigController(service);

        var result = controller.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<RoutingConfigResponse>(ok.Value);
        Assert.NotNull(response.Config);
    }

    [Fact]
    public void GetConfig_IncludesValidationErrors()
    {
        var config = SimpleConfig();
        config.Aliases["bad"] = "no-such-route";
        var service = new StubRoutingService(config);
        var controller = new RoutingConfigController(service);

        var result = controller.GetConfig();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<RoutingConfigResponse>(ok.Value);
        Assert.Contains(response.ValidationErrors, e => e.Code == "INVALID_ALIAS_TARGET");
    }

    [Fact]
    public async Task SaveConfig_DryRun_DoesNotCallSave()
    {
        var service = new StubRoutingService(SimpleConfig());
        var controller = new RoutingConfigController(service);

        var result = await controller.SaveConfig(
            new RoutingConfigSaveRequest { Config = SimpleConfig(), DryRun = true },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<RoutingConfigSaveResponse>(ok.Value);
        Assert.False(response.Saved);
        Assert.False(service.SaveCalled);
    }

    [Fact]
    public async Task SaveConfig_WithChanges_CallsSave()
    {
        var service = new StubRoutingService(SimpleConfig());
        var controller = new RoutingConfigController(service);

        var result = await controller.SaveConfig(
            new RoutingConfigSaveRequest { Config = SimpleConfig(), DryRun = false },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<RoutingConfigSaveResponse>(ok.Value);
        Assert.True(response.Saved);
        Assert.True(service.SaveCalled);
    }

    [Fact]
    public async Task SaveConfig_HardValidationErrors_Returns422()
    {
        var service = new StubRoutingService(SimpleConfig());
        var controller = new RoutingConfigController(service);

        // Reference a non-existent provider → validation error
        var invalidConfig = SimpleConfig();
        invalidConfig.Routes["small"].Add(new RouteEntry
        {
            Provider = "nonexistent-provider",
            Model = "x",
            Keys = [],
            Order = 99
        });

        var result = await controller.SaveConfig(
            new RoutingConfigSaveRequest { Config = invalidConfig, DryRun = false },
            CancellationToken.None);

        Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.False(service.SaveCalled);
    }

    private sealed class StubRoutingService : ILiteLlmRoutingConfigService
    {
        private readonly LiteLlmRoutingConfig _config;
        public bool SaveCalled { get; private set; }

        public StubRoutingService(LiteLlmRoutingConfig config) => _config = config;

        public LiteLlmRoutingConfig Load() => _config;

        public List<RoutingValidationError> Validate(LiteLlmRoutingConfig config)
        {
            var realService = new LiteLlmRoutingConfigService("/tmp/stub.yaml");
            return realService.Validate(config);
        }

        public List<ProviderKeyStatus> GetKeyStatuses(LiteLlmRoutingConfig config)
        {
            var realService = new LiteLlmRoutingConfigService("/tmp/stub.yaml");
            return realService.GetKeyStatuses(config);
        }

        public string GenerateYaml(LiteLlmRoutingConfig config)
        {
            var realService = new LiteLlmRoutingConfigService("/tmp/stub.yaml");
            return realService.GenerateYaml(config);
        }

        public string ComputeDiff(string oldYaml, string newYaml)
        {
            var realService = new LiteLlmRoutingConfigService("/tmp/stub.yaml");
            return realService.ComputeDiff(oldYaml, newYaml);
        }

        public Task SaveAsync(LiteLlmRoutingConfig config, CancellationToken ct)
        {
            SaveCalled = true;
            return Task.CompletedTask;
        }

        public string GetRawYaml() => string.Empty;

        public Task SaveRawYamlAsync(string yaml, CancellationToken ct)
        {
            SaveCalled = true;
            return Task.CompletedTask;
        }
    }
}
