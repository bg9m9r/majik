using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EnsnaringBridgeFactory"/>
/// (Stronghold, {3}).
///
/// Artifact. Oracle text:
///   "Creatures with power greater than the number of cards in your
///    hand can't attack."
///
/// Covers:
///   - Identity (name, type, mana cost, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - <see cref="StaticAbility"/> marker description and battlefield gate.
///   - Shape-only path leaves a standalone <see cref="ContinuousEffectsService"/>
///     untouched (no restriction registered).
///   - With an effects service: predicate-mode
///     <see cref="CombatRestrictionEffect"/> registered with
///     <see cref="CombatRestriction.CannotAttack"/>.
///   - Predicate semantics: power &gt; hand-size traps the creature;
///     power == hand-size and power &lt; hand-size are safe;
///     restriction is colour-blind / controller-blind (catches the
///     Bridge controller's own big creatures too).
///   - <see cref="ContinuousEffectsService.HasRestriction"/> re-evaluates
///     the predicate on every call — adding a card to hand lifts the
///     restriction, removing one re-imposes it.
///   - IsActive gate: when the Bridge leaves the battlefield, the
///     restriction is suppressed and pruned away.
/// </summary>
public class EnsnaringBridgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeBear(string name, int power, Player controller)
    {
        var c = new Creature(name, "{1}{G}", power, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        return c;
    }

    private static void AddDummyHandCard(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            // Any concrete Card with a unique name will do — Zone tracks by
            // reference, not by name.
            var c = new Creature($"HandFiller{Guid.NewGuid()}", "{1}", 1, 1);
            c.SetOwner(player);
            player.Zones.Hand.AddCard(c);
        }
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void EnsnaringBridge_Identity()
    {
        var bridge = EnsnaringBridgeFactory.Create(_alice);

        bridge.Name.Should().Be("Ensnaring Bridge");
        bridge.ManaCost.Should().Be("{3}");
        bridge.HasType(CardType.Artifact).Should().BeTrue();
        bridge.HasType(CardType.Creature).Should().BeFalse();
        bridge.Owner.Should().BeSameAs(_alice);
        bridge.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EnsnaringBridge_DispatchesViaNamedCardFactory()
    {
        var bridge = NamedCardFactory.Create("Ensnaring Bridge", _alice);

        bridge.Should().BeOfType<Artifact>();
        bridge.Name.Should().Be("Ensnaring Bridge");
        bridge.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Static-ability marker — printed text + battlefield gate
    // -------------------------------------------------------------------------

    [Fact]
    public void EnsnaringBridge_StaticAbilityMarker_CarriesPrintedText()
    {
        var bridge = EnsnaringBridgeFactory.Create(_alice);

        var statics = bridge.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().ContainSingle();
        statics[0].Description.Should().Be(
            EnsnaringBridgeFactory.StaticDescription);
    }

    [Fact]
    public void EnsnaringBridge_StaticAbility_IsActiveOnlyOnBattlefield()
    {
        var bridge = EnsnaringBridgeFactory.Create(_alice);
        var staticAbility = bridge.Abilities.OfType<StaticAbility>().Single();

        // No zone change yet — bridge is in the abstract default zone.
        staticAbility.IsActive().Should().BeFalse(
            "static abilities function only while their source is on the " +
            "battlefield (CR 603.6e)");

        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        staticAbility.IsActive().Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Shape-only path — no restriction registered
    // -------------------------------------------------------------------------

    [Fact]
    public void EnsnaringBridge_ShapeOnly_DoesNotRegisterRestriction()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice);
        bridge.Should().NotBeNull();

        var bigBear = MakeBear("BigBear", 7, _bob);

        effects.HasRestriction(bigBear, CombatRestriction.CannotAttack).Should().BeFalse(
            "shape-only Create overload does not install the predicate-mode " +
            "combat restriction");
    }

    // -------------------------------------------------------------------------
    // Predicate-mode restriction — power > hand-size catches the creature
    // -------------------------------------------------------------------------

    [Fact]
    public void EnsnaringBridge_CreatureWithPowerGreaterThanHandSize_CannotAttack()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        // Alice (Bridge controller) holds 2 cards.
        AddDummyHandCard(_alice, 2);

        var bigBear = MakeBear("BigBear", 5, _bob);
        effects.HasRestriction(bigBear, CombatRestriction.CannotAttack).Should().BeTrue(
            "5 > 2 — predicate trips, CR 508.1c restriction applies");
    }

    [Fact]
    public void EnsnaringBridge_CreatureWithPowerEqualToHandSize_CanAttack()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        AddDummyHandCard(_alice, 3);

        var bear = MakeBear("ThreePowerBear", 3, _bob);
        effects.HasRestriction(bear, CombatRestriction.CannotAttack).Should().BeFalse(
            "printed text is 'power GREATER than' (strict); 3 > 3 is false");
    }

    [Fact]
    public void EnsnaringBridge_SmallCreature_CanAttack()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        AddDummyHandCard(_alice, 4);

        var bear = MakeBear("TwoPowerBear", 2, _bob);
        effects.HasRestriction(bear, CombatRestriction.CannotAttack).Should().BeFalse(
            "2 < 4 — predicate is false");
    }

    [Fact]
    public void EnsnaringBridge_AppliesToBridgeControllersOwnCreaturesToo()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        AddDummyHandCard(_alice, 1);

        // Alice's own 4-power bear is also stopped — Ensnaring Bridge is
        // famously symmetric. (The 8-Rack / Lantern Control trade-off.)
        var ownBigBear = MakeBear("AlicesOwnBigBear", 4, _alice);
        effects.HasRestriction(ownBigBear, CombatRestriction.CannotAttack).Should().BeTrue(
            "Ensnaring Bridge is colour-blind / controller-blind: any creature " +
            "with power > the Bridge controller's hand size is caught, " +
            "including the Bridge controller's own");
    }

    [Fact]
    public void EnsnaringBridge_HandThresholdIsBridgeControllerSpecific()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        // Alice has 0 cards, Bob has 7 — the predicate reads ALICE's hand
        // (the Bridge controller's), so a 1-power creature is caught.
        AddDummyHandCard(_bob, 7);

        var oneOne = MakeBear("OneOneBear", 1, _bob);
        effects.HasRestriction(oneOne, CombatRestriction.CannotAttack).Should().BeTrue(
            "'your hand' in a static ability refers to the ability's " +
            "controller (CR 109.5); Bob's hand size is irrelevant");
    }

    // -------------------------------------------------------------------------
    // Predicate re-evaluation — hand-size fluctuations take effect immediately
    // -------------------------------------------------------------------------

    [Fact]
    public void EnsnaringBridge_DrawingCardLiftsRestriction()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        AddDummyHandCard(_alice, 2);
        var bear = MakeBear("ThreePowerBear", 3, _bob);
        effects.HasRestriction(bear, CombatRestriction.CannotAttack).Should().BeTrue(
            "3 > 2 — trapped");

        // Draw a card — hand goes 2 -> 3.
        AddDummyHandCard(_alice, 1);

        effects.HasRestriction(bear, CombatRestriction.CannotAttack).Should().BeFalse(
            "3 > 3 is false — predicate re-evaluates per query so the new " +
            "hand size lifts the restriction immediately");
    }

    [Fact]
    public void EnsnaringBridge_DiscardingCardReimposesRestriction()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        AddDummyHandCard(_alice, 4);
        var bear = MakeBear("ThreePowerBear", 3, _bob);
        effects.HasRestriction(bear, CombatRestriction.CannotAttack).Should().BeFalse(
            "3 < 4 — safe");

        // Discard down to 2 cards in hand.
        var hand = _alice.Zones.Hand.GetCards().Take(2).ToList();
        foreach (var c in hand) _alice.Zones.Hand.RemoveCard(c);

        effects.HasRestriction(bear, CombatRestriction.CannotAttack).Should().BeTrue(
            "3 > 2 — predicate re-evaluates per query so dropping cards " +
            "re-imposes the restriction immediately");
    }

    // -------------------------------------------------------------------------
    // IsActive gate — restriction is suppressed off-battlefield
    // -------------------------------------------------------------------------

    [Fact]
    public void EnsnaringBridge_OffBattlefield_RestrictionIsSuppressed()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice, effects);
        // Note: NOT moved to the battlefield.

        AddDummyHandCard(_alice, 0);
        var bigBear = MakeBear("BigBear", 5, _bob);

        effects.HasRestriction(bigBear, CombatRestriction.CannotAttack).Should().BeFalse(
            "static-ability source not on the battlefield (CR 603.6e) — " +
            "the IsActive gate suppresses the restriction");
    }

    [Fact]
    public void EnsnaringBridge_LeavesBattlefield_RestrictionIsPrunedAndGone()
    {
        var effects = new ContinuousEffectsService();
        var bridge = EnsnaringBridgeFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        AddDummyHandCard(_alice, 0);
        var bigBear = MakeBear("BigBear", 5, _bob);
        effects.HasRestriction(bigBear, CombatRestriction.CannotAttack).Should().BeTrue(
            "sanity — Bridge on battlefield, restriction active");

        // Bridge leaves the battlefield.
        _alice.Zones.Battlefield.RemoveCard(bridge);
        _alice.Zones.Graveyard.AddCard(bridge);
        bridge.SetZone(ZoneType.Graveyard);

        effects.HasRestriction(bigBear, CombatRestriction.CannotAttack).Should().BeFalse(
            "IsActive gate flips false once Bridge leaves the battlefield");

        // Prune should drop the (now inactive) effect entirely.
        effects.Prune();
        effects.HasRestriction(bigBear, CombatRestriction.CannotAttack).Should().BeFalse(
            "post-prune, the effect is removed from the service");
    }
}
