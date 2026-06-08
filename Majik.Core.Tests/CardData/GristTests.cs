using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GristFactory"/>.
///
/// Covers:
/// - Card identity: Legendary Planeswalker with loyalty 3
/// - V1 simplification: Creature type added unconditionally
/// - Insect + Grist subtypes present
/// - Owner/controller assignment
/// - Green Sun's Zenith integration: Grist is found because HasType(Creature) == true
/// </summary>
public class GristTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Grist_IsLegendaryPlaneswalker()
    {
        var grist = GristFactory.Create(_alice);

        grist.HasType(CardType.Planeswalker).Should().BeTrue("Grist is a Planeswalker");
        grist.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Grist is Legendary");
    }

    [Fact]
    public void Grist_HasCreatureType_ForTutorTargeting()
    {
        // V1 simplification: Creature type is added unconditionally so that
        // tutors like Green Sun's Zenith can target Grist in all zones.
        var grist = GristFactory.Create(_alice);

        grist.HasType(CardType.Creature).Should().BeTrue(
            "Grist's Creature type is added unconditionally in v1 to enable tutor targeting");
    }

    [Fact]
    public void Grist_HasInsectSubtype()
    {
        var grist = GristFactory.Create(_alice);

        grist.HasSubtype(CardSubtype.Insect).Should().BeTrue();
    }

    [Fact]
    public void Grist_HasGristSubtype()
    {
        var grist = GristFactory.Create(_alice);

        grist.HasSubtype(CardSubtype.Grist).Should().BeTrue();
    }

    [Fact]
    public void Grist_HasLoyalty3()
    {
        var grist = GristFactory.Create(_alice);

        grist.Loyalty.Should().Be(3);
        grist.StartingLoyalty.Should().Be(3);
    }

    [Fact]
    public void Grist_OwnerAndControllerAreSet()
    {
        var grist = GristFactory.Create(_alice);

        grist.Owner.Should().BeSameAs(_alice);
        grist.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Grist_ManaCostAndName()
    {
        var grist = GristFactory.Create(_alice);

        grist.Name.Should().Be("Grist, the Hunger Tide");
        grist.ManaCost.Should().Be("{1}{B}{G}");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory route
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_CreatesGrist()
    {
        var card = NamedCardFactory.Create("Grist, the Hunger Tide", _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be("Grist, the Hunger Tide");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Green Sun's Zenith integration
    // Verifies Grist is found by GSZ because HasType(Creature) == true and
    // it is green (B/G pips) with mana value 3 (1 generic + B + G).
    // -----------------------------------------------------------------------

    [Fact]
    public void GreenSunsZenith_CanTutorGrist_BecauseItHasCreatureType()
    {
        var alice = new Player("Alice", 20);
        var grist = GristFactory.Create(alice);
        grist.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(grist);

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Green Sun's Zenith",
                ManaCost = "{X}{G}",
                OracleText =
                    "Search your library for a green creature card with mana value X or less, " +
                    "put it onto the battlefield, then shuffle. " +
                    "Shuffle Green Sun's Zenith into its owner's library.",
            },
            alice, raw => raw, null);

        def.Should().NotBeNull();

        // X = 3: Grist's CMC is 3 (1 generic + B + G pip), so X=3 exactly meets it.
        var chosen = new ChosenSpellParams(null, X: 3,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.Zones.Battlefield.GetCards().Should().Contain(grist,
            "GSZ with X=3 should find Grist (CMC 3, green, HasType(Creature))");
        alice.Zones.Library.GetCards().Should().NotContain(grist);
    }

    [Fact]
    public void GreenSunsZenith_XTooLow_DoesNotTutorGrist()
    {
        var alice = new Player("Alice", 20);
        var grist = GristFactory.Create(alice);
        grist.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(grist);

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Green Sun's Zenith",
                ManaCost = "{X}{G}",
                OracleText =
                    "Search your library for a green creature card with mana value X or less, " +
                    "put it onto the battlefield, then shuffle. " +
                    "Shuffle Green Sun's Zenith into its owner's library.",
            },
            alice, raw => raw, null);

        def.Should().NotBeNull();

        // X = 2: Grist's CMC is 3 — should not be found.
        var chosen = new ChosenSpellParams(null, X: 2,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.Zones.Library.GetCards().Should().Contain(grist,
            "GSZ with X=2 should not find Grist (CMC 3 > 2)");
        alice.Zones.Battlefield.GetCards().Should().NotContain(grist);
    }

    // -----------------------------------------------------------------------
    // Loyalty abilities — present on the routed/factory build
    // -----------------------------------------------------------------------

    [Fact]
    public void Grist_HasThreeLoyaltyAbilities_Plus1_Minus2_Minus5()
    {
        var grist = GristFactory.Create(_alice);
        var loyalty = grist.Abilities.OfType<LoyaltyAbility>().ToList();

        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -2, -5 });
    }

    [Fact]
    public void NamedCardFactory_Grist_HasLoyaltyAbilities()
    {
        // The source generator must route dispatch to Create(owner) (which
        // attaches loyalty abilities), NOT the bare Define() shape.
        var card = NamedCardFactory.Create("Grist, the Hunger Tide", _alice);

        card.Should().BeOfType<Planeswalker>();
        ((Planeswalker)card).Abilities.OfType<LoyaltyAbility>()
            .Should().HaveCount(3, "the routed build must keep the loyalty abilities");
    }

    // -----------------------------------------------------------------------
    // +1: Create a 1/1 black-and-green Insect token, then mill a card. If an
    //     Insect card was milled, put a loyalty counter on Grist and repeat.
    // -----------------------------------------------------------------------

    [Fact]
    public void Grist_Plus1_CreatesInsectToken_AndMillsOneCard_WhenNoInsectMilled()
    {
        var zones = new ZoneService();
        var grist = GristFactory.Create(_alice, zones,
            sacrificeResolver: null, destroyTargetResolver: null, opponentsResolver: null);
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        // Library top is a non-Insect card → loop runs once.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var plus1 = grist.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        grist.Loyalty.Should().Be(4, "+1 cost adds 1 (3 -> 4); no Insect milled so no extra counter");

        // One Insect token entered.
        var insectTokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Insect))
            .ToList();
        insectTokens.Should().HaveCount(1);
        var token = insectTokens[0];
        token.GetPower().Should().Be(1);
        token.GetToughness().Should().Be(1);
        var colors = CardColors.GetColors(token);
        colors.Should().Contain(ManaColor.Black).And.Contain(ManaColor.Green);

        // The bear was milled to the graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void Grist_Plus1_RepeatsAndGainsLoyalty_WhenInsectCardMilled()
    {
        var zones = new ZoneService();
        var grist = GristFactory.Create(_alice, zones,
            sacrificeResolver: null, destroyTargetResolver: null, opponentsResolver: null);
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        // Library (top first): an Insect card, then a non-Insect card.
        // First mill -> Insect -> +1 loyalty counter + repeat.
        // Second token + second mill -> non-Insect -> stop.
        var insectCard = new Creature("Hornet Queen", "{4}{G}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Insect });
        insectCard.SetOwner(_alice);
        var nonInsect = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        nonInsect.SetOwner(_alice);
        _alice.Zones.Library.AddCard(insectCard); // top
        insectCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(nonInsect);
        nonInsect.SetZone(ZoneType.Library);

        var plus1 = grist.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        // 3 start + 1 (+1 cost) + 1 (Insect milled -> extra loyalty counter) = 5.
        grist.Loyalty.Should().Be(5);

        // Two iterations -> two Insect tokens.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.IsToken && c.HasSubtype(CardSubtype.Insect))
            .Should().Be(2);

        // Both library cards milled.
        _alice.Zones.Graveyard.GetCards().Should().Contain(insectCard).And.Contain(nonInsect);
    }

    [Fact]
    public void Grist_Plus1_EmptyLibrary_EndsLoop_AfterOneToken()
    {
        var zones = new ZoneService();
        var grist = GristFactory.Create(_alice, zones,
            sacrificeResolver: null, destroyTargetResolver: null, opponentsResolver: null);
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);
        // Empty library — mill returns nothing, not an Insect.

        var plus1 = grist.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        grist.Loyalty.Should().Be(4, "only the +1 cost; empty mill ends the loop");
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.IsToken && c.HasSubtype(CardSubtype.Insect))
            .Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // −2: You may sacrifice a creature. When you do, destroy target creature
    //     or planeswalker.
    // -----------------------------------------------------------------------

    [Fact]
    public void Grist_Minus2_SacrificesCreature_ThenDestroysTarget()
    {
        var grist = GristFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        var sac = new Creature("Sakura-Tribe Elder", "{1}{G}", 1, 1);
        sac.SetOwner(_alice); sac.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sac); sac.SetZone(ZoneType.Battlefield);

        var bob = new Player("Bob", 20);
        var victim = new Creature("Tarmogoyf", "{1}{G}", 4, 5);
        victim.SetOwner(bob); victim.SetController(bob);
        bob.Zones.Battlefield.AddCard(victim); victim.SetZone(ZoneType.Battlefield);

        var grist2 = GristFactory.Create(_alice,
            zones: null,
            sacrificeResolver: () => new[] { sac },
            destroyTargetResolver: () => new Permanent[] { victim },
            opponentsResolver: null);
        _alice.Zones.Battlefield.AddCard(grist2);
        grist2.SetZone(ZoneType.Battlefield);

        var minus2 = grist2.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        grist2.Loyalty.Should().Be(1, "3 - 2 = 1");
        _alice.Zones.Graveyard.GetCards().Should().Contain(sac, "the chosen creature was sacrificed");
        bob.Zones.Graveyard.GetCards().Should().Contain(victim, "the target creature was destroyed");
    }

    [Fact]
    public void Grist_Minus2_NoSacrificeChosen_SkipsDestroy()
    {
        var bob = new Player("Bob", 20);
        var victim = new Creature("Tarmogoyf", "{1}{G}", 4, 5);
        victim.SetOwner(bob); victim.SetController(bob);
        bob.Zones.Battlefield.AddCard(victim); victim.SetZone(ZoneType.Battlefield);

        // "You may sacrifice" — no sacrifice resolver -> the destroy is gated off.
        var grist = GristFactory.Create(_alice,
            zones: null,
            sacrificeResolver: null,
            destroyTargetResolver: () => new Permanent[] { victim },
            opponentsResolver: null);
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        var minus2 = grist.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        grist.Loyalty.Should().Be(1, "3 - 2 = 1; loyalty cost still applies");
        bob.Zones.Battlefield.GetCards().Should().Contain(victim,
            "no sacrifice was made, so the reflexive destroy never happens");
    }

    // -----------------------------------------------------------------------
    // −5: Each opponent loses life equal to the number of creature cards in
    //     your graveyard.
    // -----------------------------------------------------------------------

    [Fact]
    public void Grist_Minus5_EachOpponentLosesLifePerCreatureCardInGraveyard()
    {
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);

        // 3 creature cards + 1 non-creature in Alice's graveyard.
        foreach (var n in new[] { "Llanowar Elves", "Grizzly Bears", "Tarmogoyf" })
        {
            var c = new Creature(n, "{G}", 1, 1);
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c); c.SetZone(ZoneType.Graveyard);
        }
        var bolt = new Majik.Core.Cards.Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt); bolt.SetZone(ZoneType.Graveyard);

        var grist = GristFactory.Create(_alice,
            zones: null,
            sacrificeResolver: null,
            destroyTargetResolver: null,
            opponentsResolver: () => new[] { bob, carol });
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        var minus5 = grist.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -5);
        // Need at least 5 loyalty to use the ultimate.
        grist.AddLoyalty(2); // 3 -> 5
        minus5.Activate();

        grist.Loyalty.Should().Be(0, "5 - 5 = 0");
        bob.LifeTotal.Should().Be(17, "20 - 3 creature cards");
        carol.LifeTotal.Should().Be(17, "20 - 3 creature cards");
    }
}
