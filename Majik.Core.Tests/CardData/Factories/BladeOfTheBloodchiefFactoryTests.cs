using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BladeOfTheBloodchiefFactory"/>.
///
/// Card: Blade of the Bloodchief — Artifact — Equipment (Zendikar, {1}).
///   "Whenever a creature dies, put a +1/+1 counter on equipped creature.
///    If equipped creature is a Vampire, put two +1/+1 counters on it
///    instead."
///   "Equip {1}."
///
/// Equipment shape + Equip {1} are line-for-line
/// <see cref="BonesplitterFactory"/>; the death-trigger reuses the
/// "whenever a creature dies" shape from
/// <see cref="FalkenrathNobleFactory"/> (CR 603.1 + CR 700.4), differing
/// only in the resolution effect (counters on the equipped creature, not
/// a life drain).
/// </summary>
[Trait("Color", "C")]
public class BladeOfTheBloodchiefFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BladeOfTheBloodchief_Identity()
    {
        var c = BladeOfTheBloodchiefFactory.Create(_alice);

        c.Name.Should().Be("Blade of the Bloodchief");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Blade of the Bloodchief is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Blade has a single 'whenever a creature dies' trigger");
    }
    [Fact]
    public void BladeOfTheBloodchief_EquipAbility_HasGenericOneCost()
    {
        var c = BladeOfTheBloodchiefFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1,
            "Equip {1} is the printed activation cost");
    }

    [Fact]
    public void BladeOfTheBloodchief_DeathTrigger_FiresForAnyCreature()
    {
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(blade);
        blade.SetZone(ZoneType.Battlefield);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        creature.SetController(_bob);

        var diesEvent = new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = blade.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(diesEvent).Should().BeTrue(
            "CR 603.1 + 700.4 — any creature dying triggers Blade, controller-agnostic");
    }

    [Fact]
    public void BladeOfTheBloodchief_DeathTrigger_DoesNotFireForNonCreature()
    {
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(blade);
        blade.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Trinket", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var moveEvent = new CardMovedEvent(artifact, ZoneType.Battlefield, ZoneType.Graveyard);

        var trigger = blade.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "Blade's trigger reads 'creature' — non-creature deaths skip");
    }

    [Fact]
    public void BladeOfTheBloodchief_DeathTrigger_DoesNotFireOnExile()
    {
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(blade);
        blade.SetZone(ZoneType.Battlefield);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);

        var exileEvent = new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Exile);

        var trigger = blade.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(exileEvent).Should().BeFalse(
            "CR 700.4 — exile is not death");
    }

    [Fact]
    public void BladeOfTheBloodchief_NonVampire_GetsOneCounter()
    {
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        blade.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        blade.AttachTo(bear);

        var trigger = blade.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "non-Vampire equipped creature gets a single +1/+1 counter");
    }

    [Fact]
    public void BladeOfTheBloodchief_Vampire_GetsTwoCounters()
    {
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        blade.Zone = ZoneType.Battlefield;

        var vamp = new Creature("Vampire Nighthawk", "{1}{B}{B}", 2, 3,
            subtypes: new[] { CardSubtype.Vampire })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        blade.AttachTo(vamp);

        var trigger = blade.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        vamp.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "CR text: if equipped creature is a Vampire, put two +1/+1 counters instead");
    }

    [Fact]
    public void BladeOfTheBloodchief_Unequipped_NoCounters()
    {
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        blade.Zone = ZoneType.Battlefield;
        // intentionally not equipped — AttachedTo is null

        var trigger = blade.Abilities.OfType<TriggeredAbility>().Single();
        // Resolution must be a safe no-op when there's no equipped creature.
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow("unequipped Blade has no creature to place counters on");
    }
}
