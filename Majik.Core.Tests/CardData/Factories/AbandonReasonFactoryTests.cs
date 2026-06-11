using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
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
/// Tests for <see cref="AbandonReasonFactory"/> — the Eventide instant
/// Abandon Reason ({1}{R}).
///
/// Oracle: "Up to two target creatures each get +1/+0 and gain first strike
/// until end of turn. Madness {1}{R}."
///
/// Madness is intrinsic (CR 702.35 — MadnessCatalog + Fx.DiscardCard) and is
/// covered by MadnessDiscardFunnelTests, so it is NOT tested here. These
/// tests cover only the unique spell body.
///
/// Covers:
/// - Identity ({1}{R} red instant, mana value 2).
/// - SpellDefinition shape — one 0..2 target-creature request.
/// - Resolve: two targets each get +1/+0 and first strike.
/// - Resolve: EOT cleanup removes pump AND first strike (CR 514.2).
/// - Resolve: zero targets is a legal no-op ("up to two").
/// - Resolve: non-Creature target dropped; the legal target still resolves.
/// - Resolve: target off the battlefield dropped (CR 608.2b).
/// </summary>
[Trait("Color", "R")]
public class AbandonReasonFactoryTests : IDisposable
{
    public AbandonReasonFactoryTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    // =========================================================================
    // Identity
    // =========================================================================

    [Fact]
    public void AbandonReason_Identity_RedInstant_ManaValueTwo()
    {
        var alice = new Player("Alice", 20);
        var card = AbandonReasonFactory.Create(alice);

        card.Name.Should().Be("Abandon Reason");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(2,
            "Abandon Reason costs {1}{R} — generic 1 + {R} = mana value 2 (CR 202.3)");

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Green);
    }

    // =========================================================================
    // SpellDefinition shape
    // =========================================================================

    [Fact]
    public void AbandonReason_BuildDefinition_UpToTwoTargetCreatures()
    {
        var def = AbandonReasonFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(0,
            "\"up to two\" allows zero targets (CR 601.2c)");
        def.TargetRequests[0].MaxTargets.Should().Be(2);
    }

    // =========================================================================
    // Resolve
    // =========================================================================

    [Fact]
    public void AbandonReason_Resolve_TwoTargets_EachGetPlusOneZero_AndFirstStrike()
    {
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();
        var a = BuildCreature(continuous, alice, power: 2, toughness: 2);
        var b = BuildCreature(continuous, alice, power: 3, toughness: 1);

        ExecuteResolve(a, b);

        a.GetPower().Should().Be(3, "+1/+0 applied");
        a.GetToughness().Should().Be(2, "pump is power-only");
        b.GetPower().Should().Be(4);
        b.GetToughness().Should().Be(1);
        CombatAbilities.HasFirstStrike(a).Should().BeTrue("CR 702.7 first strike granted");
        CombatAbilities.HasFirstStrike(b).Should().BeTrue();
    }

    [Fact]
    public void AbandonReason_Resolve_EndOfTurnCleanup_RemovesPumpAndFirstStrike()
    {
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();
        var a = BuildCreature(continuous, alice, power: 2, toughness: 2);

        ExecuteResolve(a);
        a.GetPower().Should().Be(3);
        CombatAbilities.HasFirstStrike(a).Should().BeTrue();

        continuous.ExpireEndOfTurn();

        a.GetPower().Should().Be(2, "pump expires at cleanup (CR 514.2)");
        CombatAbilities.HasFirstStrike(a).Should().BeFalse(
            "first strike grant expires at cleanup (CR 514.2)");
    }

    [Fact]
    public void AbandonReason_Resolve_ZeroTargets_IsLegalNoOp()
    {
        var def = AbandonReasonFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { Array.Empty<object>() },
            Mana: ManaPayment.Empty);

        var act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };
        act.Should().NotThrow("\"up to two\" permits choosing zero targets");
    }

    [Fact]
    public void AbandonReason_Resolve_NonCreatureTargetDropped_LegalTargetStillResolves()
    {
        var alice = new Player("Alice", 20);
        var continuous = new ContinuousEffectsService();
        var creature = BuildCreature(continuous, alice, power: 2, toughness: 2);
        var nonCreature = new Card("Some Land", "");

        var def = AbandonReasonFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { nonCreature, creature } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // CR 608.2b — non-Creature dropped, the creature still resolves.
        creature.GetPower().Should().Be(3);
        CombatAbilities.HasFirstStrike(creature).Should().BeTrue();
    }

    [Fact]
    public void AbandonReason_Resolve_TargetNotOnBattlefield_IsNoOp()
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

        ExecuteResolve(target);

        target.GetPower().Should().Be(2, "CR 608.2b — off-battlefield target dropped");
        CombatAbilities.HasFirstStrike(target).Should().BeFalse();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static void ExecuteResolve(params Creature[] targets)
    {
        var def = AbandonReasonFactory.BuildDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets.Cast<object>().ToArray() },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private static Creature BuildCreature(
        ContinuousEffectsService continuous,
        Player controller,
        int power,
        int toughness)
    {
        var c = new Creature($"{power}/{toughness} Creature", "{R}", power, toughness)
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
