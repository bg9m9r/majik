using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SimianSpiritGuideFactory"/>.
///
/// Simian Spirit Guide — Creature — Ape Spirit 2/2 {2}{R}.
///   "Exile this card from your hand: Add {R}."
///
/// Covers:
/// - Card identity (Creature — Ape Spirit, 2/2, {2}{R}).
/// - NamedCardFactory dispatch.
/// - One mana ability producing {R}.
/// - The ability is a no-tap mana ability (CR 605.1) gated on the card
///   being in its controller's hand (CR 602.5 — "Exile this card from
///   your hand" is an activation-zone restriction).
/// - Activation produces {R} AND moves the card from hand to exile
///   (the exile is the activation cost, paid inline). The card is NOT
///   tapped (it never touches the battlefield).
/// - Once exiled the ability is no longer activatable (no longer in hand).
/// </summary>
public class SimianSpiritGuideTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void SimianSpiritGuide_IsApeSpirit_TwoTwo_TwoR()
    {
        var guide = SimianSpiritGuideFactory.Create(_alice);

        guide.Name.Should().Be("Simian Spirit Guide");
        guide.HasType(CardType.Creature).Should().BeTrue();
        guide.HasSubtype(CardSubtype.Ape).Should().BeTrue();
        guide.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        guide.Power.Should().Be(2);
        guide.Toughness.Should().Be(2);
        guide.ManaCost.Should().Be("{2}{R}");
        guide.Owner.Should().BeSameAs(_alice);
        guide.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SimianSpiritGuide()
    {
        var card = NamedCardFactory.Create("Simian Spirit Guide", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Simian Spirit Guide");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
    }

    // --------------------------------------------------------------
    // Mana ability shape — one red mana ability
    // --------------------------------------------------------------

    [Fact]
    public void SimianSpiritGuide_HasOneRedManaAbility()
    {
        var guide = SimianSpiritGuideFactory.Create(_alice);
        var mas = guide.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().ContainSingle("Simian Spirit Guide has exactly one mana ability");
        mas[0].ManaGenerated.Red.Should().Be(1);
        mas[0].ManaGenerated.TotalValue.Should().Be(1);
    }

    // --------------------------------------------------------------
    // Activation gate — only legal while in hand
    // --------------------------------------------------------------

    [Fact]
    public void SimianSpiritGuide_NotActivatable_WhileNotInHand()
    {
        var guide = SimianSpiritGuideFactory.Create(_alice);
        // Default zone is not Hand — the ability can't be activated yet.
        var ma = guide.Abilities.OfType<ManaAbility>().Single();

        ma.CanActivate().Should().BeFalse(
            "the ability is 'Exile this card from your hand' — only legal while in hand");
    }

    [Fact]
    public void SimianSpiritGuide_Activatable_WhileInHand()
    {
        var guide = SimianSpiritGuideFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(guide);
        guide.SetZone(ZoneType.Hand);

        var ma = guide.Abilities.OfType<ManaAbility>().Single();
        ma.CanActivate().Should().BeTrue("the card is in its controller's hand");
    }

    // --------------------------------------------------------------
    // Activation — produces {R}, exiles the card from hand, no tap
    // --------------------------------------------------------------

    [Fact]
    public void SimianSpiritGuide_Activate_ProducesRed_AndExilesFromHand()
    {
        var guide = SimianSpiritGuideFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(guide);
        guide.SetZone(ZoneType.Hand);

        var ma = guide.Abilities.OfType<ManaAbility>().Single();
        var produced = ma.Activate();

        produced.Red.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        // The card is exiled (cost), not tapped — it never enters play.
        guide.IsTapped.Should().BeFalse(
            "Simian Spirit Guide is exiled from hand, never tapped on the battlefield");
        guide.Zone.Should().Be(ZoneType.Exile,
            "the exile cost moves the card from hand to its owner's exile zone");
        _alice.Zones.Hand.GetCards().Should().NotContain(guide,
            "the card has left the hand");
        _alice.Zones.Exile.GetCards().Should().Contain(guide,
            "the card is now in its owner's exile zone");

        // No further activations — the card is no longer in hand.
        ma.CanActivate().Should().BeFalse(
            "the card has been exiled — no further activations possible");
    }

    // --------------------------------------------------------------
    // ManaAbilityActivator path — pool gets credited with red
    // --------------------------------------------------------------

    [Fact]
    public void SimianSpiritGuide_ActivateViaActivator_CreditsRedMana()
    {
        var guide = SimianSpiritGuideFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(guide);
        guide.SetZone(ZoneType.Hand);

        var activator = new Majik.Core.Services.ManaAbilityActivator();
        var ability = guide.Abilities.OfType<ManaAbility>().Single();

        _alice.ManaPool.Total.Should().Be(0);

        activator.ActivateManaAbility(ability, _alice);

        _alice.ManaPool.Red.Should().Be(1);
        _alice.ManaPool.Total.Should().Be(1);
        guide.Zone.Should().Be(ZoneType.Exile);
    }
}
