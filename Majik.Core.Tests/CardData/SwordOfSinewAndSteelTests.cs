using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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
/// Unit tests for <see cref="SwordOfSinewAndSteelFactory"/> (Modern
/// Horizons 2, {3}).
///
/// Covers:
/// - Identity (name, type, mana cost, Equipment subtype, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip {2} activated ability cost.
/// - +2/+2 boost to equipped creature via AttachedBoostEffect (Layer 7c).
/// - Protection from black + red.
/// - Combat-damage-to-a-player trigger gating.
/// - Resolution: destroys both chosen planeswalker and artifact targets.
/// - Resolution: 0..1 cardinality — empty target slot is allowed.
/// </summary>
public class SwordOfSinewAndSteelTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SwordOfSinewAndSteel_Identity()
    {
        var c = SwordOfSinewAndSteelFactory.Create(_alice);

        c.Name.Should().Be("Sword of Sinew and Steel");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwordOfSinewAndSteel_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sword of Sinew and Steel", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Sword of Sinew and Steel");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void SwordOfSinewAndSteel_EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfSinewAndSteelFactory.Create(_alice);

        var equip = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2);
    }

    [Fact]
    public void SwordOfSinewAndSteel_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfSinewAndSteelFactory.Create(_alice, svc, triggers: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4);
        bear.GetToughness().Should().Be(4);
    }

    [Fact]
    public void SwordOfSinewAndSteel_HasProtectionFromBlackAndRed_Markers()
    {
        var sword = SwordOfSinewAndSteelFactory.Create(_alice);

        var qualities = sword.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().BeEquivalentTo(new[] { "black", "red" });

        Protection.HasProtectionFromColor(sword, ManaColor.Black).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.Red).Should().BeTrue();
        Protection.HasProtectionFromColor(sword, ManaColor.White).Should().BeFalse();
        Protection.HasProtectionFromColor(sword, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(sword, ManaColor.Green).Should().BeFalse();
    }

    [Fact]
    public void SwordOfSinewAndSteel_CombatTrigger_GatesOnEquippedCreatureAndPlayerTarget()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfSinewAndSteelFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue();

        var blocker = new Creature("Blocker", "1G", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, blocker, 2))
            .Should().BeFalse("printed text gates on 'to a player'");
    }

    [Fact]
    public void SwordOfSinewAndSteel_OnCombatDamage_DestroysBothChosenTargets()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfSinewAndSteelFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        // Bob has a planeswalker and an artifact on the battlefield.
        var walker = new Planeswalker("Some Walker", "3R", 4)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var artifact = new Artifact("Some Artifact", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        _bob.Zones.Battlefield.AddCard(walker);
        _bob.Zones.Battlefield.AddCard(artifact);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { walker },
            new object[] { artifact },
        });

        foreach (var effect in trigger.Effects) effect.Execute();

        walker.Zone.Should().Be(ZoneType.Graveyard,
            "the chosen planeswalker is destroyed (CR 701.7)");
        artifact.Zone.Should().Be(ZoneType.Graveyard,
            "the chosen artifact is destroyed (CR 701.7)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(walker);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(artifact);
    }

    [Fact]
    public void SwordOfSinewAndSteel_OnCombatDamage_AllowsEmptyTargetSlots()
    {
        // "Up to one" — CR 115.3 permits zero. Empty target slots
        // should be a no-op (the trigger still resolves cleanly).
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfSinewAndSteelFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var artifact = new Artifact("Some Artifact", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        _bob.Zones.Battlefield.AddCard(artifact);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),       // skipped planeswalker
            new object[] { artifact },   // only the artifact half is used
        });

        foreach (var effect in trigger.Effects) effect.Execute();

        artifact.Zone.Should().Be(ZoneType.Graveyard);
    }
}
