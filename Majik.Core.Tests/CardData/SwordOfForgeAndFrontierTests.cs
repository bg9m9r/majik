using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SwordOfForgeAndFrontierFactory"/> (Modern
/// Horizons 3, {3}). Completes the nine-card "Sword of X and Y" cycle.
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch + Equipment shape (Artifact + Equipment subtype).
/// - Equip activated ability shape: {2} mana cost.
/// - Static +2/+2 effect: equipped 2/2 Bear becomes 4/4.
/// - Protection markers: "red" + "green" ProtectionAbility instances present;
///   <see cref="Protection.HasProtectionFromColor"/> answers true for both.
/// - Combat-damage-to-a-player trigger: condition gates on equipped creature
///   + non-null TargetPlayer.
/// - Resolution: draw 1 + (v1 deterministic decline-discard) bump land cap.
/// </summary>
public class SwordOfForgeAndFrontierTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfForgeAndFrontier_Identity()
    {
        var c = SwordOfForgeAndFrontierFactory.Create(_alice);

        c.Name.Should().Be("Sword of Forge and Frontier");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Sword of Forge and Frontier is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwordOfForgeAndFrontier_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sword of Forge and Frontier", _alice);

        c.Should().BeOfType<Artifact>("Sword of Forge and Frontier is an Artifact");
        c.Name.Should().Be("Sword of Forge and Frontier");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage-to-a-player trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Equip {2} is wired");
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2,
            "protection from red + green markers ride on the equipment");
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfForgeAndFrontier_EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfForgeAndFrontierFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static continuous effect — +2/+2
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfForgeAndFrontier_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfForgeAndFrontierFactory.Create(
            _alice, svc, triggers: null, landDropTracker: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2 power from Sword of Forge and Frontier");
        bear.GetToughness().Should().Be(4, "+2 toughness from Sword of Forge and Frontier");
    }

    // -----------------------------------------------------------------------
    // Protection markers
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfForgeAndFrontier_HasProtectionFromRedAndGreen_Markers()
    {
        var sword = SwordOfForgeAndFrontierFactory.Create(_alice);

        var qualities = sword.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().BeEquivalentTo(new[] { "red", "green" },
            "Sword of Forge and Frontier carries protection-from-red + protection-from-green markers");

        Protection.HasProtectionFromColor(sword, ManaColor.Red).Should().BeTrue(
            "the 'red' marker is visible to Protection helpers");
        Protection.HasProtectionFromColor(sword, ManaColor.Green).Should().BeTrue(
            "the 'green' marker is visible to Protection helpers");
        Protection.HasProtectionFromColor(sword, ManaColor.White).Should().BeFalse(
            "no protection-from-white marker is attached");
    }

    // -----------------------------------------------------------------------
    // Combat-damage-to-a-player trigger — condition gating
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfForgeAndFrontier_CombatTrigger_GatesOnEquippedCreatureAndPlayerTarget()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var other = new Creature("Other", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfForgeAndFrontierFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();

        // Equipped Bear damages a player → matches.
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue("equipped creature dealt combat damage to a player (CR 510)");

        // A different (unequipped) creature damages a player → does not match.
        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, 2))
            .Should().BeFalse("trigger fires only for the equipped creature, not any creature");

        // Equipped Bear damages a creature (not a player) → does not match.
        var dummy = new Creature("Dummy", "1G", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, dummy, 2))
            .Should().BeFalse("printed text gates on 'to a player'");
    }

    // -----------------------------------------------------------------------
    // Combat-damage trigger — resolution effect (draw 1 + bump land cap)
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfForgeAndFrontier_CombatTrigger_DrawsOne_AndBumpsLandCap()
    {
        var tracker = new LandDropTracker();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfForgeAndFrontierFactory.Create(
            _alice, continuousEffects: null, triggers: null, landDropTracker: tracker);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        // Seed library so the draw resolves.
        var top = new Creature("Top", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Baseline: 1 land drop per turn.
        tracker.MaxLandDropsThisTurn(_alice).Should().Be(1);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        // Resolve the effect.
        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "the printed draw resolves first");
        _alice.Zones.Hand.GetCards().Single().Should().BeSameAs(top,
            "top card was drawn");
        tracker.MaxLandDropsThisTurn(_alice).Should().Be(2,
            "v1 declines the optional discard so the extra-land branch fires (CR 305.2)");
    }

    [Fact]
    public void SwordOfForgeAndFrontier_CombatTrigger_NoTracker_StillDraws()
    {
        // Shape-only path: no LandDropTracker. The draw still resolves;
        // the extra-land branch silently no-ops.
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfForgeAndFrontierFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var top = new Creature("Top", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "draw resolves on the shape-only path");
    }
}
