using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Sanctum Prelate — Creature — Human Cleric {1}{W}{W} 2/2
/// (Conspiracy: Take the Crown). Oracle text (verified against Scryfall):
///   "As this creature enters, choose a number.
///    Noncreature spells with mana value equal to the chosen number can't
///    be cast."
///
/// Covers:
/// - Card identity (P/T, subtypes, mana cost) + dispatcher routing.
/// - Printed static (CR 601.3): while on the battlefield with a chosen
///   number, the validator rejects noncreature casts whose mana value
///   matches; creature casts and non-matching MVs pass.
/// - Symmetric: blocks both players' noncreature spells.
/// - Lifecycle: the block lifts when the Prelate leaves the battlefield
///   (CR 603.6 / zone change), and the empty-choice (shape) path imposes no
///   restriction.
/// </summary>
[Trait("Color", "W")]
public class SanctumPrelateFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SanctumPrelateFactoryTests()
    {
        CastingRestrictions.Clear();
    }

    public void Dispose()
    {
        CastingRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SanctumPrelate_HasCorrectIdentity_AndPT_AndSubtypes()
    {
        var prelate = SanctumPrelateFactory.Create(_alice);

        prelate.Name.Should().Be("Sanctum Prelate");
        prelate.ManaCost.Should().Be("{1}{W}{W}");
        prelate.Power.Should().Be(2);
        prelate.Toughness.Should().Be(2);
        prelate.HasType(CardType.Creature).Should().BeTrue();
        prelate.HasSubtype(CardSubtype.Human).Should().BeTrue();
        prelate.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        prelate.Owner.Should().BeSameAs(_alice);
        prelate.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesSanctumPrelate_ToFactory()
    {
        var card = NamedCardFactory.Create("Sanctum Prelate", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sanctum Prelate");
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        ((Creature)card).Power.Should().Be(2);
        ((Creature)card).Toughness.Should().Be(2);
    }

    [Fact]
    public void NoChosenNumber_ImposesNoRestriction()
    {
        // Shape / dispatcher path: no number chosen → no block registered,
        // even once on the battlefield.
        var prelate = SanctumPrelateFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(prelate);
        prelate.SetZone(ZoneType.Battlefield);

        CastingRestrictions.IsNoncreatureManaValueBlocked(0).Should().BeFalse();
        CastingRestrictions.IsNoncreatureManaValueBlocked(3).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Printed static — CR 601.3
    // -----------------------------------------------------------------------

    [Fact]
    public void OnBattlefield_WithChosenNumber_RegistersBlock_ForThatManaValue()
    {
        var bus = new EventBus();
        // Choose 3.
        var prelate = SanctumPrelateFactory.Create(_alice, chosenNumber: 3, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(prelate);
        prelate.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(prelate, ZoneType.Stack, ZoneType.Battlefield));

        CastingRestrictions.IsNoncreatureManaValueBlocked(3).Should().BeTrue();
        CastingRestrictions.IsNoncreatureManaValueBlocked(2).Should().BeFalse();
    }

    [Fact]
    public void Validator_BlocksNoncreatureCast_WhenManaValueMatches()
    {
        var bus = new EventBus();
        var prelate = SanctumPrelateFactory.Create(_alice, chosenNumber: 3, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(prelate);
        prelate.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(prelate, ZoneType.Stack, ZoneType.Battlefield));

        // Bob casts a mv-3 noncreature spell ({2}{R}) — blocked.
        var skred = new Instant("Skred", "{2}{R}");
        skred.SetOwner(_bob);
        var action = new CastSpellAction(skred, _bob, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("601.3");
    }

    [Fact]
    public void Validator_AllowsNoncreatureCast_WhenManaValueDoesNotMatch()
    {
        var bus = new EventBus();
        var prelate = SanctumPrelateFactory.Create(_alice, chosenNumber: 3, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(prelate);
        prelate.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(prelate, ZoneType.Stack, ZoneType.Battlefield));

        // Lightning Bolt is mv-1 ({R}) — does not match the chosen 3.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_AllowsCreatureCast_EvenWhenManaValueMatches()
    {
        // CR 601.3 — restriction is strictly noncreature; a mv-3 creature
        // spell is unaffected even though its MV matches the chosen number.
        var bus = new EventBus();
        var prelate = SanctumPrelateFactory.Create(_alice, chosenNumber: 3, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(prelate);
        prelate.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(prelate, ZoneType.Stack, ZoneType.Battlefield));

        var beast = new Creature("Hill Giant", "{2}{R}", 3, 3); // mv 3
        beast.SetOwner(_bob);
        var action = new CastSpellAction(beast, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Block_IsSymmetric_AlsoBlocksControllersOwnNoncreatureSpell()
    {
        // The printed text isn't player-scoped — it blocks every player's
        // noncreature spells, including the Prelate's controller (Alice).
        var bus = new EventBus();
        var prelate = SanctumPrelateFactory.Create(_alice, chosenNumber: 2, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(prelate);
        prelate.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(prelate, ZoneType.Stack, ZoneType.Battlefield));

        var counterspell = new Instant("Counterspell", "{U}{U}"); // mv 2
        counterspell.SetOwner(_alice);
        var action = new CastSpellAction(counterspell, _alice, sorcerySpeedAvailable: true);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse("the block is symmetric (CR 601.3)");
    }

    [Fact]
    public void Block_Lifts_WhenPrelateLeavesBattlefield()
    {
        var bus = new EventBus();
        var prelate = SanctumPrelateFactory.Create(_alice, chosenNumber: 3, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(prelate);
        prelate.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(prelate, ZoneType.Stack, ZoneType.Battlefield));
        CastingRestrictions.IsNoncreatureManaValueBlocked(3).Should().BeTrue();

        // Prelate dies — leaves the battlefield. Block lifts (CR 603.6).
        _alice.Zones.Battlefield.RemoveCard(prelate);
        _alice.Zones.Graveyard.AddCard(prelate);
        prelate.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(prelate, ZoneType.Battlefield, ZoneType.Graveyard));

        CastingRestrictions.IsNoncreatureManaValueBlocked(3).Should().BeFalse(
            "the static lifts when its source leaves the battlefield");
    }

    [Fact]
    public void ChosenNumberZero_BlocksManaValueZeroNoncreatureSpells()
    {
        // Zero is a legal choice (CR 614.1c) — blocks mv-0 noncreature
        // spells (Mox-style artifacts, Memnite is a creature so excluded).
        var bus = new EventBus();
        var prelate = SanctumPrelateFactory.Create(_alice, chosenNumber: 0, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(prelate);
        prelate.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(prelate, ZoneType.Stack, ZoneType.Battlefield));

        var mox = new Artifact("Mox Opal", "{0}"); // mv 0
        mox.SetOwner(_bob);
        var action = new CastSpellAction(mox, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse(
            "mv-0 noncreature spells are blocked when 0 is chosen");
    }
}
