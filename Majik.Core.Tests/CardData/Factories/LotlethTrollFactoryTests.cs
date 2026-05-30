using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Lotleth Troll (Return to Ravnica, {B}{G}, Creature — Zombie
/// Troll 2/1).
///
/// Oracle text (Scryfall, verified):
///   "Trample
///    Discard a creature card: Put a +1/+1 counter on this creature.
///    {B}: Regenerate this creature."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trample keyword marker (CR 702.19).
///   - Discard-pump activated ability shape: sole cost is a
///     <see cref="DiscardACreatureCardCost"/> (no mana).
///   - Discard-pump cost gate: payable only with a creature card in hand;
///     a non-creature card in hand does NOT satisfy it.
///   - Discard-pump end-to-end: pays the cost (a creature card leaves hand
///     for the graveyard) and places a +1/+1 counter on Lotleth Troll
///     (CR 614).
///   - Regenerate activated ability shape: sole cost is {B}; resolution adds
///     a regeneration shield (CR 701.18 / CR 701.15a).
/// </summary>
public class LotlethTrollFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakeCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    // ------------------------------------------------------------------
    // Identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void LotlethTroll_Identity_ZombieTroll21()
    {
        var c = LotlethTrollFactory.Create(_alice);

        c.Name.Should().Be("Lotleth Troll");
        c.ManaCost.Should().Be("{B}{G}");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Troll).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LotlethTroll()
    {
        var card = NamedCardFactory.Create("Lotleth Troll", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Lotleth Troll");
        card.HasSubtype(CardSubtype.Troll).Should().BeTrue();
    }

    [Fact]
    public void LotlethTroll_HasTrampleKeyword()
    {
        var c = LotlethTrollFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Trample", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Lotleth Troll has Trample (CR 702.19)");
    }

    [Fact]
    public void LotlethTroll_HasExactlyTwoActivatedAbilities()
    {
        var c = LotlethTrollFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "discard-pump and {B}-regenerate are the two activated abilities");
    }

    // ------------------------------------------------------------------
    // Discard a creature card: +1/+1 counter
    // ------------------------------------------------------------------

    [Fact]
    public void DiscardPump_Cost_IsDiscardACreatureCardNoMana()
    {
        var c = LotlethTrollFactory.Create(_alice);

        var pump = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<DiscardACreatureCardCost>().Any());

        pump.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the discard-pump ability has no mana cost — discard is the sole cost");
        pump.Costs.OfType<DiscardACreatureCardCost>().Should().ContainSingle();
    }

    [Fact]
    public void DiscardPump_CanPay_WithCreatureCardInHand()
    {
        var c = LotlethTrollFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(MakeCreature(_alice));

        var cost = c.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs).OfType<DiscardACreatureCardCost>().Single();

        cost.CanPay(_alice).Should().BeTrue(
            "a creature card in hand pays 'discard a creature card'");
    }

    [Fact]
    public void DiscardPump_CannotPay_WithNoCreatureCardInHand()
    {
        var c = LotlethTrollFactory.Create(_alice);
        // A non-creature card in hand must NOT satisfy the cost.
        var land = new Land("Forest");
        land.SetOwner(_alice);
        land.SetController(_alice);
        _alice.Zones.Hand.AddCard(land);

        var cost = c.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs).OfType<DiscardACreatureCardCost>().Single();

        cost.CanPay(_alice).Should().BeFalse(
            "only a creature card can pay 'discard a creature card'");
    }

    [Fact]
    public void DiscardPump_PayThenResolve_DiscardsCreatureAndAddsCounter()
    {
        var c = LotlethTrollFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var fodder = MakeCreature(_alice, "Fodder");
        _alice.Zones.Hand.AddCard(fodder);

        var pump = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<DiscardACreatureCardCost>().Any());
        var cost = pump.Costs.OfType<DiscardACreatureCardCost>().Single();

        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice);
        foreach (var effect in pump.Effects) effect.Execute();

        _alice.Zones.Hand.ContainsCard(fodder).Should().BeFalse(
            "the discarded creature card leaves the hand");
        _alice.Zones.Graveyard.ContainsCard(fodder).Should().BeTrue(
            "the discarded creature card goes to the graveyard (CR 701.16a)");
        c.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the discard-pump places a +1/+1 counter on Lotleth Troll");
    }

    // ------------------------------------------------------------------
    // {B}: Regenerate this creature
    // ------------------------------------------------------------------

    [Fact]
    public void Regenerate_Cost_IsBManaOnly()
    {
        var c = LotlethTrollFactory.Create(_alice);

        var regen = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

        regen.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "regenerate's only cost is {B}");
        regen.Costs.OfType<DiscardACreatureCardCost>().Should().BeEmpty(
            "regenerate does not discard");
    }

    [Fact]
    public void Regenerate_OnResolve_AddsRegenerationShield()
    {
        var c = LotlethTrollFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var regen = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

        c.HasRegenerationShield.Should().BeFalse("no shield before activation");
        foreach (var effect in regen.Effects) effect.Execute();

        c.HasRegenerationShield.Should().BeTrue(
            "regenerate creates a regeneration shield (CR 701.18 / CR 701.15a)");
    }
}
