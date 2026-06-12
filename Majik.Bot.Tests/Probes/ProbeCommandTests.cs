using FluentAssertions;
using Majik.Console.Commands;
using Xunit;

namespace Majik.Bot.Tests.Probes;

/// <summary>
/// Arg-parsing / head-resolution contract for the <c>Majik.Console probe</c>
/// subcommand (the testable static — the console Main is a thin shim).
/// </summary>
public class ProbeCommandTests
{
    [Fact]
    public void Parse_Panel_WithNOverride()
    {
        var (config, error) = ProbeCommand.Parse(new[] { "probe", "panel", "--n", "2" });

        error.Should().BeNull();
        config.Should().NotBeNull();
        config!.Target.Should().Be("panel");
        config.Heads.Should().HaveCount(13);
        config.Heads.Should().OnlyContain(h => h.Games == 2);
    }

    [Fact]
    public void Parse_Panel_Defaults()
    {
        var (config, error) = ProbeCommand.Parse(new[] { "probe", "panel" });

        error.Should().BeNull();
        config!.Heads.Should().OnlyContain(h => h.Games == 30);
        config.OutDir.Should().BeNull();
        config.Concurrency.Should().BeNull();
    }

    [Fact]
    public void Parse_SingleHead_ResolvesByName_CaseInsensitive()
    {
        var (config, error) = ProbeCommand.Parse(new[] { "probe", "MIRROR-BURN", "--n", "4" });

        error.Should().BeNull();
        config!.Target.Should().Be("mirror-burn");
        config.Heads.Should().ContainSingle().Which.Name.Should().Be("mirror-burn");
        config.Heads[0].Games.Should().Be(4);
    }

    [Fact]
    public void Parse_UnknownHead_ErrorListsAvailableHeads()
    {
        var (config, error) = ProbeCommand.Parse(new[] { "probe", "nosuchhead" });

        config.Should().BeNull();
        error.Should().Contain("nosuchhead");
        error.Should().Contain("panel");
        error.Should().Contain("mirror-prowess");
        error.Should().Contain("asym-burn-vs-prowess");
    }

    [Fact]
    public void Parse_OutAndConcurrency()
    {
        var (config, error) = ProbeCommand.Parse(
            new[] { "probe", "panel", "--out", "/tmp/x", "--concurrency", "3" });

        error.Should().BeNull();
        config!.OutDir.Should().Be("/tmp/x");
        config.Concurrency.Should().Be(3);
    }

    [Fact]
    public void Parse_MissingTarget_Errors()
    {
        var (config, error) = ProbeCommand.Parse(new[] { "probe" });

        config.Should().BeNull();
        error.Should().Contain("panel");
    }

    [Theory]
    [InlineData("--n", "zero")]
    [InlineData("--n", "0")]
    [InlineData("--concurrency", "nope")]
    public void Parse_BadNumericFlag_Errors(string flag, string value)
    {
        var (config, error) = ProbeCommand.Parse(new[] { "probe", "panel", flag, value });

        config.Should().BeNull();
        error.Should().Contain(flag);
    }

    [Fact]
    public void Parse_UnknownFlag_Errors()
    {
        var (config, error) = ProbeCommand.Parse(new[] { "probe", "panel", "--frobnicate" });

        config.Should().BeNull();
        error.Should().Contain("--frobnicate");
    }
}
