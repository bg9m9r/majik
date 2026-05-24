using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TerritorialKavuFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 2/2, Kavu subtype, mana cost,
///   owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Domain P/T pump (Layer 7c):
///   - 0 basic land types → 2/2 (base only).
///   - 3 basic land types → 5/5 (2 + 3).
///   - 5 basic land types → 7/7 (2 + 5).
///   - Only the controller's lands count (opponent's lands don't).
///   - Non-basic land types (Wastes) don't count.
/// - Attack trigger loot:
///   - Card in hand → discard first card, then draw.
///   - Empty hand → no-op.
///   - Attack trigger fires only on Kavu itself (not other attackers).
/// </summary>
public class TerritorialKavuTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly ContinuousEffectsService _effects = new();
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public TerritorialKavuTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TerritorialKavu_Identity()
    {
        var kavu = TerritorialKavuFactory.Create(_alice);

        kavu.Name.Should().Be("Territorial Kavu");
        kavu.HasType(CardType.Creature).Should().BeTrue();
        kavu.Power.Should().Be(2);
        kavu.Toughness.Should().Be(2);
        kavu.HasSubtype(CardSubtype.Kavu).Should().BeTrue("Territorial Kavu is a Kavu (CR 205.3m)");
        kavu.ManaCost.Should().Be("{G}{W}");
        kavu.Owner.Should().BeSameAs(_alice);
        kavu.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TerritorialKavu_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Territorial Kavu", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Territorial Kavu");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Kavu).Should().BeTrue();
        card.ManaCost.Should().Be("{G}{W}");
    }

    // -----------------------------------------------------------------------
    // Domain P/T pump — Layer 7c static effect
    // -----------------------------------------------------------------------

    [Fact]
    public void TerritorialKavu_Domain_ZeroBasicLandTypes_IsBaseStatLine()
    {
        // With no lands on battlefield, domain count = 0 → no pump.
        var kavu = TerritorialKavuFactory.Create(_alice, _effects, _bus, triggers: null);
        _zones.MoveCard(kavu, ZoneType.Library, ZoneType.Battlefield, _alice);

        var chars = _effects.Compute(kavu);

        chars.Power.Should().Be(2, "0 basic land types → no Domain pump, base 2/2");
        chars.Toughness.Should().Be(2, "0 basic land types → no Domain pump, base 2/2");
    }

    [Fact]
    public void TerritorialKavu_Domain_ThreeBasicLandTypes_IsFiveFive()
    {
        // Forest + Island + Plains on the battlefield under Alice.
        var kavu = TerritorialKavuFactory.Create(_alice, _effects, _bus, triggers: null);
        _zones.MoveCard(kavu, ZoneType.Library, ZoneType.Battlefield, _alice);

        AddLand(_alice, CardSubtype.Forest);
        AddLand(_alice, CardSubtype.Island);
        AddLand(_alice, CardSubtype.Plains);

        var chars = _effects.Compute(kavu);

        chars.Power.Should().Be(5, "3 basic land types → +3/+3 from Domain, 2+3=5");
        chars.Toughness.Should().Be(5, "3 basic land types → +3/+3 from Domain, 2+3=5");
    }

    [Fact]
    public void TerritorialKavu_Domain_FiveBasicLandTypes_IsSevenSeven()
    {
        // All five basic land types under Alice.
        var kavu = TerritorialKavuFactory.Create(_alice, _effects, _bus, triggers: null);
        _zones.MoveCard(kavu, ZoneType.Library, ZoneType.Battlefield, _alice);

        AddLand(_alice, CardSubtype.Forest);
        AddLand(_alice, CardSubtype.Island);
        AddLand(_alice, CardSubtype.Plains);
        AddLand(_alice, CardSubtype.Swamp);
        AddLand(_alice, CardSubtype.Mountain);

        var chars = _effects.Compute(kavu);

        chars.Power.Should().Be(7, "5 basic land types → +5/+5 from Domain, 2+5=7");
        chars.Toughness.Should().Be(7, "5 basic land types → +5/+5 from Domain, 2+5=7");
    }

    [Fact]
    public void TerritorialKavu_Domain_DuplicateLandTypes_CountOnce()
    {
        // Two Forests: domain should still be 1 (not 2).
        var kavu = TerritorialKavuFactory.Create(_alice, _effects, _bus, triggers: null);
        _zones.MoveCard(kavu, ZoneType.Library, ZoneType.Battlefield, _alice);

        AddLand(_alice, CardSubtype.Forest);
        AddLand(_alice, CardSubtype.Forest); // duplicate

        var chars = _effects.Compute(kavu);

        chars.Power.Should().Be(3, "2× Forest counts as only 1 land type → +1/+1");
        chars.Toughness.Should().Be(3, "2× Forest counts as only 1 land type → +1/+1");
    }

    [Fact]
    public void TerritorialKavu_Domain_WastesDoesNotCount()
    {
        // Wastes is a basic land but NOT a basic land TYPE (CR 702.16 / 305.6).
        var kavu = TerritorialKavuFactory.Create(_alice, _effects, _bus, triggers: null);
        _zones.MoveCard(kavu, ZoneType.Library, ZoneType.Battlefield, _alice);

        AddLand(_alice, CardSubtype.Wastes);

        var chars = _effects.Compute(kavu);

        chars.Power.Should().Be(2, "Wastes is not a basic land type for Domain purposes");
        chars.Toughness.Should().Be(2, "Wastes is not a basic land type for Domain purposes");
    }

    [Fact]
    public void TerritorialKavu_Domain_OpponentLandsDoNotCount()
    {
        // Bob controls basic lands — they should not boost Alice's Kavu.
        var kavu = TerritorialKavuFactory.Create(_alice, _effects, _bus, triggers: null);
        _zones.MoveCard(kavu, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Bob has all five basic land types.
        AddLand(_bob, CardSubtype.Forest);
        AddLand(_bob, CardSubtype.Island);
        AddLand(_bob, CardSubtype.Plains);
        AddLand(_bob, CardSubtype.Swamp);
        AddLand(_bob, CardSubtype.Mountain);

        var chars = _effects.Compute(kavu);

        chars.Power.Should().Be(2,
            "opponent's lands don't count for Domain — only the controller's lands (CR 702.16)");
        chars.Toughness.Should().Be(2,
            "opponent's lands don't count for Domain — only the controller's lands (CR 702.16)");
    }

    [Fact]
    public void TerritorialKavu_Domain_PumpInactiveWhenNotOnBattlefield()
    {
        // Domain pump should only apply while Kavu is on the battlefield.
        var kavu = TerritorialKavuFactory.Create(_alice, _effects, _bus, triggers: null);
        // Kavu stays in library (never moved to battlefield) — ZoneService
        // has not fired a CardMovedEvent → Battlefield, so the lifecycle
        // has not registered the DomainPumpStaticEffect.

        AddLand(_alice, CardSubtype.Forest);
        AddLand(_alice, CardSubtype.Island);

        var chars = _effects.Compute(kavu);

        // When the effect is not yet registered (not on battlefield),
        // Compute starts from printed base values only.
        chars.Power.Should().Be(2,
            "DomainPumpStaticEffect is not registered when Kavu is not on the battlefield");
        chars.Toughness.Should().Be(2,
            "DomainPumpStaticEffect is not registered when Kavu is not on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — loot on attack (CR 508.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void TerritorialKavu_AttackTrigger_WithCardInHand_DiscardsAndDraws()
    {
        var kavu = TerritorialKavuFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kavu);
        kavu.SetZone(ZoneType.Battlefield);

        // Put a card in hand and a card in library.
        var handCard = new Creature("Grizzly Bears", "1G", 2, 2);
        handCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(handCard);

        var libraryCard = new Creature("Hill Giant", "3R", 3, 3);
        libraryCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libraryCard);

        var attackTrigger = kavu.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        // Resolve the attack trigger effect.
        foreach (var effect in attackTrigger.Effects) effect.Execute();

        // handCard should be discarded (hand → graveyard).
        _alice.Zones.Hand.GetCards().Should().NotContain(handCard,
            "the discarded card moves from hand to graveyard");
        _alice.Zones.Graveyard.GetCards().Should().Contain(handCard,
            "the discarded card ends in the graveyard");

        // libraryCard should be drawn (library → hand).
        _alice.Zones.Library.GetCards().Should().NotContain(libraryCard,
            "the drawn card leaves the library");
        _alice.Zones.Hand.GetCards().Should().Contain(libraryCard,
            "the drawn card ends in hand");
    }

    [Fact]
    public void TerritorialKavu_AttackTrigger_EmptyHand_IsNoOp()
    {
        var kavu = TerritorialKavuFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kavu);
        kavu.SetZone(ZoneType.Battlefield);

        // Library has a card but hand is empty.
        var libraryCard = new Creature("Hill Giant", "3R", 3, 3);
        libraryCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libraryCard);

        _alice.Zones.Hand.GetCards().Should().BeEmpty("precondition: hand is empty");

        var attackTrigger = kavu.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        var act = () => { foreach (var effect in attackTrigger.Effects) effect.Execute(); };

        act.Should().NotThrow("empty-hand no-op path should not throw");
        _alice.Zones.Library.GetCards().Should().Contain(libraryCard,
            "no discard happened → no draw happened; library is unchanged");
        _alice.Zones.Hand.GetCards().Should().BeEmpty("hand remains empty");
    }

    [Fact]
    public void TerritorialKavu_AttackTrigger_FiresOnlyForKavuItself()
    {
        var kavu = TerritorialKavuFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kavu);
        kavu.SetZone(ZoneType.Battlefield);

        var attackTrigger = kavu.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        // Event for Kavu itself should trigger.
        var kavuAttacks = new CreatureAttacksEvent(kavu, _bob);
        attackTrigger.IsTriggered(kavuAttacks).Should().BeTrue(
            "attack trigger matches CreatureAttacksEvent where the attacker IS Territorial Kavu");

        // Event for a different attacker should NOT trigger.
        var other = new Creature("Llanowar Elves", "G", 1, 1);
        other.SetOwner(_alice);
        other.SetController(_alice);
        other.SetZone(ZoneType.Battlefield);
        var otherAttacks = new CreatureAttacksEvent(other, _bob);
        attackTrigger.IsTriggered(otherAttacks).Should().BeFalse(
            "per-attacker trigger only fires for Territorial Kavu itself (Triggers.OnAttackSelf)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Create a land with the given basic subtype, set it under
    /// <paramref name="controller"/>'s control on the battlefield.
    /// </summary>
    private static void AddLand(Player controller, CardSubtype subtype)
    {
        var land = new Land(subtype.ToString(), supertypes: null, subtypes: new[] { subtype });
        land.SetOwner(controller);
        land.SetController(controller);
        controller.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
    }
}
