using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ZagothCrystalFactory"/> (Commander Legends).
///
/// Zagoth Crystal — Artifact {3}.
///   "{T}: Add {B}, {G}, or {U}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Card identity (Artifact, mana cost {3}) + NamedCardFactory dispatch.
/// - Three fixed colour mana-ability slots (B/G/U; CR 605.1 — no stack).
/// - {T} gate: a colour slot is inactive while the artifact is tapped.
/// - Activating a slot adds the matching colour and taps the artifact.
/// - Cycling {2} activated ability: cost shape ({2} + Discard self),
///   hand-zone gate (CR 702.32a), end-to-end activation discards self + draws.
/// </summary>
public class ZagothCrystalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ZagothCrystal_IsArtifact_ThreeCost()
    {
        var crystal = ZagothCrystalFactory.Create(_alice);

        crystal.Name.Should().Be("Zagoth Crystal");
        crystal.HasType(CardType.Artifact).Should().BeTrue();
        crystal.ManaCost.Should().Be("{3}");
        crystal.Owner.Should().BeSameAs(_alice);
        crystal.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ZagothCrystal()
    {
        var card = NamedCardFactory.Create("Zagoth Crystal", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Zagoth Crystal");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{3}");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — three fixed colour slots (B/G/U).
    // -----------------------------------------------------------------------

    [Fact]
    public void ZagothCrystal_HasThreeManaAbilities_OnePerColor()
    {
        var crystal = ZagothCrystalFactory.Create(_alice);

        var manaAbilities = crystal.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(3);
        manaAbilities.Should().OnlyContain(ma => ma.ManaGenerated.TotalValue == 1);

        foreach (var pip in new[] { "B", "G", "U" })
        {
            ZagothCrystalFactory.AbilityForColor(crystal, pip)
                .Should().NotBeNull($"Zagoth Crystal can add {{{pip}}}");
        }
    }

    [Fact]
    public void ZagothCrystal_AnyColorActivatable_WhileUntapped()
    {
        var crystal = ZagothCrystalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(crystal);
        crystal.SetZone(ZoneType.Battlefield);

        crystal.Abilities.OfType<ManaAbility>()
            .Should().OnlyContain(ma => ma.CanActivate(),
                "every colour slot is available while Zagoth Crystal is untapped");
    }

    [Fact]
    public void ZagothCrystal_TappedCrystal_CannotActivate()
    {
        var crystal = ZagothCrystalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(crystal);
        crystal.SetZone(ZoneType.Battlefield);
        crystal.Tap();

        crystal.Abilities.OfType<ManaAbility>()
            .Should().OnlyContain(ma => ma.CanActivate() == false,
                "the {T} cost can't be paid while Zagoth Crystal is tapped");
    }

    [Fact]
    public void ZagothCrystal_ActivateBlack_AddsBlackMana_AndTaps()
    {
        var crystal = ZagothCrystalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(crystal);
        crystal.SetZone(ZoneType.Battlefield);

        var black = ZagothCrystalFactory.AbilityForColor(crystal, "B");
        var produced = black.Activate();

        produced.TotalValue.Should().Be(1);
        crystal.IsTapped.Should().BeTrue("activating taps Zagoth Crystal (CR 605.1)");
    }

    // -----------------------------------------------------------------------
    // Cycling {2} — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void ZagothCrystal_HasCyclingActivatedAbility_WithManaAndDiscardSelf()
    {
        var crystal = ZagothCrystalFactory.Create(_alice);

        var cycling = crystal.Abilities.OfType<ActivatedAbility>()
            .Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling has {2} + Discard self");
        cycling.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();
        cycling.TargetRequests.Should().BeEmpty("cycling draws a card — no targets");

        crystal.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Cycling");
    }

    /// <summary>
    /// CR 702.32a — cycling activates only from the controller's hand.
    /// </summary>
    [Fact]
    public void ZagothCrystal_DiscardSelfCost_CannotPay_FromBattlefield()
    {
        var crystal = ZagothCrystalFactory.Create(_alice);
        crystal.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(crystal);

        var cycling = crystal.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.32a — cycling can only be activated from hand");
    }

    [Fact]
    public void ZagothCrystal_DiscardSelfCost_CanPay_FromHand()
    {
        var crystal = ZagothCrystalFactory.Create(_alice);
        crystal.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(crystal);

        var cycling = crystal.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeTrue();
    }

    /// <summary>
    /// End-to-end: paying both costs moves Zagoth Crystal hand → graveyard,
    /// and the effect closure draws one card (CR 702.32a).
    /// </summary>
    [Fact]
    public void ZagothCrystal_Cycling_EndToEnd_DiscardsSelfDrawsOne()
    {
        var topCard = new Instant("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var crystal = ZagothCrystalFactory.Create(_alice);
        crystal.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(crystal);

        var cycling = crystal.Abilities.OfType<ActivatedAbility>().Single();

        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();
        discardCost.CanPay(_alice).Should().BeTrue();
        discardCost.Pay(_alice);

        crystal.Zone.Should().Be(ZoneType.Graveyard, "discarded self");
        _alice.Zones.Graveyard.GetCards().Should().Contain(crystal);

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "Zagoth Crystal's cycling effect draws one card");
        topCard.Zone.Should().Be(ZoneType.Hand);
    }
}
