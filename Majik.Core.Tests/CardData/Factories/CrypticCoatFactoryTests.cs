using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Cryptic Coat (Murders at Karlov Manor, {2}{U}) — the Cloak
/// cluster's flagship, unblocked by the cloak-keyword-subsystem
/// (CR 702.168) shipped in this PR.
///
/// Oracle text (Scryfall, verified 2026-06-02):
///   "When this Equipment enters, cloak the top card of your library, then
///    attach this Equipment to it."
///   "Equipped creature gets +1/+0 and can't be blocked."
///   "{1}{U}: Return this Equipment to its owner's hand."
/// </summary>
public class CrypticCoatFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Create_ShapeOnly_IsArtifactEquipment_WithReturnAbility()
    {
        var coat = CrypticCoatFactory.Create(_alice);

        coat.Should().BeOfType<Artifact>();
        coat.Name.Should().Be("Cryptic Coat");
        coat.ManaCost.Should().Be("{2}{U}");
        coat.HasSubtype(CardSubtype.Equipment).Should().BeTrue();

        // "{1}{U}: Return this Equipment to its owner's hand."
        coat.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not EquipActivatedAbility)
            .Should().ContainSingle("the return-to-hand activated ability is always present");
    }

    [Fact]
    public void Etb_CloaksTopCard_ThenAttachesCoatToIt()
    {
        var svc = new ContinuousEffectsService();
        var top = new Creature("Hidden Threat", "{3}{B}", 5, 5);
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);

        var coat = CrypticCoatFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(coat);
        coat.SetZone(ZoneType.Battlefield);

        // Run the ETB trigger's effect body directly.
        var etb = coat.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        // A face-down 2/2 cloaked creature is now on the battlefield...
        var cloaked = _alice.Zones.Battlefield.GetCards()
            .OfType<ManifestedCreature>().Single();
        cloaked.IsFaceDown.Should().BeTrue();
        cloaked.UnderlyingCard.Should().BeSameAs(top);
        cloaked.Power.Should().Be(2, "CR 708.2 — cloaked creatures are 2/2");

        // ...with ward {2} (CR 702.168a)...
        cloaked.EffectiveAbilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Ward" && k.Arg == 2);

        // ...and the Coat is attached to it (CR 301.5).
        coat.AttachedTo.Should().BeSameAs(cloaked);
        cloaked.Attachments.Should().Contain(coat);
    }

    [Fact]
    public void Etb_EmptyLibrary_IsCleanNoOp_NothingAttached()
    {
        var svc = new ContinuousEffectsService();
        var coat = CrypticCoatFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(coat);
        coat.SetZone(ZoneType.Battlefield);

        var etb = coat.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        coat.AttachedTo.Should().BeNull("empty library → nothing cloaked, nothing to attach");
        _alice.Zones.Battlefield.GetCards().OfType<ManifestedCreature>().Should().BeEmpty();
    }

    [Fact]
    public void EquippedCreature_GetsPlusOneZero_AndCantBeBlocked()
    {
        var svc = new ContinuousEffectsService();
        // A plain face-up creature standing in for "equipped creature".
        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice, Controller = _alice,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        var coat = CrypticCoatFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(coat);
        coat.SetZone(ZoneType.Battlefield);
        coat.AttachTo(bear);

        // "+1/+0" (CR 613 Layer 7c).
        bear.Power.Should().Be(3);
        bear.Toughness.Should().Be(2);

        // "can't be blocked" (CR 509.1b).
        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeTrue();
    }

    [Fact]
    public void UnequippedElsewhere_BoostAndUnblockable_DoNotApply()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice, Controller = _alice,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        var coat = CrypticCoatFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(coat);
        coat.SetZone(ZoneType.Battlefield);
        // NOT attached to bear.

        bear.Power.Should().Be(2, "an unattached Coat grants nothing");
        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeFalse();
    }

    [Fact]
    public void ReturnAbility_BouncesCoatToOwnersHand_AndDetaches()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "{1}{G}", 2, 2)
        {
            Owner = _alice, Controller = _alice,
            Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        var coat = CrypticCoatFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(coat);
        coat.SetZone(ZoneType.Battlefield);
        coat.AttachTo(bear);

        // The non-equip activated ability is the return-to-hand ability.
        var returnAbility = coat.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not EquipActivatedAbility);
        foreach (var e in returnAbility.Effects) e.Execute();

        coat.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(coat);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(coat);
        coat.AttachedTo.Should().BeNull("returning to hand detaches the Equipment");
        // Grants stop applying once the Coat leaves the battlefield.
        bear.Power.Should().Be(2);
        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeFalse();
    }
}
