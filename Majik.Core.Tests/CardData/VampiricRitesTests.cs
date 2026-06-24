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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="VampiricRitesFactory"/> — Enchantment {B}.
///
/// Oracle text (verified against Scryfall):
///   "{1}{B}, Sacrifice a creature: You gain 1 life and draw a card."
///
/// Village Rites' repeatable cousin: instead of a one-shot Instant that
/// sacrifices a creature as an additional cast cost, Vampiric Rites is a
/// permanent (Enchantment) carrying a repeatable activated ability whose cost
/// is {1}{B} + Sacrifice a creature, and whose resolution gains 1 life and
/// draws ONE card (CR 602 — activated ability).
///
/// Covers:
///   - Identity (Enchantment, {B}, black, owner / controller). Mana cost +
///     type come from the embedded JSON definition.
///   - The single activated ability with TWO costs: a {1}{B}
///     <see cref="ManaCostCost"/> and a sacrifice-a-creature cost
///     (<see cref="SacrificeAnotherCreatureCost"/>).
///   - Resolve: controller gains 1 life and draws one card (CR 119.3 /
///     CR 120).
///   - Cost legality: the sacrifice cost can't be paid with no creature.
/// </summary>
[Trait("Color", "B")]
public class VampiricRitesTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Permanent card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_EnchantmentManaCostBlack()
    {
        var card = VampiricRitesFactory.Create(_alice);

        card.Name.Should().Be("Vampiric Rites");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Black,
            "the {B} pip makes it black");
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HasSingleActivatedAbility_WithManaAndSacrificeCosts()
    {
        var card = VampiricRitesFactory.Create(_alice);

        var ability = card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle()
            .Which;

        // {1}{B} mana component + "Sacrifice a creature" component (CR 602).
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the printed activation cost includes {1}{B}");
        ability.Costs.OfType<SacrificeAnotherCreatureCost>().Should().ContainSingle(
            "the printed activation cost includes 'Sacrifice a creature'");
    }

    [Fact]
    public void ManaComponent_IsOneB()
    {
        var card = VampiricRitesFactory.Create(_alice);

        var manaCost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<ManaCostCost>().Single();

        manaCost.Cost.Should().Be(Majik.Core.ValueObjects.ManaCost.Parse("1B"));
    }

    // -----------------------------------------------------------------------
    // Resolve — gain 1 life + draw one card
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_GainsOneLifeAndDrawsOneCard()
    {
        var top = SeedLibraryCard(_alice, "Top");
        SeedLibraryCard(_alice, "Next");

        var card = VampiricRitesFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var e in ability.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "Vampiric Rites gains its controller 1 life (CR 119.3)");
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top, "it draws exactly one card (CR 120)");
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsSbaLoss()
    {
        // No cards in library — the single draw hits an empty library.
        var card = VampiricRitesFactory.Create(_alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var e in ability.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "life gain still happens");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the draw hit an empty library — SBA loss flag must be set (CR 704.5b)");
    }

    // -----------------------------------------------------------------------
    // Cost legality
    // -----------------------------------------------------------------------

    [Fact]
    public void SacrificeCost_CanPay_WhenControllerHasCreature()
    {
        var card = VampiricRitesFactory.Create(_alice);
        PutOnBattlefield(_alice, card);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, bear);

        var sacCost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<SacrificeAnotherCreatureCost>().Single();

        sacCost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void SacrificeCost_CannotPay_WithNoCreature()
    {
        var card = VampiricRitesFactory.Create(_alice);
        PutOnBattlefield(_alice, card);

        var sacCost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<SacrificeAnotherCreatureCost>().Single();

        sacCost.CanPay(_alice).Should().BeFalse(
            "no creature is controlled — 'Sacrifice a creature' can't be paid (CR 602.1)");
    }
}
