using FluentAssertions;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Telemetry;

public sealed class TelemetrySettingsValidationTests
{
    [Fact]
    public void ValidCurrency_PassesValidation()
    {
        var settings = new TelemetrySettings { Currency = "USD" };
        var errors = Validate(settings);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void EmptyCurrency_FailsValidation()
    {
        var settings = new TelemetrySettings { Currency = string.Empty };
        var errors = Validate(settings);
        errors.Should().Contain(e => e.Contains("Currency"));
    }

    [Fact]
    public void NullCurrency_FailsValidation()
    {
        var settings = new TelemetrySettings { Currency = null! };
        var errors = Validate(settings);
        errors.Should().Contain(e => e.Contains("Currency"));
    }

    [Fact]
    public void LowercaseCurrency_FailsValidation()
    {
        var settings = new TelemetrySettings { Currency = "usd" };
        var errors = Validate(settings);
        errors.Should().Contain(e => e.Contains("Currency"));
    }

    [Fact]
    public void CurrencyWithWrongLength_FailsValidation()
    {
        var settings = new TelemetrySettings { Currency = "USDD" };
        var errors = Validate(settings);
        errors.Should().Contain(e => e.Contains("Currency"));
    }

    private static List<string> Validate(TelemetrySettings settings)
    {
        var services = new ServiceCollection();
        services.AddOptions<TelemetrySettings>()
            .Configure(s =>
            {
                s.Enabled = settings.Enabled;
                s.Currency = settings.Currency;
                s.RetainRawMetadata = settings.RetainRawMetadata;
                s.UseCostEstimate = settings.UseCostEstimate;
            })
            .Validate(
                static s => !string.IsNullOrWhiteSpace(s.Currency)
                    && s.Currency.Length == 3
                    && s.Currency.All(char.IsAsciiLetterUpper),
                "Agents:Telemetry Currency must be a 3-letter uppercase ISO currency code (e.g. USD).")
            .ValidateOnStart();

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<TelemetrySettings>>();

        try
        {
            _ = options.Value;
            return [];
        }
        catch (OptionsValidationException ex)
        {
            return ex.Failures.ToList();
        }
    }
}