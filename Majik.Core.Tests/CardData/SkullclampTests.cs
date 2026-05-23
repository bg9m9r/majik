using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SkullclampFactory"/> (Darksteel, {1}).
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip activated ability shape: {1} mana cost.
/// - Static effect: equipped 2/2 Bear becomes 3/1.
/// - Dies trigger: equipped creature death draws two cards.
/// - Dies trigger: condition matches Battlefield → Graveyard for the
///   equipped creature only (CR 603.6c / 700.4).
/// </summary>
public class SkullclampTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Skullclamp_Identity()
    {
        var c = SkullclampFactory.Create(_alice);

        c.Name.Should().Be("Skullclamp");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Skullclamp is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Skullclamp_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Skullclamp", _alice);

        c.Should().BeOfType<Artifact>("Skullclamp is an Artifact");
        c.Name.Should().Be("Skullclamp");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "dies trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Equip {1} is wired");
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void Skullclamp_EquipAbility_HasGenericOneCost()
    {
        var c = SkullclampFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(1,
            "Equip {1} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Static continuous effect — +1/-1
    // -----------------------------------------------------------------------

    [Fact]
    public void Skullclamp_Equipped_Bear_Becomes_3_1()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var clamp = SkullclampFactory.Create(_alice, svc, triggers: null);
        clamp.Zone = ZoneType.Battlefield;

        clamp.AttachTo(bear);

        bear.GetPower().Should().Be(3, "+1 power from Skullclamp");
        bear.GetToughness().Should().Be(1, "-1 toughness from Skullclamp");
    }

    [Fact]
    public void Skullclamp_Unattached_DoesNothing()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var clamp = SkullclampFactory.Create(_alice, svc, triggers: null);
        clamp.Zone = ZoneType.Battlefield;
        // intentionally not equipped

        bear.GetPower().Should().Be(2,
            "unequipped Skullclamp's effect gates on AttachedTo");
        bear.GetToughness().Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Dies trigger — draw two cards
    // -----------------------------------------------------------------------

    [Fact]
    public void Skullclamp_EquippedCreatureDies_DrawsTwoCards()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var clamp = SkullclampFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(clamp);
        clamp.SetZone(ZoneType.Battlefield);
        clamp.AttachTo(bear);

        // Library has 3 cards; we expect Skullclamp to draw the first two.
        var top1 = new Creature("Token1", "1G", 1, 1) { Owner = _alice };
        var top2 = new Creature("Token2", "1G", 1, 1) { Owner = _alice };
        var top3 = new Creature("Token3", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(top1);
        _alice.Zones.Library.AddCard(top2);
        _alice.Zones.Library.AddCard(top3);
        top1.SetZone(ZoneType.Library);
        top2.SetZone(ZoneType.Library);
        top3.SetZone(ZoneType.Library);

        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        // Resolve the dies trigger's effect — equipped Bear has died.
        var dies = clamp.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in dies.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "the dies trigger draws two cards");
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "the third card stays on top of the library");
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { top1, top2 });
    }

    [Fact]
    public void Skullclamp_DiesTrigger_MatchesEquippedCreatureOnly()
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
        var clamp = SkullclampFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(clamp);
        clamp.SetZone(ZoneType.Battlefield);
        clamp.AttachTo(bear);

        var dies = clamp.Abilities.OfType<TriggeredAbility>().Single();

        // Equipped Bear dies → trigger matches.
        var bearDies = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);
        dies.IsTriggered(bearDies).Should().BeTrue(
            "Battlefield → Graveyard for the equipped creature matches (CR 603.6c)");

        // A different (unequipped) creature dying → does not match.
        var otherDies = new CardMovedEvent(other, ZoneType.Battlefield, ZoneType.Graveyard);
        dies.IsTriggered(otherDies).Should().BeFalse(
            "the dies trigger only fires for the equipped creature, not any creature");

        // Equipped Bear bounced → not a death.
        var bearBounces = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Hand);
        dies.IsTriggered(bearBounces).Should().BeFalse(
            "Battlefield → Hand is not a death");
    }

    [Fact]
    public void Skullclamp_DiesTrigger_DoesNotFireWhenUnattached()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var clamp = SkullclampFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(clamp);
        clamp.SetZone(ZoneType.Battlefield);
        // intentionally not equipped — Skullclamp has no AttachedTo

        var dies = clamp.Abilities.OfType<TriggeredAbility>().Single();
        var bearDies = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);

        dies.IsTriggered(bearDies).Should().BeFalse(
            "no equipped creature → no death matches the trigger");
    }
}
