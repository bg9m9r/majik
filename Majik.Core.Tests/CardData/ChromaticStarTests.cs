using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ChromaticStarFactory"/>.
///
/// Chromatic Star — Artifact {1}.
///   "{T}, Sacrifice Chromatic Star: Add one mana of any color.
///    When Chromatic Star is put into a graveyard from the battlefield,
///    draw a card."
///
/// Covers:
/// - Identity (Artifact, {1}) + NamedCardFactory dispatch.
/// - Five mana abilities (one per WUBRG) — same shape as Lotus Petal.
/// - Activating one of the colour abilities taps the star, sacrifices it,
///   and credits one mana of the chosen colour.
/// - Sibling mana abilities un-activatable once sacrificed.
/// - LTB trigger: <see cref="Triggers.OnDies"/> condition fires on
///   Battlefield → Graveyard CardMovedEvent for the source.
/// - LTB trigger effect draws one card on resolve.
/// </summary>
public class ChromaticStarTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void ChromaticStar_IsArtifact_OneCost()
    {
        var star = ChromaticStarFactory.Create(_alice);

        star.Name.Should().Be("Chromatic Star");
        star.HasType(CardType.Artifact).Should().BeTrue();
        star.ManaCost.Should().Be("{1}");
        star.Owner.Should().BeSameAs(_alice);
        star.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ChromaticStar()
    {
        var card = NamedCardFactory.Create("Chromatic Star", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Chromatic Star");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    // --------------------------------------------------------------
    // Ability shape — 5 mana abilities + 1 LTB trigger
    // --------------------------------------------------------------

    [Fact]
    public void ChromaticStar_HasFiveManaAbilities_OnePerColor()
    {
        var star = ChromaticStarFactory.Create(_alice);
        var mas = star.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1
                                     && m.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void ChromaticStar_HasOneTriggeredAbility_ForLTB()
    {
        var star = ChromaticStarFactory.Create(_alice);
        star.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // --------------------------------------------------------------
    // Mana ability activation — tap, produce, sacrifice
    // --------------------------------------------------------------

    [Fact]
    public void ChromaticStar_Activate_ProducesChosenColor_AndSacrificesStar()
    {
        var star = ChromaticStarFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(star);
        star.SetZone(ZoneType.Battlefield);

        var mas = star.Abilities.OfType<ManaAbility>().ToList();
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeTrue(
                "star is untapped and on the battlefield");
        }

        // Activate the green option.
        var green = mas.Single(m => m.ManaGenerated.Green == 1);
        var produced = green.Activate();

        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        star.IsTapped.Should().BeTrue("activation taps the star");
        star.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.16 — sacrifice moves the star from battlefield to owner's graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(star);
        _alice.Zones.Graveyard.GetCards().Should().Contain(star);

        // Sibling colour abilities are now un-activatable.
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse(
                "star has been sacrificed — no further activations possible");
        }
    }

    // --------------------------------------------------------------
    // LTB trigger — Battlefield → Graveyard for the source
    // --------------------------------------------------------------

    [Fact]
    public void ChromaticStar_DiesTrigger_ConditionMatchesBattlefieldToGraveyard()
    {
        var star = ChromaticStarFactory.Create(_alice);
        star.SetZone(ZoneType.Battlefield);

        var ltb = star.Abilities.OfType<TriggeredAbility>().Single();

        var dies = new CardMovedEvent(star, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.IsTriggered(dies).Should().BeTrue(
            "Battlefield → Graveyard for the source matches the LTB condition");

        var bounce = new CardMovedEvent(star, ZoneType.Battlefield, ZoneType.Hand);
        ltb.IsTriggered(bounce).Should().BeFalse(
            "Battlefield → Hand is a bounce, not LTB-to-graveyard");

        var exile = new CardMovedEvent(star, ZoneType.Battlefield, ZoneType.Exile);
        ltb.IsTriggered(exile).Should().BeFalse(
            "Battlefield → Exile bypasses the graveyard step entirely");
    }

    [Fact]
    public void ChromaticStar_LTB_Resolve_DrawsACard()
    {
        // Set up a card to draw on top of the library.
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var star = ChromaticStarFactory.Create(_alice);
        // Star is conceptually in the graveyard at resolve time (activeZones
        // = {Graveyard} keeps the trigger attached across the LTB hop).
        _alice.Zones.Graveyard.AddCard(star);
        star.SetZone(ZoneType.Graveyard);

        var ltb = star.Abilities.OfType<TriggeredAbility>().Single();
        ltb.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top, "LTB cantrip drew one card");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }
}
