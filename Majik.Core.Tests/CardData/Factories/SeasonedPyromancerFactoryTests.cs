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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SeasonedPyromancerFactory"/>
/// (Modern Horizons, {1}{R}{R}).
///
/// Covers:
/// - Identity (name, cost {1}{R}{R}, 2/2, Creature — Human Shaman, MV 3).
/// - NamedCardFactory dispatch.
/// - ETB trigger wired: discard two, draw two, create one 1/1 red Elemental
///   token per NONLAND discarded.
///   - 2 nonland discards → 2 tokens.
///   - 1 land + 1 nonland → 1 token.
/// - Graveyard-activated ability exists (card has an ActivatedAbility).
/// - Graveyard-activated ability creates two 1/1 red Elemental tokens.
/// - Graveyard-activated ability is a no-op when card is not in graveyard.
/// </summary>
[Trait("Color", "R")]
public class SeasonedPyromancerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature MakeNonlandCard(Player owner, string name = "Bolt")
    {
        var c = new Creature(name, "R", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static Card MakeLandCard(Player owner, string name = "Forest")
    {
        var land = new Land(name);
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SeasonedPyromancer_Identity_HumanShaman_2_2_At_1RR()
    {
        var sp = SeasonedPyromancerFactory.Create(_alice);

        sp.Name.Should().Be("Seasoned Pyromancer");
        sp.ManaCost.Should().Be("{1}{R}{R}");
        sp.HasType(CardType.Creature).Should().BeTrue();
        sp.HasSubtype(CardSubtype.Human).Should().BeTrue("Seasoned Pyromancer is a Human");
        sp.HasSubtype(CardSubtype.Shaman).Should().BeTrue("Seasoned Pyromancer is a Shaman");
        sp.BasePower.Should().Be(2);
        sp.BaseToughness.Should().Be(2);
        sp.Owner.Should().BeSameAs(_alice);
        sp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SeasonedPyromancer_ManaValue_Is_3()
    {
        var sp = SeasonedPyromancerFactory.Create(_alice);
        sp.ManaCostValue.TotalValue.Should().Be(3, "mana cost {1}{R}{R} = 1+1+1 = 3");
    }
    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SeasonedPyromancer_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var sp = SeasonedPyromancerFactory.Create(_alice);

        var triggers = sp.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB trigger");

        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — 2 nonland discards → 2 Elemental tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void ETB_TwoNonlandDiscards_CreatesTwoElementalTokens()
    {
        var alice = new Player("Alice", 20);
        var sp = SeasonedPyromancerFactory.Create(alice);

        // Give Alice two nonland cards in hand.
        var hand1 = MakeNonlandCard(alice, "Card1");
        var hand2 = MakeNonlandCard(alice, "Card2");

        alice.Zones.Hand.AddCard(hand1);
        hand1.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(hand2);
        hand2.SetZone(ZoneType.Hand);

        // Give Alice two library cards to draw.
        var lib1 = MakeNonlandCard(alice, "Lib1");
        var lib2 = MakeNonlandCard(alice, "Lib2");
        alice.Zones.Library.AddCard(lib1);
        lib1.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(lib2);
        lib2.SetZone(ZoneType.Library);

        // Execute ETB effects directly.
        var etb = sp.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // Discarded 2, drew 2 → net 2 cards in hand.
        alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "discarded 2 nonlands, drew 2 → net 2 cards in hand");

        // Two nonlands discarded → 2 Elemental tokens on battlefield.
        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Elemental" && c.BasePower == 1 && c.BaseToughness == 1)
            .ToList();
        tokens.Should().HaveCount(2, "each nonland discarded creates a 1/1 Elemental token");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — 1 land + 1 nonland discarded → 1 Elemental token
    // -----------------------------------------------------------------------

    [Fact]
    public void ETB_OneLandOneNonland_CreatesOneElementalToken()
    {
        var alice = new Player("Alice", 20);
        var sp = SeasonedPyromancerFactory.Create(alice);

        // Give Alice 1 land + 1 nonland in hand.
        var landCard = MakeLandCard(alice, "Forest");
        var nonland  = MakeNonlandCard(alice, "Goblin");

        alice.Zones.Hand.AddCard(landCard);
        landCard.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(nonland);
        nonland.SetZone(ZoneType.Hand);

        // Give Alice two library cards to draw.
        var lib1 = MakeNonlandCard(alice, "Lib1");
        var lib2 = MakeNonlandCard(alice, "Lib2");
        alice.Zones.Library.AddCard(lib1);
        lib1.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(lib2);
        lib2.SetZone(ZoneType.Library);

        var etb = sp.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // 1 land + 1 nonland discarded → 1 token.
        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Elemental" && c.BasePower == 1 && c.BaseToughness == 1)
            .ToList();
        tokens.Should().HaveCount(1, "only the nonland discard triggers a token; land does not");
    }

    // -----------------------------------------------------------------------
    // Graveyard-activated ability — creates two 1/1 red Elemental tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void GraveyardAbility_HasActivatedAbility()
    {
        var sp = SeasonedPyromancerFactory.Create(_alice);

        sp.Abilities.OfType<ActivatedAbility>()
            .Should().HaveCount(1, "graveyard activated ability must be wired");
    }

    [Fact]
    public void GraveyardAbility_CreatesTwoElementalTokens_WhenInGraveyard()
    {
        var alice = new Player("Alice", 20);
        var sp = SeasonedPyromancerFactory.Create(alice);
        sp.SetZone(ZoneType.Graveyard);
        alice.Zones.Graveyard.AddCard(sp);

        var activatedAbility = sp.Abilities.OfType<ActivatedAbility>().Single();
        activatedAbility.Resolve();

        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Elemental" && c.BasePower == 1 && c.BaseToughness == 1)
            .ToList();
        tokens.Should().HaveCount(2, "graveyard ability creates two 1/1 Elemental tokens");

        // Self should be exiled, not in graveyard.
        sp.Zone.Should().Be(ZoneType.Exile,
            "the card exiles itself from the graveyard as the activation cost");
        alice.Zones.Graveyard.GetCards().Should().NotContain(sp,
            "card was moved from graveyard to exile");
    }

    [Fact]
    public void GraveyardAbility_NoOp_WhenNotInGraveyard()
    {
        var alice = new Player("Alice", 20);
        var sp = SeasonedPyromancerFactory.Create(alice);
        sp.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(sp);

        var activatedAbility = sp.Abilities.OfType<ActivatedAbility>().Single();
        activatedAbility.Resolve();

        // Should create no tokens — card is not in graveyard.
        var tokens = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Elemental")
            .ToList();
        tokens.Should().BeEmpty("graveyard ability is a no-op when the card is not in the graveyard");
    }
}
