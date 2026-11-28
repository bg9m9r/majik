using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 508.1a / 509.1a / 613.1c — the permanents the engine offers as eligible
/// attackers / blockers are exactly what the answer validator accepts. A
/// permanent that is a creature by type or by a layer effect but is not of the
/// <see cref="Creature"/> class (an animated land — Forest under Badgermole
/// Cub, Verdant Catacombs, Kaito) must be declarable; a permanent that wasn't
/// offered must be rejected with the prompt left pending.
/// </summary>
public class CombatEligibilityAgreementTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Land AnimatedLand(Player owner)
    {
        var land = new Land("Forest") { Owner = owner };
        land.AddCardType(CardType.Creature); // effectively a creature, not a Creature instance
        land.IsEffectivelyCreature().Should().BeTrue();
        return land;
    }

    private RemoteAgent Agent(params ICard[] cards) => new(
        _alice,
        cardLookup: id => cards.FirstOrDefault(c => c.InstanceId == id),
        playerLookup: id => id == _bob.Id ? _bob : id == _alice.Id ? _alice : null);

    private GameContext Ctx() => new(_alice, new[] { _alice, _bob }, _alice, 1, Majik.Core.StateMachine.StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

    [Fact]
    public async Task AnOfferedAttackerThatIsEffectivelyACreature_IsAccepted()
    {
        var land = AnimatedLand(_alice);
        var agent = Agent(land);
        var task = agent.DeclareAttackersAsync(Ctx(), new Permanent[] { land });

        agent.Submit(new DeclareAttackersCommand(new[] { new AttackerDeclarationDto(land.InstanceId, _bob.Id) }) { PlayerId = _alice.Id });

        var plan = await task;
        plan.Attackers.Should().ContainSingle().Which.Attacker.Should().BeSameAs(land);
    }

    [Fact]
    public void AnAttackerThatWasNotOffered_IsRejected_PromptStaysPending()
    {
        var offered = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        var notOffered = new Creature("Runeclaw Bear", "1G", 2, 2) { Owner = _alice };
        var agent = Agent(offered, notOffered);
        _ = agent.DeclareAttackersAsync(Ctx(), new Permanent[] { offered });

        FluentActions.Invoking(() => agent.Submit(new DeclareAttackersCommand(
                new[] { new AttackerDeclarationDto(notOffered.InstanceId, _bob.Id) }) { PlayerId = _alice.Id }))
            .Should().Throw<InvalidOperationException>().WithMessage("*not an eligible attacker*");
        agent.HasPending.Should().BeTrue();
    }

    [Fact]
    public async Task AnOfferedBlockerAndAttacker_ThatAreEffectivelyCreatures_AreAccepted()
    {
        var attacker = AnimatedLand(_bob);
        var blocker = AnimatedLand(_alice);
        var agent = Agent(attacker, blocker);
        var task = agent.DeclareBlockersAsync(Ctx(), new Permanent[] { attacker }, new Permanent[] { blocker });

        agent.Submit(new DeclareBlockersCommand(new[] { new BlockerDeclarationDto(blocker.InstanceId, attacker.InstanceId) }) { PlayerId = _alice.Id });

        var plan = await task;
        plan.Blockers.Should().ContainSingle();
        plan.Blockers[0].Blocker.Should().BeSameAs(blocker);
        plan.Blockers[0].Attacker.Should().BeSameAs(attacker);
    }

    [Fact]
    public void ABlockerOrAttackedCreatureThatWasNotOffered_IsRejected_PromptStaysPending()
    {
        var attacker = new Creature("Goblin Guide", "R", 2, 2) { Owner = _bob };
        var blocker = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        var stranger = new Creature("Runeclaw Bear", "1G", 2, 2) { Owner = _alice };
        var agent = Agent(attacker, blocker, stranger);
        _ = agent.DeclareBlockersAsync(Ctx(), new Permanent[] { attacker }, new Permanent[] { blocker });

        FluentActions.Invoking(() => agent.Submit(new DeclareBlockersCommand(
                new[] { new BlockerDeclarationDto(stranger.InstanceId, attacker.InstanceId) }) { PlayerId = _alice.Id }))
            .Should().Throw<InvalidOperationException>().WithMessage("*not an eligible blocker*");
        FluentActions.Invoking(() => agent.Submit(new DeclareBlockersCommand(
                new[] { new BlockerDeclarationDto(blocker.InstanceId, stranger.InstanceId) }) { PlayerId = _alice.Id }))
            .Should().Throw<InvalidOperationException>().WithMessage("*not an attacking creature*");
        agent.HasPending.Should().BeTrue();
    }
}
