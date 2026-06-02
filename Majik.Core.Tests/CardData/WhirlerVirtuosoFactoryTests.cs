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
/// Unit tests for <see cref="WhirlerVirtuosoFactory"/> (Kaladesh).
///
/// Oracle:
///   "When Whirler Virtuoso enters, you get {E}{E}{E} (three energy
///    counters). Pay {E}{E}: Create a 1/1 colorless Thopter artifact
///    creature token with flying."
///
/// Covers:
/// - Identity ({2}{U}{R}, 2/3, Human Artificer — a plain Creature, NOT an artifact).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger grants the controller {E}{E}{E} on resolution.
/// - {E}{E} activated ability shape (PayEnergyCost(2), no targets).
/// - Activation mints a 1/1 colourless Thopter token with Flying +
///   Artifact + Creature types.
/// - <see cref="PayEnergyCost"/> can't pay with insufficient energy.
/// </summary>
public class WhirlerVirtuosoFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WhirlerVirtuoso_Identity_HumanArtificer()
    {
        var card = WhirlerVirtuosoFactory.Create(_alice);

        card.Name.Should().Be("Whirler Virtuoso");
        card.ManaCost.ToString().Should().Be("{2}{U}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeFalse(
            "Whirler Virtuoso is a plain Creature — Human Artificer, not an Artifact Creature");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WhirlerVirtuoso_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Whirler Virtuoso", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Whirler Virtuoso");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ETB trigger — gain {E}{E}{E}
    // -----------------------------------------------------------------------

    [Fact]
    public void WhirlerVirtuoso_HasExactlyOneEtbTrigger()
    {
        var card = WhirlerVirtuosoFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"you get {E}{E}{E}\" trigger");
    }

    [Fact]
    public void WhirlerVirtuoso_EtbEffect_GrantsControllerThreeEnergy()
    {
        var alice = new Player("Alice", 20);
        var card = WhirlerVirtuosoFactory.Create(alice);
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();

        alice.EnergyCounters.Should().Be(0);

        foreach (var effect in etb.Effects) effect.Execute();

        alice.EnergyCounters.Should().Be(3,
            "ETB grants the controller three energy (CR 106.13b)");
    }

    // -----------------------------------------------------------------------
    // Activated ability — Pay {E}{E}: create Thopter
    // -----------------------------------------------------------------------

    [Fact]
    public void WhirlerVirtuoso_HasExactlyOneActivatedAbility()
    {
        var card = WhirlerVirtuosoFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {E}{E}: create-Thopter activation");
    }

    [Fact]
    public void WhirlerVirtuoso_ActivatedAbility_HasPayEnergyCostOfTwo()
    {
        var card = WhirlerVirtuosoFactory.Create(_alice);
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();

        var cost = activated.Costs.OfType<PayEnergyCost>().Single();
        cost.Amount.Should().Be(2,
            "Pay {E}{E} → PayEnergyCost(2) — sibling of Guide of Souls' " +
            "pump activation");
    }

    [Fact]
    public void WhirlerVirtuoso_PayEnergyCost_CannotPayWithOneEnergy()
    {
        var alice = new Player("Alice", 20);
        alice.GainEnergy(1);
        var card = WhirlerVirtuosoFactory.Create(alice);
        var cost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<PayEnergyCost>().Single();

        cost.CanPay(alice).Should().BeFalse(
            "CR 119.4 — can't pay {E}{E} with only one energy");
    }

    [Fact]
    public void WhirlerVirtuoso_PayEnergyCost_CanPayWithTwoEnergy()
    {
        var alice = new Player("Alice", 20);
        alice.GainEnergy(2);
        var card = WhirlerVirtuosoFactory.Create(alice);
        var cost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<PayEnergyCost>().Single();

        cost.CanPay(alice).Should().BeTrue("2 energy satisfies the {E}{E} cost");
    }

    [Fact]
    public void WhirlerVirtuoso_Activation_MintsFlyingThopterToken()
    {
        var alice = new Player("Alice", 20);
        var card = WhirlerVirtuosoFactory.Create(alice);
        // Put Whirler on the battlefield so the activation's source-zone
        // check (CR 603.6c) passes.
        card.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(card);

        var activated = card.Abilities.OfType<ActivatedAbility>().Single();

        // Skip cost payment — assert the effect's body produces the token.
        foreach (var effect in activated.Effects) effect.Execute();

        var thopter = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.Name == "Thopter");

        thopter.IsToken.Should().BeTrue("CR 111.1 — minted as a token");
        thopter.BasePower.Should().Be(1);
        thopter.BaseToughness.Should().Be(1);
        thopter.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        thopter.HasType(CardType.Creature).Should().BeTrue();
        thopter.HasType(CardType.Artifact).Should().BeTrue(
            "Thopter token is an Artifact Creature (CR 111.1)");

        // CR 702.9 — Flying keyword wired via KeywordAbility marker
        thopter.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "the printed Thopter token has flying");
    }

    [Fact]
    public void WhirlerVirtuoso_Activation_NoOpWhenNotOnBattlefield()
    {
        // CR 603.6c — leaves-the-battlefield exception: Whirler's
        // activated ability requires the source on the battlefield to
        // mint the token. v1 closure short-circuits when zone != BF.
        var alice = new Player("Alice", 20);
        var card = WhirlerVirtuosoFactory.Create(alice);
        // Card not on battlefield (still in hand sentinel).
        card.SetZone(ZoneType.Hand);

        var activated = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in activated.Effects) effect.Execute();

        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Thopter")
            .Should().BeEmpty("no token when Whirler isn't on the battlefield");
    }
}
