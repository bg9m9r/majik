using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ShoreUpFactory"/> — the Dominaria instant Shore Up
/// ({U}).
///
/// Oracle: "Target creature you control gets +1/+1 and gains hexproof until end
/// of turn. Untap it."
///
/// Covers ONLY the unique spell body (identity + resolve behaviour):
/// - Identity ({U} blue instant, mana value 1).
/// - SpellDefinition shape — one 1..1 target-creature request.
/// - Resolve: target gets +1/+1, hexproof, and is untapped.
/// - Resolve: EOT cleanup removes pump AND hexproof (CR 514.2).
/// - Resolve: target off the battlefield is a no-op (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class ShoreUpFactoryTests : IDisposable
{
    public ShoreUpFactoryTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    // =========================================================================
    // Identity
    // =========================================================================

    [Fact]
    public void ShoreUp_Identity_BlueInstant_ManaValueOne()
    {
        var alice = new Player("Alice", 20);
        var card = ShoreUpFactory.Create(alice);

        card.Name.Should().Be("Shore Up");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(1,
            "Shore Up costs {U} — mana value 1 (CR 202.3)");

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
    }

    // =========================================================================
    // SpellDefinition shape
    // =========================================================================

    [Fact]
    public void ShoreUp_BuildDefinition_OneTargetCreature()
    {
        var def = ShoreUpFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Resolve
    // =========================================================================

    [Fact]
    public void ShoreUp_Resolve_TargetGetsPlusOnePlusOne_Hexproof_AndUntaps()
    {
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();
        var target = BuildCreature(continuous, alice, power: 2, toughness: 2);
        target.Tap();
        target.IsTapped.Should().BeTrue("preconditions: target starts tapped");

        ExecuteResolve(target);

        target.GetPower().Should().Be(3, "+1/+1 applied (CR 613.4d)");
        target.GetToughness().Should().Be(3);
        continuous.Compute(target).Keywords.Contains("Hexproof").Should().BeTrue(
            "CR 702.11b — gains hexproof until end of turn");
        target.IsTapped.Should().BeFalse("CR 701.21a — \"Untap it\"");
    }

    [Fact]
    public void ShoreUp_Resolve_EndOfTurnCleanup_RemovesPumpAndHexproof()
    {
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();
        var target = BuildCreature(continuous, alice, power: 2, toughness: 2);

        ExecuteResolve(target);
        target.GetPower().Should().Be(3);
        continuous.Compute(target).Keywords.Contains("Hexproof").Should().BeTrue();

        continuous.ExpireEndOfTurn();

        target.GetPower().Should().Be(2, "pump expires at cleanup (CR 514.2)");
        target.GetToughness().Should().Be(2);
        continuous.Compute(target).Keywords.Contains("Hexproof").Should().BeFalse(
            "hexproof grant expires at cleanup (CR 514.2)");
    }

    [Fact]
    public void ShoreUp_Resolve_TargetNotOnBattlefield_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();
        var target = new Creature("Bears", "{1}{G}", 2, 2)
        {
            Owner = alice,
            Controller = alice,
            Zone = ZoneType.Graveyard,
            ActiveEffects = continuous,
        };
        alice.Zones.Graveyard.AddCard(target);
        target.Tap();

        ExecuteResolve(target);

        target.GetPower().Should().Be(2, "CR 608.2b — off-battlefield target dropped");
        continuous.Compute(target).Keywords.Contains("Hexproof").Should().BeFalse();
        target.IsTapped.Should().BeTrue("no-op leaves the creature tapped");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static void ExecuteResolve(Creature target)
    {
        var def = ShoreUpFactory.BuildDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static Creature BuildCreature(
        ContinuousEffectsService continuous,
        Player controller,
        int power,
        int toughness)
    {
        var c = new Creature($"{power}/{toughness} Creature", "{U}", power, toughness)
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }
}
