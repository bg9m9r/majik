using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BloomTenderFactory"/> — Bloom Tender, the {1}{G}
/// Elf Druid mana dork ("Vivid — {T}: For each color among permanents you
/// control, add one mana of that color.").
///
/// Analogue of <see cref="ElvishArchdruidFactory"/>'s dynamic {T} mana
/// ability, but the dynamic quantity is the set of DISTINCT colours among
/// controlled permanents (one mana per colour) rather than a count of a
/// subtype.
///
/// Covers ONLY the card's unique behaviour:
/// - Identity (1/1 Creature — Elf Druid, {1}{G}) — single combined assert.
/// - The Vivid {T} mana ability: one mana per distinct colour among
///   permanents the controller controls, evaluated at activation, including
///   Bloom Tender itself, ignoring colourless permanents and opponents'.
/// </summary>
[Trait("Color", "G")]
public class BloomTenderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature Make(Player owner) =>
        (Creature)NamedCardFactory.Create("Bloom Tender", owner);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BloomTender_Identity()
    {
        var card = Make(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bloom Tender");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        card.Power.Should().Be(1);
        card.Toughness.Should().Be(1);

        var cost = card.ManaCostValue;
        cost.Generic.Should().Be(1);
        cost.Green.Should().Be(1);
        cost.TotalValue.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Vivid mana ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BloomTender_HasExactlyOneManaAbility_AndNoActivatedTriggered()
    {
        var card = Make(_alice);

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the only ability is the Vivid {T} mana ability");
        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        card.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Vivid mana ability — colour-set output
    // -----------------------------------------------------------------------

    [Fact]
    public void BloomTender_Alone_TapsForGreen_BecauseItIsAGreenPermanentYouControl()
    {
        var card = Make(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.ClearSummoningSickness(); // can tap for mana this turn (CR 302.6)

        var mana = card.Abilities.OfType<ManaAbility>().Single();
        var produced = mana.Activate();

        // Bloom Tender is itself a green permanent the controller controls.
        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1, "only one distinct colour is present (green)");
        card.IsTapped.Should().BeTrue("activation taps Bloom Tender");
    }

    [Fact]
    public void BloomTender_OneManaPerDistinctColor_AmongControlledPermanents()
    {
        var card = Make(_alice);
        _alice.Zones.Battlefield.AddCard(card); // green
        card.ClearSummoningSickness();

        // Add a white-and-black gold permanent. Distinct colours present:
        // G (Bloom Tender) + W + B = three; one mana each.
        var goldCreature = (Creature)NamedCardFactory.Create("Corpse Knight", _alice);
        _alice.Zones.Battlefield.AddCard(goldCreature);

        var mana = card.Abilities.OfType<ManaAbility>().Single();
        var produced = mana.Activate();

        // Corpse Knight is {W}{B} — White, Black. Plus Bloom Tender's Green
        // = three distinct colours, one mana each.
        produced.White.Should().Be(1);
        produced.Black.Should().Be(1);
        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(3,
            "one mana per DISTINCT colour among controlled permanents");
    }

    [Fact]
    public void BloomTender_ColorlessPermanents_ContributeNoMana()
    {
        var card = Make(_alice);
        _alice.Zones.Battlefield.AddCard(card); // green
        card.ClearSummoningSickness();

        // A colourless artifact mana rock contributes no colour.
        var rock = (Artifact)NamedCardFactory.Create("Gilded Lotus", _alice);
        _alice.Zones.Battlefield.AddCard(rock);

        var mana = card.Abilities.OfType<ManaAbility>().Single();
        var produced = mana.Activate();

        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1,
            "the colourless Gilded Lotus adds no colour to the Vivid set");
    }

    [Fact]
    public void BloomTender_IgnoresOpponentsPermanents()
    {
        var bob = new Player("Bob", 20);
        var card = Make(_alice);
        _alice.Zones.Battlefield.AddCard(card); // green, Alice controls
        card.ClearSummoningSickness();

        // A red permanent Bob controls must NOT contribute (CR 109.5).
        var bobsRed = (Creature)NamedCardFactory.Create("Atog", bob);
        bob.Zones.Battlefield.AddCard(bobsRed);

        var mana = card.Abilities.OfType<ManaAbility>().Single();
        var produced = mana.Activate();

        produced.Red.Should().Be(0, "opponents' permanents don't count for \"you control\"");
        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Activation legality
    // -----------------------------------------------------------------------

    [Fact]
    public void BloomTender_CannotActivateWhenTapped()
    {
        var card = Make(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.ClearSummoningSickness();
        card.Tap();

        card.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeFalse("the {T} cost cannot be paid by a tapped permanent");
    }

    [Fact]
    public void BloomTender_Create_ThrowsOnNullOwner()
    {
        var act = () => (Creature)NamedCardFactory.Create("Bloom Tender", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
