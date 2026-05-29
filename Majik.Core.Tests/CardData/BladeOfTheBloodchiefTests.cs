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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BladeOfTheBloodchiefFactory"/>
/// (Zendikar, {1}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified):
///   "Whenever a creature dies, put a +1/+1 counter on equipped creature.
///    If equipped creature is a Vampire, put two +1/+1 counters on it
///    instead."
///   "Equip {1}"
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip activated ability shape: {1} mana cost.
/// - Dies trigger condition matches Battlefield -> Graveyard for ANY
///   creature (CR 603.6c / 700.4), not bounces / non-creatures.
/// - Resolution: a +1/+1 counter lands on the equipped creature.
/// - Resolution: TWO +1/+1 counters when the equipped creature is a Vampire.
/// - Unattached Blade does nothing on resolution.
/// </summary>
public class BladeOfTheBloodchiefTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Blade_Identity()
    {
        var c = BladeOfTheBloodchiefFactory.Create(_alice);

        c.Name.Should().Be("Blade of the Bloodchief");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Blade of the Bloodchief is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Blade_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Blade of the Bloodchief", _alice);

        c.Should().BeOfType<Artifact>("Blade of the Bloodchief is an Artifact");
        c.Name.Should().Be("Blade of the Bloodchief");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "creature-dies trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Equip {1} is wired");
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void Blade_EquipAbility_HasGenericOneCost()
    {
        var c = BladeOfTheBloodchiefFactory.Create(_alice);

        var ability = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1, "Equip {1} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Dies trigger — condition
    // -----------------------------------------------------------------------

    [Fact]
    public void Blade_DiesTrigger_MatchesAnyCreatureDeath_NotBounceOrNonCreature()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var rock = new Artifact("Rock", "{1}", null)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(blade);
        blade.SetZone(ZoneType.Battlefield);
        blade.AttachTo(bear);

        var dies = blade.Abilities.OfType<TriggeredAbility>().Single();

        // Any creature dies -> trigger matches (CR 700.4 — even one not
        // equipped, since the printed text is "a creature", not "equipped
        // creature").
        var creatureDies = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);
        dies.IsTriggered(creatureDies).Should().BeTrue(
            "Battlefield -> Graveyard for a creature matches (CR 603.6c)");

        // A non-creature dying -> does not match.
        var rockDies = new CardMovedEvent(rock, ZoneType.Battlefield, ZoneType.Graveyard);
        dies.IsTriggered(rockDies).Should().BeFalse(
            "the trigger fires only on creature deaths");

        // Creature bounced -> not a death.
        var bearBounces = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Hand);
        dies.IsTriggered(bearBounces).Should().BeFalse(
            "Battlefield -> Hand is not a death");
    }

    // -----------------------------------------------------------------------
    // Dies trigger — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Blade_OnCreatureDeath_PutsOneCounter_OnEquippedNonVampire()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(blade);
        blade.SetZone(ZoneType.Battlefield);
        blade.AttachTo(bear);

        var dies = blade.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in dies.Effects) effect.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a single +1/+1 counter lands on the equipped non-Vampire creature");
    }

    [Fact]
    public void Blade_OnCreatureDeath_PutsTwoCounters_OnEquippedVampire()
    {
        var vamp = new Creature("Vampire Nighthawk", "1BB", 2, 3,
            subtypes: new[] { CardSubtype.Vampire })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(blade);
        blade.SetZone(ZoneType.Battlefield);
        blade.AttachTo(vamp);

        var dies = blade.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in dies.Effects) effect.Execute();

        vamp.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "two +1/+1 counters land when the equipped creature is a Vampire");
    }

    [Fact]
    public void Blade_OnCreatureDeath_Unattached_DoesNothing()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var blade = BladeOfTheBloodchiefFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(blade);
        blade.SetZone(ZoneType.Battlefield);
        // intentionally not equipped

        var dies = blade.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in dies.Effects) effect.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no equipped creature -> nothing to receive the counter");
    }
}
