using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Behaviour tests for the two embedded JSON cards whose only activated ability
/// is the controller-scoped each-opponent drain shape
/// ("{cost}: Each opponent loses N life.", CR 119.3 / 109.5):
/// <list type="bullet">
///   <item>Archers' Parapet — a 0/5 Defender Wall whose
///     "{1}{B}, {T}: Each opponent loses 1 life." carries a {T} cost.</item>
///   <item>Engine Rat — a 1/1 Deathtouch creature whose pure-mana
///     "{5}{B}: Each opponent loses 2 life." has no {T} cost.</item>
/// </list>
/// The drain routes through the <c>lose_life_each_opponent</c> verb (the same
/// primitive the JSON card-def materializer uses), enumerating the controller's
/// opponents live off <c>ctx.Game</c>.
/// </summary>
public class EachOpponentDrainCardsTests
{
    private static Majik.Core.Game.GameContext Context(Player self, params Player[] all) =>
        new(self, all, self, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

    private static void ResolveDrain(ActivatedAbility ability, Player controller, params Player[] all)
    {
        var ctx = new ResolutionContext(
            Controller: controller,
            Agent: null,
            Game: Context(controller, all),
            ChosenTargets: System.Array.Empty<IReadOnlyList<object>>());
        foreach (var effect in ability.Effects)
            effect.ExecuteAsync(ctx).AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void ArchersParapet_IsDefenderWall_WithTappedEachOpponentDrain()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Archers' Parapet", alice);

        var creature = card.Should().BeAssignableTo<Creature>().Subject;
        creature.Subtypes.Should().Contain(CardSubtype.Wall);
        creature.HasEffectiveKeyword("Defender").Should().BeTrue();
        creature.BasePower.Should().Be(0);
        creature.BaseToughness.Should().Be(5);

        var drain = creature.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility).ToList();
        drain.Should().ContainSingle("Archers' Parapet has exactly one each-opponent drain");
        drain[0].Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the {1}{B}, {T} cost taps the Wall");
        drain[0].TargetRequests.Should().BeEmpty(
            "an each-opponent group effect (CR 608.2) announces no target");
    }

    [Fact]
    public void ArchersParapet_Drain_DrainsEachOpponentOneLife()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var card = NamedCardFactory.Create("Archers' Parapet", alice);
        var creature = (Creature)card;

        var drain = creature.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        ResolveDrain(drain, alice, alice, bob);

        bob.LifeTotal.Should().Be(19, "the opponent loses 1 life (CR 109.5)");
        alice.LifeTotal.Should().Be(20, "the controller is not an opponent");
    }

    [Fact]
    public void EngineRat_IsDeathtouch_WithPureManaEachOpponentDrain()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Engine Rat", alice);

        var creature = card.Should().BeAssignableTo<Creature>().Subject;
        creature.Subtypes.Should().Contain(CardSubtype.Rat);
        creature.HasEffectiveKeyword("Deathtouch").Should().BeTrue();
        creature.BasePower.Should().Be(1);
        creature.BaseToughness.Should().Be(1);

        var drain = creature.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);
        drain.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Tap,
                "Engine Rat's {5}{B} drain has no {T} cost");
    }

    [Fact]
    public void EngineRat_Drain_DrainsEveryOpponentTwoLife()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);
        var card = NamedCardFactory.Create("Engine Rat", alice);
        var creature = (Creature)card;

        var drain = creature.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        ResolveDrain(drain, alice, alice, bob, carol);

        bob.LifeTotal.Should().Be(18, "each opponent loses 2 life");
        carol.LifeTotal.Should().Be(18, "EVERY opponent is drained");
        alice.LifeTotal.Should().Be(20, "the controller is not an opponent");
    }
}
