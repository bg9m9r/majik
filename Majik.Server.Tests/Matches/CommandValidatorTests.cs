using Majik.Core.Api.Commands;
using Majik.Server.Matches;
using FluentAssertions;
using Xunit;

namespace Majik.Server.Tests.Matches;

/// <summary>
/// Unit tests for <see cref="CommandValidator"/> — the input-bounds DoS guard
/// applied to player commands before they reach the engine.
/// </summary>
public class CommandValidatorTests
{
    private static Guid[] Ids(int n) => Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToArray();

    // ---- well-behaved input passes ----

    [Fact]
    public void Pass_IsWithinBounds() =>
        CommandValidator.Validate(new PassPriorityCommand()).Should().BeNull();

    [Fact]
    public void ChooseX_AtUpperBound_Passes() =>
        CommandValidator.Validate(new ChooseXCommand(CommandValidator.MaxX)).Should().BeNull();

    [Fact]
    public void ChooseX_Zero_Passes() =>
        CommandValidator.Validate(new ChooseXCommand(0)).Should().BeNull();

    [Fact]
    public void TargetList_AtMax_Passes() =>
        CommandValidator.Validate(
            new ChooseTargetsCommand(Ids(CommandValidator.MaxListLength)))
            .Should().BeNull();

    // ---- over-bounds input rejected ----

    [Fact]
    public void ChooseX_HugeValue_RejectedAsInvalidCommand()
    {
        var err = CommandValidator.Validate(new ChooseXCommand(1_000_000));
        err.Should().NotBeNull();
        err!.Error.Should().Be("invalid-command");
    }

    [Fact]
    public void ChooseX_Negative_Rejected() =>
        CommandValidator.Validate(new ChooseXCommand(-1))!.Error.Should().Be("invalid-command");

    [Fact]
    public void CastSpell_HugeXValue_Rejected()
    {
        var err = CommandValidator.Validate(
            new CastSpellCommand(Guid.NewGuid(), Array.Empty<Guid>(), 1_000_000, null));
        err!.Error.Should().Be("invalid-command");
    }

    [Fact]
    public void CastSpell_HugeTargetList_Rejected()
    {
        var err = CommandValidator.Validate(
            new CastSpellCommand(Guid.NewGuid(), Ids(CommandValidator.MaxListLength + 1), null, null));
        err!.Error.Should().Be("invalid-command");
    }

    [Fact]
    public void ChooseTargets_OverMax_Rejected() =>
        CommandValidator.Validate(
            new ChooseTargetsCommand(Ids(CommandValidator.MaxListLength + 1)))!
            .Error.Should().Be("invalid-command");

    [Fact]
    public void ChooseMana_OverMax_Rejected() =>
        CommandValidator.Validate(
            new ChooseManaCommand(Ids(CommandValidator.MaxListLength + 1)))!
            .Error.Should().Be("invalid-command");

    [Fact]
    public void OrderTriggers_OverMax_Rejected() =>
        CommandValidator.Validate(
            new OrderTriggersCommand(Ids(CommandValidator.MaxListLength + 1)))!
            .Error.Should().Be("invalid-command");

    [Fact]
    public void DeclareAttackers_OverMax_Rejected()
    {
        var attackers = Enumerable.Range(0, CommandValidator.MaxListLength + 1)
            .Select(_ => new AttackerDeclarationDto(Guid.NewGuid(), Guid.NewGuid()))
            .ToArray();
        CommandValidator.Validate(new DeclareAttackersCommand(attackers))!
            .Error.Should().Be("invalid-command");
    }

    [Fact]
    public void DeclareBlockers_OverMax_Rejected()
    {
        var blockers = Enumerable.Range(0, CommandValidator.MaxListLength + 1)
            .Select(_ => new BlockerDeclarationDto(Guid.NewGuid(), Guid.NewGuid()))
            .ToArray();
        CommandValidator.Validate(new DeclareBlockersCommand(blockers))!
            .Error.Should().Be("invalid-command");
    }

    [Fact]
    public void ChooseCardsToBottom_OverMax_Rejected() =>
        CommandValidator.Validate(
            new ChooseCardsToBottomCommand(Ids(CommandValidator.MaxListLength + 1)))!
            .Error.Should().Be("invalid-command");
}
