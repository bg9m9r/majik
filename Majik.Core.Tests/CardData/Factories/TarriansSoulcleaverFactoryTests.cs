using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TarriansSoulcleaverFactory"/>.
///
/// Card: Tarrian's Soulcleaver — Legendary Artifact — Equipment ({1}).
///   "Equipped creature has vigilance."
///   "Whenever another artifact or creature is put into a graveyard from
///    the battlefield, put a +1/+1 counter on equipped creature."
///   "Equip {2}"
///
/// Equipment shape + Equip {2} mirror <see cref="BonesplitterFactory"/>;
/// the vigilance grant mirrors <see cref="SwiftfootBootsFactory"/>'s
/// keyword grant; the dies-trigger counter accrual mirrors
/// <see cref="BladeOfTheBloodchiefFactory"/> (CR 603.6e + CR 700.4),
/// differing in the qualifying card (an artifact OR creature, and
/// *another* permanent).
/// </summary>
[Trait("Color", "C")]
public class TarriansSoulcleaverFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void TarriansSoulcleaver_Identity()
    {
        var c = TarriansSoulcleaverFactory.Create(_alice);

        c.Name.Should().Be("Tarrian's Soulcleaver");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Tarrian's Soulcleaver is a Legendary Artifact");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Tarrian's Soulcleaver is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Soulcleaver has a single 'whenever another artifact or creature dies' trigger");
    }

    [Fact]
    public void TarriansSoulcleaver_EquipAbility_HasGenericTwoCost()
    {
        var c = TarriansSoulcleaverFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    [Fact]
    public void TarriansSoulcleaver_GrantsVigilance_WhileEquipped()
    {
        var continuous = new ContinuousEffectsService();
        var soulcleaver = TarriansSoulcleaverFactory.Create(
            _alice, continuous, triggers: null, replacements: null);
        soulcleaver.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };

        // Before equip: no vigilance.
        CombatAbilities.HasVigilance(bear).Should().BeFalse(
            "an unequipped Grizzly Bears has no vigilance");

        soulcleaver.AttachTo(bear);

        CombatAbilities.HasVigilance(bear).Should().BeTrue(
            "CR 613.1f — the Layer-6 grant gives the equipped creature vigilance");
    }

    [Fact]
    public void TarriansSoulcleaver_DiesTrigger_FiresForAnotherCreature()
    {
        var soulcleaver = TarriansSoulcleaverFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(soulcleaver);
        soulcleaver.SetZone(ZoneType.Battlefield);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        creature.SetController(_bob);

        var diesEvent = new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = soulcleaver.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(diesEvent).Should().BeTrue(
            "CR 700.4 — any other creature dying triggers the Soulcleaver, controller-agnostic");
    }

    [Fact]
    public void TarriansSoulcleaver_DiesTrigger_FiresForAnotherArtifact()
    {
        var soulcleaver = TarriansSoulcleaverFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(soulcleaver);
        soulcleaver.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Trinket", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var moveEvent = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = soulcleaver.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeTrue(
            "the trigger reads 'artifact or creature' — a non-creature artifact dying still fires");
    }

    [Fact]
    public void TarriansSoulcleaver_DiesTrigger_DoesNotFireForItself()
    {
        var soulcleaver = TarriansSoulcleaverFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(soulcleaver);
        soulcleaver.SetZone(ZoneType.Battlefield);

        // The Soulcleaver is itself a Legendary Artifact, but "another"
        // (CR 109.5) excludes itself.
        var selfEvent = new CardMovedEvent(soulcleaver, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = soulcleaver.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(selfEvent).Should().BeFalse(
            "CR 109.5 — 'another' excludes the Soulcleaver dying itself");
    }

    [Fact]
    public void TarriansSoulcleaver_DiesTrigger_DoesNotFireForNonArtifactNonCreature()
    {
        var soulcleaver = TarriansSoulcleaverFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(soulcleaver);
        soulcleaver.SetZone(ZoneType.Battlefield);

        var land = new Land("Forest");
        land.SetOwner(_alice);
        land.SetController(_alice);

        var moveEvent = new CardMovedEvent(land, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = soulcleaver.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "a land that is neither artifact nor creature does not trigger the Soulcleaver");
    }

    [Fact]
    public void TarriansSoulcleaver_DiesTrigger_DoesNotFireOnExile()
    {
        var soulcleaver = TarriansSoulcleaverFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(soulcleaver);
        soulcleaver.SetZone(ZoneType.Battlefield);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var exileEvent = new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Exile);

        var trigger = soulcleaver.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(exileEvent).Should().BeFalse(
            "CR 700.4 — exile is not a graveyard, so it is not 'put into a graveyard'");
    }

    [Fact]
    public void TarriansSoulcleaver_Equipped_GetsOneCounter()
    {
        var soulcleaver = TarriansSoulcleaverFactory.Create(_alice);
        soulcleaver.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        soulcleaver.AttachTo(bear);

        var trigger = soulcleaver.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "each trigger puts a single +1/+1 counter on the equipped creature");
    }

    [Fact]
    public void TarriansSoulcleaver_Unequipped_NoCounters()
    {
        var soulcleaver = TarriansSoulcleaverFactory.Create(_alice);
        soulcleaver.Zone = ZoneType.Battlefield;
        // intentionally not equipped — AttachedTo is null

        var trigger = soulcleaver.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow("unequipped Soulcleaver has no creature to place counters on");
    }
}
