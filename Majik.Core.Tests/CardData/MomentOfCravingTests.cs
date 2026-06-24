using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Moment of Craving (Dominaria / various reprints, {1}{B}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Target creature gets -2/-2 until end of turn. You gain 2 life."
///
/// Covers the card's UNIQUE behaviour:
///   - Card identity ({1}{B} Instant — exercises the non-vanilla mana cost).
///   - BuildSpellDefinition declares a single 1..1 "target creature" request.
///   - Resolve registers a -2/-2 PumpUntilEndOfTurnEffect on the target
///     (CR 613 Layer 7c / CR 514.2 — expires at EOT) AND the controller gains
///     2 life (CR 119.3), both as part of the same resolution.
///   - Target not on battlefield at resolution → no pump, but the lifegain
///     clause is part of the spell's single resolution; per the LightningHelix
///     analogue the lifegain is unconditional once the spell resolves.
///   - No ContinuousEffectsService wired → pump no-ops, lifegain still applies,
///     no throw (Disfigure-style guard).
///
/// CardFactoryContractTests already asserts NamedCardFactory dispatch +
/// well-formedness for every implemented card, so no dispatch test here.
/// </summary>
[Trait("Color", "B")]
public class MomentOfCravingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MomentOfCraving_Identity_InstantAt1B()
    {
        var card = MomentOfCravingFactory.Create(_alice);

        card.Name.Should().Be("Moment of Craving");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreatureRequest()
    {
        var def = MomentOfCravingFactory.BuildSpellDefinition(_alice, t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void MomentOfCraving_AppliesMinus2Minus2_AndGains2Life()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var aliceStarting = _alice.LifeTotal;

        Resolve(_alice, bear);

        // CR 613 Layer 7c — -2/-2 takes Grizzly Bears 2/2 → 0/0.
        bear.Power.Should().Be(0, "Grizzly Bears 2/2 with -2/-2 → 0/0");
        bear.Toughness.Should().Be(0);

        // CR 119.3 — controller gains 2 life as part of the same resolution.
        _alice.LifeTotal.Should().Be(aliceStarting + 2, "controller gains 2 life");
    }

    [Fact]
    public void MomentOfCraving_TargetNotOnBattlefield_NoPumpButStillGainsLife()
    {
        // Creature already left the battlefield before the spell resolves
        // (CR 608.2b — the pump can't apply to a non-battlefield permanent).
        // The "You gain 2 life" clause is part of the spell's single
        // resolution and still fires (mirrors LightningHelix's unconditional
        // lifegain clause once the spell resolves).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var aliceStarting = _alice.LifeTotal;

        Resolve(_alice, bear);

        bear.Power.Should().Be(2, "no pump applied off the battlefield");
        bear.Toughness.Should().Be(2);
        _alice.LifeTotal.Should().Be(aliceStarting + 2, "controller still gains 2 life");
    }

    [Fact]
    public void MomentOfCraving_NoActiveEffectsService_DoesNotThrow_StillGainsLife()
    {
        // Shape-only path: target on battlefield but no
        // ContinuousEffectsService wired. The pump registration must silently
        // no-op (Disfigure-style guard); the lifegain still applies.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var aliceStarting = _alice.LifeTotal;

        var act = () => Resolve(_alice, bear);
        act.Should().NotThrow();

        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        _alice.LifeTotal.Should().Be(aliceStarting + 2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Player controller, Creature target)
    {
        var def = MomentOfCravingFactory.BuildSpellDefinition(controller, t => t);
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
