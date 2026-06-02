using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Nameless Inversion (Lorwyn, {1}{B}, Kindred Instant —
/// Shapeshifter).
///
/// Oracle text:
///   "Changeling (This card is every creature type.)
///    Target creature gets +3/-3 and loses all creature types until end of
///    turn."
///
/// Covers:
///   - Card identity (Instant + Tribal, {1}{B}, Shapeshifter subtype,
///     owner/controller).
///   - Changeling keyword marker (CR 702.73 / 312).
///   - NamedCardFactory dispatch.
///   - BuildDefinition: single 1..1 target-creature request, Removal intent.
///   - Resolve registers a +3/-3 PumpUntilEndOfTurnEffect AND a
///     LoseAllCreatureTypesUntilEndOfTurnEffect (CR 514.2 / 613).
///   - Target not on battlefield at resolution → no-op (CR 608.2b).
///   - No ContinuousEffectsService wired → no-op (Disfigure-style guard).
/// </summary>
public class NamelessInversionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void NamelessInversion_Identity_KindredInstantAt1B()
    {
        var card = NamelessInversionFactory.Create(_alice);

        card.Name.Should().Be("Nameless Inversion");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Tribal).Should().BeTrue("Kindred is the Tribal card type, CR 312");
        card.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamelessInversion_CarriesChangelingKeywordMarker()
    {
        var card = NamelessInversionFactory.Create(_alice);

        card.Abilities
            .OfType<Majik.Core.Abilities.KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Changeling", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Nameless Inversion has Changeling (CR 702.73 / 312)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_NamelessInversion()
    {
        var card = NamedCardFactory.Create("Nameless Inversion", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Nameless Inversion");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_SingleTargetCreatureRequest()
    {
        var def = NamelessInversionFactory.BuildDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void NamelessInversion_AppliesPlus3Minus3_UntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        // A 4/4 Goblin so +3/-3 leaves a survivable 7/1 (tests P/T) and the
        // Goblin creature type is present to be stripped.
        var goblin = new Creature("Goblin Test", "{2}{R}", 4, 4,
            subtypes: new[] { CardSubtype.Goblin })
        {
            ActiveEffects = svc,
        };
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        goblin.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goblin);

        Resolve(goblin);

        // CR 613 Layer 7c — +3/-3 takes 4/4 → 7/1.
        goblin.Power.Should().Be(7, "4/4 with +3/-3 → 7/1");
        goblin.Toughness.Should().Be(1);
    }

    [Fact]
    public void NamelessInversion_LosesAllCreatureTypes_UntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var goblin = new Creature("Goblin Test", "{2}{R}", 4, 4,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior })
        {
            ActiveEffects = svc,
        };
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        goblin.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goblin);

        Resolve(goblin);

        // CR 613 Layer 4 — both creature subtypes stripped.
        var chars = svc.Compute(goblin);
        chars.Subtypes.Should().NotContain(CardSubtype.Goblin);
        chars.Subtypes.Should().NotContain(CardSubtype.Warrior);
        chars.Subtypes.Should().BeEmpty("a creature with only creature types loses them all");
    }

    [Fact]
    public void NamelessInversion_LoseTypes_PreservesNonCreatureSubtypes()
    {
        var svc = new ContinuousEffectsService();
        // A Vehicle creature (e.g. crewed): the Vehicle artifact subtype must
        // survive — "loses all CREATURE types" doesn't touch it (CR 205.3m).
        var vehicle = new Creature("Vehicle Test", "{3}", 5, 5,
            subtypes: new[] { CardSubtype.Vehicle, CardSubtype.Construct })
        {
            ActiveEffects = svc,
        };
        vehicle.SetOwner(_bob);
        vehicle.SetController(_bob);
        vehicle.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(vehicle);

        Resolve(vehicle);

        var chars = svc.Compute(vehicle);
        chars.Subtypes.Should().Contain(CardSubtype.Vehicle);
        chars.Subtypes.Should().Contain(CardSubtype.Construct);
    }

    [Fact]
    public void NamelessInversion_TargetNotOnBattlefield_NoOp()
    {
        var svc = new ContinuousEffectsService();
        var goblin = new Creature("Goblin Test", "{2}{R}", 4, 4,
            subtypes: new[] { CardSubtype.Goblin })
        {
            ActiveEffects = svc,
        };
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        goblin.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(goblin);

        Resolve(goblin);

        // Stats + subtypes unchanged — no effect applied (CR 608.2b).
        goblin.Power.Should().Be(4);
        goblin.Toughness.Should().Be(4);
        svc.Compute(goblin).Subtypes.Should().Contain(CardSubtype.Goblin);
    }

    [Fact]
    public void NamelessInversion_NoActiveEffectsService_DoesNotThrow()
    {
        var goblin = new Creature("Goblin Test", "{2}{R}", 4, 4,
            subtypes: new[] { CardSubtype.Goblin });
        goblin.SetOwner(_bob);
        goblin.SetController(_bob);
        goblin.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goblin);

        var act = () => Resolve(goblin);
        act.Should().NotThrow();

        goblin.Power.Should().Be(4);
        goblin.Toughness.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target)
    {
        var def = NamelessInversionFactory.BuildDefinition();
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
