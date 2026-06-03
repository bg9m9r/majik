using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Tinybones, the Pickpocket (Modern Horizons 3, {B}).
///
/// Oracle: "Deathtouch. Whenever Tinybones deals combat damage to a player,
/// you may cast target nonland permanent card from that player's graveyard,
/// and mana of any type can be spent to cast that spell."
///
/// Covers:
///   - Identity (Legendary Skeleton Rogue 1/1, {B}, Deathtouch).
///   - NamedCardFactory dispatch.
///   - Combat-damage-to-a-player trigger structure (active on battlefield).
///   - Mechanic (CR 601.3e): combat damage to the opponent stamps a non-owner
///     graveyard-cast grant on the damaged player's nonland permanent cards,
///     nominating the TINYBONES CONTROLLER (not the card's owner) as the
///     allowed caster, with "mana of any type" (all-generic cost).
///   - <see cref="GraveyardNonOwnerCastAlternativeCost"/> is legal for the
///     nominated caster (a non-owner) and ONLY for them.
///   - Land cards and instant/sorcery cards in the graveyard are NOT granted.
///   - Damage to a creature (not a player) does NOT fire the trigger.
/// </summary>
public class TinybonesThePickpocketTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PlaceOnBattlefield(Player controller, Creature card)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static Card PutInGraveyard(Player owner, Card card)
    {
        card.SetOwner(owner);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        return card;
    }

    // -------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------

    [Fact]
    public void Tinybones_Identity_LegendarySkeletonRogue_1_1_AtCostB_Deathtouch()
    {
        var card = TinybonesThePickpocketFactory.Create(_alice);

        card.Name.Should().Be("Tinybones, the Pickpocket");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Skeleton).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Deathtouch");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Tinybones_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Tinybones, the Pickpocket", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Tinybones, the Pickpocket");
        c.HasSubtype(CardSubtype.Skeleton).Should().BeTrue();
    }

    [Fact]
    public void Tinybones_HasSingleTriggeredAbility()
    {
        var card = TinybonesThePickpocketFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Trigger_OnlyActiveOnBattlefield()
    {
        var card = TinybonesThePickpocketFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    // -------------------------------------------------------------------
    // Mechanic — CR 601.3e non-owner graveyard cast
    // -------------------------------------------------------------------

    [Fact]
    public void CombatDamageToOpponent_GrantsNonOwnerGraveyardCast_OnOpponentsNonlandPermanent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tinybones = TinybonesThePickpocketFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, tinybones);

        // Bob (the victim) owns a creature card and a land card in his yard.
        var bobBear = PutInGraveyard(_bob, new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        var bobLand = PutInGraveyard(_bob, new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        var bobBolt = PutInGraveyard(_bob, new Instant("Lightning Bolt", "{R}"));

        // Tinybones connects with Bob.
        bus.Publish(new CombatDamageDealtEvent(tinybones, _bob, amount: 1));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // The nonland permanent card is granted — to Alice (the NON-owner).
        bobBear.RuntimeGraveyardNonOwnerCastAllowedCaster.Should().BeSameAs(_alice);
        bobBear.RuntimeGraveyardNonOwnerCastAnyTypeMana.Should().BeTrue();
        // "mana of any type" → all-generic cost of equal mana value ({1}{G} = 2).
        bobBear.RuntimeGraveyardNonOwnerCastCost!.TotalValue.Should().Be(2);
        bobBear.RuntimeGraveyardNonOwnerCastCost!.Generic.Should().Be(2);

        // The land card is NOT granted ("nonland permanent card").
        bobLand.RuntimeGraveyardNonOwnerCastAllowedCaster.Should().BeNull();
        // The instant is NOT a permanent card.
        bobBolt.RuntimeGraveyardNonOwnerCastAllowedCaster.Should().BeNull();
    }

    [Fact]
    public void GrantedCard_IsCastable_ByNonOwner_NotByOwner()
    {
        var bobBear = PutInGraveyard(_bob, new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        bobBear.GrantRuntimeGraveyardNonOwnerCast(_alice, Majik.Core.ValueObjects.ManaCost.Parse("{2}"), anyTypeMana: true);

        var altCost = new GraveyardNonOwnerCastAlternativeCost(
            "Tinybones — cast from opponent's graveyard", bobBear.RuntimeGraveyardNonOwnerCastCost!);

        // Alice (non-owner, nominated caster) may cast it from Bob's graveyard.
        altCost.CanCastFor(bobBear, _alice).Should().BeTrue();
        // Bob (the owner) is NOT the nominated caster — he may NOT use this grant.
        altCost.CanCastFor(bobBear, _bob).Should().BeFalse();
    }

    [Fact]
    public void Grant_OnlyValidWhileInGraveyard()
    {
        var bobBear = PutInGraveyard(_bob, new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        bobBear.GrantRuntimeGraveyardNonOwnerCast(_alice, Majik.Core.ValueObjects.ManaCost.Parse("{2}"));
        var altCost = new GraveyardNonOwnerCastAlternativeCost("x", Majik.Core.ValueObjects.ManaCost.Parse("{2}"));

        altCost.CanCastFor(bobBear, _alice).Should().BeTrue();

        // Move it out of the graveyard — the alt cost no longer applies.
        bobBear.SetZone(ZoneType.Stack);
        altCost.CanCastFor(bobBear, _alice).Should().BeFalse();
    }

    [Fact]
    public void ClearedGrant_NoLongerCastable()
    {
        var bobBear = PutInGraveyard(_bob, new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        bobBear.GrantRuntimeGraveyardNonOwnerCast(_alice, Majik.Core.ValueObjects.ManaCost.Parse("{2}"));
        var altCost = new GraveyardNonOwnerCastAlternativeCost("x", Majik.Core.ValueObjects.ManaCost.Parse("{2}"));

        altCost.CanCastFor(bobBear, _alice).Should().BeTrue();
        bobBear.ClearRuntimeGraveyardNonOwnerCast();
        altCost.CanCastFor(bobBear, _alice).Should().BeFalse();
    }

    [Fact]
    public void CombatDamageToCreature_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tinybones = TinybonesThePickpocketFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(_alice, tinybones);

        var blocker = new Creature("Wall", "{0}", 0, 4);
        bus.Publish(new CombatDamageDealtEvent(tinybones, blocker, amount: 1));

        triggers.PendingCount.Should().Be(0, "only damage to a PLAYER fires Tinybones");
    }
}
