using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KorOutfitterFactory"/> — Creature — Kor Soldier {W}{W}
/// 2/2 (Zendikar). Oracle:
///   "When this creature enters, you may attach target Equipment you control
///    to target creature you control."
///
/// Covers:
///   - Card identity (Creature + Kor/Soldier, {W}{W}, 2/2, owner/controller).
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated/mana abilities.
///   - ETB resolve: attaches a controlled Equipment to a controlled creature
///     (CR 603.6a / 701.3a).
///   - ETB resolve: no controlled Equipment → no attach.
///   - ETB resolve: no controlled creature → no attach.
/// </summary>
[Trait("Color", "W")]
public class KorOutfitterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Artifact MakeEquipment(string name, Player owner)
    {
        var eq = new Artifact(name, "{1}", subtypes: new[] { CardSubtype.Equipment });
        eq.SetOwner(owner);
        eq.SetController(owner);
        return eq;
    }

    private static Creature MakeCreature(string name, Player owner)
    {
        var c = new Creature(name, "{1}", power: 1, toughness: 1);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    [Fact]
    public void KorOutfitter_IsKorSoldier_AtWW_TwoTwo()
    {
        var c = KorOutfitterFactory.Create(_alice);

        c.Name.Should().Be("Kor Outfitter");
        c.ManaCost.Should().Be("{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Kor).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KorOutfitter_HasOneEtbTrigger_NoActivatedOrManaAbilities()
    {
        var c = KorOutfitterFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Etb_Attaches_ControlledEquipment_ToControlledCreature()
    {
        var equipment = MakeEquipment("Bonesplitter", _alice);
        _alice.Zones.Battlefield.AddCard(equipment);
        equipment.SetZone(ZoneType.Battlefield);

        var bear = MakeCreature("Grizzly Bears", _alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var outfitter = KorOutfitterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(outfitter);
        outfitter.SetZone(ZoneType.Battlefield);

        var etb = outfitter.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        equipment.AttachedTo.Should().NotBeNull("the Equipment should be attached on resolution");
        bear.Attachments.Should().Contain(equipment,
            "Kor Outfitter attaches a controlled Equipment to a controlled creature");
    }

    [Fact]
    public void Etb_NoControlledEquipment_DoesNotAttach()
    {
        var bear = MakeCreature("Grizzly Bears", _alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var outfitter = KorOutfitterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(outfitter);
        outfitter.SetZone(ZoneType.Battlefield);

        var etb = outfitter.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        bear.Attachments.Should().BeEmpty("no Equipment to attach → no-op (CR 603.7c)");
    }

    [Fact]
    public void Etb_NoControlledCreature_DoesNotAttach()
    {
        var equipment = MakeEquipment("Bonesplitter", _alice);
        _alice.Zones.Battlefield.AddCard(equipment);
        equipment.SetZone(ZoneType.Battlefield);

        // Kor Outfitter itself is a creature; resolve BEFORE it enters so no
        // controlled creature exists on the battlefield at resolution time.
        var outfitter = KorOutfitterFactory.Create(_alice);

        var etb = outfitter.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        equipment.AttachedTo.Should().BeNull("no creature to attach to → no-op (CR 603.7c)");
    }
}
