using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SwordOfTruthAndJusticeFactory"/> (Modern
/// Horizons, {3}).
///
/// Covers:
/// - Identity (name, type, mana cost, Equipment subtype, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip {2} activated ability cost.
/// - +2/+2 boost to equipped creature via AttachedBoostEffect (Layer 7c).
/// - Protection from white + blue (markers on equipment card under the
///   shape-only path).
/// - Combat-damage-to-a-player trigger gating (equipped creature +
///   non-null TargetPlayer).
/// - Resolution: +1/+1 counter on equipped creature + proliferate adds
///   another counter of an existing kind to controller's permanents.
/// </summary>
public class SwordOfTruthAndJusticeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SwordOfTruthAndJustice_Identity()
    {
        var c = SwordOfTruthAndJusticeFactory.Create(_alice);

        c.Name.Should().Be("Sword of Truth and Justice");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwordOfTruthAndJustice_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sword of Truth and Justice", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Sword of Truth and Justice");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Equip {2} is the only activated ability");
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2,
            "protection-from-white + protection-from-blue markers ride on the equipment");
    }

    [Fact]
    public void SwordOfTruthAndJustice_EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfTruthAndJusticeFactory.Create(_alice);

        var equip = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2, "Equip {2} is the printed activation cost");
    }

    [Fact]
    public void SwordOfTruthAndJustice_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfTruthAndJusticeFactory.Create(_alice, svc, triggers: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+2 boost from Sword of Truth and Justice");
        bear.GetToughness().Should().Be(4);
    }

    [Fact]
    public void SwordOfTruthAndJustice_HasProtectionFromWhiteAndBlue_Markers()
    {
        var sword = SwordOfTruthAndJusticeFactory.Create(_alice);

        var qualities = sword.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().BeEquivalentTo(new[] { "white", "blue" });

        Protection.HasProtectionFromColor(sword, ManaColor.White).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.Blue).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.Red).Should().BeFalse();
        Protection.HasProtectionFromColor(sword, ManaColor.Black).Should().BeFalse();
        Protection.HasProtectionFromColor(sword, ManaColor.Green).Should().BeFalse();
    }

    [Fact]
    public void SwordOfTruthAndJustice_CombatTrigger_GatesOnEquippedCreatureAndPlayerTarget()
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
        var sword = SwordOfTruthAndJusticeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue("equipped creature dealt combat damage to a player");

        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, 2))
            .Should().BeFalse("only the equipped creature feeds the trigger");

        var blocker = new Creature("Blocker", "1G", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, blocker, 2))
            .Should().BeFalse("printed text gates on 'to a player'");
    }

    [Fact]
    public void SwordOfTruthAndJustice_OnCombatDamage_PutsPlusOneCounterOnEquippedCreature()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfTruthAndJusticeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Sword of Truth and Justice puts a +1/+1 counter on equipped creature");
    }

    [Fact]
    public void SwordOfTruthAndJustice_Proliferate_AddsCounterToAlreadyCounteredPermanent()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfTruthAndJusticeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        // A separate +1/+1-counter-bearing creature on Alice's side
        // (proliferate should also pump this guy).
        var hydra = new Creature("Hydra", "GG", 0, 0)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        hydra.Counters.Add(CounterType.PlusOnePlusOne, 3);
        _alice.Zones.Battlefield.AddCard(hydra);

        // A creature on Alice's side with zero counters — should NOT
        // gain one (proliferate adds to existing-counter permanents only).
        var zeroCounterDude = new Creature("Vanilla", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(zeroCounterDude);

        _alice.Zones.Battlefield.AddCard(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "first +1/+1 from the rider, then proliferate adds one more");
        hydra.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(4,
            "hydra had a +1/+1 counter so proliferate adds one more (CR 701.27)");
        zeroCounterDude.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "vanilla creature had no counters — proliferate skips it");
    }
}
