using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Tragic Slip (Dark Ascension, {B}, Instant).
///
/// Oracle text:
///   "Target creature gets -1/-1 until end of turn.
///    Morbid — That creature gets -13/-13 until end of turn instead if a
///    creature died this turn."
///
/// Disfigure-shape -X/-X-until-EOT pump (PumpUntilEndOfTurnEffect, CR 514.2)
/// with a Fatal-Push-shape conditional upgrade keyed off
/// <see cref="TurnState.CreaturesDiedThisTurn"/> (Morbid).
///
/// Covers:
///   - Card identity (Instant, {B}, owner/controller) + dispatch.
///   - Base clause: no creature died → -1/-1.
///   - Morbid clause: a creature died this turn → -13/-13.
///   - No TurnState wired (shape / dispatcher tests) → base -1/-1.
///   - Off-battlefield target → no-op (CR 608.2b).
///   - No ContinuousEffectsService → silent no-op.
/// </summary>
public class TragicSlipFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly TurnState _turnState = new();

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TragicSlip_Identity_InstantAtB()
    {
        var card = TragicSlipFactory.Create(_alice);

        card.Name.Should().Be("Tragic Slip");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TragicSlip()
    {
        var card = NamedCardFactory.Create("Tragic Slip", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Tragic Slip");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreatureRequest()
    {
        var def = TragicSlipFactory.BuildSpellDefinition(() => null, t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Base clause — no creature died → -1/-1.
    // -----------------------------------------------------------------------

    [Fact]
    public void Base_AppliesMinus1Minus1_WhenNoCreatureDied()
    {
        var bear = NewCreatureWithEffects(_bob, "Grizzly Bears", 2, 2);

        Resolve(bear, morbidActive: false);

        // CR 613 Layer 7c — -1/-1 takes a 2/2 to 1/1.
        bear.Power.Should().Be(1, "no creature died → base -1/-1");
        bear.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Morbid clause — a creature died this turn → -13/-13 instead.
    // -----------------------------------------------------------------------

    [Fact]
    public void Morbid_AppliesMinus13Minus13_WhenCreatureDiedThisTurn()
    {
        var bear = NewCreatureWithEffects(_bob, "Grizzly Bears", 2, 2);

        Resolve(bear, morbidActive: true);

        // -13/-13 on a 2/2 → -11/-11 (clamped or not, both stats drop well
        // below zero; toughness ≤ 0 makes it lethal for the SBA pass).
        bear.Power.Should().Be(-11, "Morbid active → -13/-13 instead of -1/-1");
        bear.Toughness.Should().Be(-11);
    }

    [Fact]
    public void NoTurnStateWired_FallsBackToBaseMinus1Minus1()
    {
        var bear = NewCreatureWithEffects(_bob, "Grizzly Bears", 2, 2);

        // Null TurnState resolver (shape / dispatcher path) → Morbid inactive.
        var def = TragicSlipFactory.BuildSpellDefinition(() => null, t => t);
        ExecuteDefinition(def, bear);

        bear.Power.Should().Be(1, "no TurnState wired → base clause");
        bear.Toughness.Should().Be(1);
    }

    [Fact]
    public void IsMorbidActive_TracksCreatureDiedThisTurn()
    {
        TragicSlipFactory.IsMorbidActive(() => _turnState).Should().BeFalse();

        _turnState.RecordCreatureDied(_alice);

        TragicSlipFactory.IsMorbidActive(() => _turnState).Should().BeTrue(
            "any creature dying this turn enables Morbid");
    }

    [Fact]
    public void IsMorbidActive_NoTurnStateWired_ReturnsFalse()
    {
        TragicSlipFactory.IsMorbidActive(() => null).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Illegal target / no effects service — defensive no-ops.
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetNotOnBattlefield_NoOp()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        Resolve(bear, morbidActive: true);

        bear.Power.Should().Be(2, "CR 608.2b — illegal target → no-op");
        bear.Toughness.Should().Be(2);
    }

    [Fact]
    public void NoActiveEffectsService_DoesNotThrow()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var act = () => Resolve(bear, morbidActive: false);
        act.Should().NotThrow();

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature NewCreatureWithEffects(Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, "{1}{G}", power, toughness)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private void Resolve(Creature target, bool morbidActive)
    {
        if (morbidActive)
        {
            _turnState.RecordCreatureDied(_bob);
        }

        var def = TragicSlipFactory.BuildSpellDefinition(
            turnStateResolver: () => _turnState,
            targetResolver: t => t);

        ExecuteDefinition(def, target);
    }

    private static void ExecuteDefinition(SpellDefinition def, Creature target)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }
}
