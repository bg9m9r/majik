using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// CR 702.35c — the madness-paid resolution-flag seam. A card cast for its
/// madness cost stamps <see cref="Card.WasCastForMadnessCost"/> (and any chosen
/// madness {X} into <see cref="Card.MadnessCastX"/>) at pay time, surviving onto
/// the resolving permanent / spell so a "if its madness cost was paid" rider
/// (Grave Scrabbler's ETB, Welcome to the Fold's control gate) reads it at
/// resolution.
/// </summary>
public class MadnessPaidResolutionFlagTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Card-level stamp seam ───────────────────────────────────────────────

    [Fact]
    public void MadnessGrant_RecordsMadnessOnTheRuntimeExileCastGrant()
    {
        var card = new Creature("Grave Scrabbler", "{3}{B}", 2, 2) { Owner = _alice, Controller = _alice };
        card.GrantRuntimeExileCast(_alice, ManaCost.Parse("{1}{B}"), spendAsAnyColor: false, isMadness: true);

        card.RuntimeExileCastIsMadness.Should().BeTrue();

        // An impulse-style (non-madness) exile-cast grant must NOT flag madness.
        var impulse = new Creature("Ragavan Prey", "{2}{R}", 2, 2) { Owner = _bob, Controller = _bob };
        impulse.GrantRuntimeExileCast(_bob, ManaCost.Parse("{2}{R}"));
        impulse.RuntimeExileCastIsMadness.Should().BeFalse();
    }

    [Fact]
    public void MarkCastForMadness_StampsFlagAndX_ClearedByConsumer()
    {
        var card = new Creature("Grave Scrabbler", "{3}{B}", 2, 2) { Owner = _alice, Controller = _alice };
        card.WasCastForMadnessCost.Should().BeFalse("no cast has happened yet");
        card.MadnessCastX.Should().BeNull();

        card.MarkCastForMadness(madnessX: 4);
        card.WasCastForMadnessCost.Should().BeTrue();
        card.MadnessCastX.Should().Be(4);

        card.ClearCastForMadness();
        card.WasCastForMadnessCost.Should().BeFalse();
        card.MadnessCastX.Should().BeNull();
    }

    [Fact]
    public void ClearRuntimeExileCast_ClearsMadnessGrantFlag()
    {
        var card = new Creature("Grave Scrabbler", "{3}{B}", 2, 2) { Owner = _alice, Controller = _alice };
        card.GrantRuntimeExileCast(_alice, ManaCost.Parse("{1}{B}"), isMadness: true);
        card.RuntimeExileCastIsMadness.Should().BeTrue();

        card.ClearRuntimeExileCast();
        card.RuntimeExileCastIsMadness.Should().BeFalse();
    }

    // ── Grave Scrabbler — ETB gated on madness-paid ─────────────────────────

    [Fact]
    public void GraveScrabbler_Identity_AndMadness()
    {
        var card = (Creature)NamedCardFactory.Create("Grave Scrabbler", _alice);

        card.Name.Should().Be("Grave Scrabbler");
        card.ManaCost.Should().Be("{3}{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);

        ImplementedCardNames.Contains("Grave Scrabbler").Should().BeTrue();
        MadnessCatalog.HasMadness(card).Should().BeTrue();
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{1}{B}"));
    }

    [Fact]
    public void GraveScrabbler_Etb_WhenMadnessPaid_ReturnsCreatureCardFromGraveyard()
    {
        var scrabbler = GraveScrabblerFactory.Create(_alice, eventBus: null, triggers: null);
        scrabbler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(scrabbler);
        scrabbler.MarkCastForMadness(); // cast for madness

        var deadBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        deadBear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(deadBear);

        var etb = scrabbler.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { deadBear } });

        GraveScrabblerFactory.ResolveEtb(scrabbler, _alice, etb, zones: null);

        deadBear.Zone.Should().Be(ZoneType.Hand, "the madness-cost was paid, so the creature card returns");
        scrabbler.WasCastForMadnessCost.Should().BeFalse("the ETB consumes (clears) the flag");
    }

    [Fact]
    public void GraveScrabbler_Etb_WhenNotMadnessPaid_DoesNothing()
    {
        var scrabbler = GraveScrabblerFactory.Create(_alice, eventBus: null, triggers: null);
        scrabbler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(scrabbler);
        // No MarkCastForMadness — cast for its normal cost.

        var deadBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        deadBear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(deadBear);

        var etb = scrabbler.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { deadBear } });

        GraveScrabblerFactory.ResolveEtb(scrabbler, _alice, etb, zones: null);

        deadBear.Zone.Should().Be(ZoneType.Graveyard, "the madness cost was NOT paid, the ETB ability does nothing");
    }

    // ── Welcome to the Fold — control gate widens to X when madness paid ────

    [Fact]
    public void WelcomeToTheFold_Identity_AndMadness()
    {
        var card = (Sorcery)NamedCardFactory.Create("Welcome to the Fold", _alice);

        card.Name.Should().Be("Welcome to the Fold");
        card.ManaCost.Should().Be("{2}{U}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();

        ImplementedCardNames.Contains("Welcome to the Fold").Should().BeTrue();
        MadnessCatalog.HasMadness(card).Should().BeTrue();
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{X}{U}{U}"));
    }

    [Fact]
    public void WelcomeToTheFold_NormalCast_GainsControlOnlyIfToughness2OrLess()
    {
        var effects = new ContinuousEffectsService();

        // Toughness 3 — NOT gained on the normal-cast (≤ 2) gate.
        var bigBear = new Creature("Hill Giant", "{3}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        bigBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bigBear);

        var spell = (Sorcery)NamedCardFactory.Create("Welcome to the Fold", _alice);
        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bigBear } },
            ManaPayment.Empty);

        WelcomeToTheFoldFactory.Resolve(_alice, chosen, o => o, effects, spell);

        effects.EffectiveController(bigBear).Should().Be(_bob, "toughness 3 > 2 and madness was not paid");
    }

    [Fact]
    public void WelcomeToTheFold_MadnessCast_GainsControlIfToughnessXOrLess()
    {
        var effects = new ContinuousEffectsService();

        // Toughness 3 — gained when madness {X}{U}{U} was paid with X = 3.
        var bigBear = new Creature("Hill Giant", "{3}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        bigBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bigBear);

        var spell = (Sorcery)NamedCardFactory.Create("Welcome to the Fold", _alice);
        spell.MarkCastForMadness(madnessX: 3); // madness paid, X = 3

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bigBear } },
            ManaPayment.Empty);

        WelcomeToTheFoldFactory.Resolve(_alice, chosen, o => o, effects, spell);

        effects.EffectiveController(bigBear).Should().Be(_alice, "madness X = 3 ≥ toughness 3 → control gained");
        spell.WasCastForMadnessCost.Should().BeFalse("resolution consumes (clears) the madness flag");
    }
}
