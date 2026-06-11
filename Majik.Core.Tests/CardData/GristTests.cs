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
/// - Zone-conditional CDA (CR 604.3): 1/1 Insect Creature off the battlefield,
///   only a Planeswalker on the battlefield
/// - Grist subtype always present; Insect subtype only off the battlefield
/// - Owner/controller assignment
/// - Green Sun's Zenith integration: Grist is found because HasType(Creature) == true
///   in the library (off battlefield)
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
    public void Grist_OffBattlefield_HasCreatureType_ForTutorTargeting()
    {
        // CR 604.3 — off the battlefield Grist is a 1/1 Insect creature in
        // addition to its other types. A fresh card defaults to the Library
        // zone, so HasType(Creature) is true (creature tutors find it).
        var grist = GristFactory.Create(_alice);

        foreach (var z in new[] { ZoneType.Library, ZoneType.Hand,
                                  ZoneType.Graveyard, ZoneType.Exile, ZoneType.Stack })
        {
            grist.SetZone(z);
            grist.HasType(CardType.Creature).Should().BeTrue(
                $"Grist is a creature in zone {z} (off battlefield, CR 604.3)");
            grist.HasSubtype(CardSubtype.Insect).Should().BeTrue(
                $"Grist is an Insect in zone {z} (off battlefield, CR 604.3)");
            grist.OffBattlefieldPower.Should().Be(1);
            grist.OffBattlefieldToughness.Should().Be(1);
        }
    }

    [Fact]
    public void Grist_OnBattlefield_IsOnlyPlaneswalker_NotCreature()
    {
        // CR 604.3 — the conditional toggles OFF on the battlefield: there Grist
        // is ONLY a Planeswalker (not a creature, not an Insect).
        var grist = GristFactory.Create(_alice);
        grist.SetZone(ZoneType.Battlefield);

        grist.HasType(CardType.Planeswalker).Should().BeTrue("Grist is always a Planeswalker");
        grist.HasType(CardType.Creature).Should().BeFalse(
            "on the battlefield Grist is NOT a creature (CR 604.3)");
        grist.HasSubtype(CardSubtype.Insect).Should().BeFalse(
            "on the battlefield Grist is NOT an Insect (CR 604.3)");
        grist.HasSubtype(CardSubtype.Grist).Should().BeTrue(
            "the printed Grist subtype is present in every zone");
    }

    [Fact]
    public void Grist_OffBattlefield_HasInsectSubtype()
    {
        var grist = GristFactory.Create(_alice);
        grist.SetZone(ZoneType.Graveyard);

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
        // Fresh card defaults to the Library zone (off battlefield), so the CDA
        // applies and the Creature type is present.
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Control cases: the off-battlefield CDA hook must not affect normal cards.
    // -----------------------------------------------------------------------

    [Fact]
    public void NormalPlaneswalker_NeverGainsCreatureType_InAnyZone()
    {
        var koth = KothOfTheHammerFactory.Create(_alice);

        foreach (var z in new[] { ZoneType.Library, ZoneType.Hand, ZoneType.Graveyard,
                                  ZoneType.Exile, ZoneType.Stack, ZoneType.Battlefield })
        {
            koth.SetZone(z);
            koth.HasType(CardType.Planeswalker).Should().BeTrue();
            koth.HasType(CardType.Creature).Should().BeFalse(
                $"Koth has no off-battlefield CDA; it is never a creature (zone {z})");
        }
        koth.OffBattlefieldPower.Should().BeNull();
        koth.OffBattlefieldToughness.Should().BeNull();
    }

    [Fact]
    public void NormalCreature_KeepsCreatureType_OnAndOffBattlefield()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });

        foreach (var z in new[] { ZoneType.Library, ZoneType.Graveyard,
                                  ZoneType.Battlefield })
        {
            bear.SetZone(z);
            bear.HasType(CardType.Creature).Should().BeTrue(
                $"a normal creature is a creature in every zone (zone {z})");
            bear.HasType(CardType.Planeswalker).Should().BeFalse();
            bear.HasSubtype(CardSubtype.Insect).Should().BeFalse(
                "no off-battlefield CDA grants the Bear the Insect subtype");
        }
        bear.OffBattlefieldPower.Should().BeNull();
        bear.OffBattlefieldToughness.Should().BeNull();
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
            sacrificeResolver: null, destroyTargetResolver: null);
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
            sacrificeResolver: null, destroyTargetResolver: null);
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
            sacrificeResolver: null, destroyTargetResolver: null);
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
            destroyTargetResolver: () => new Permanent[] { victim });
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
            destroyTargetResolver: () => new Permanent[] { victim });
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
            destroyTargetResolver: null);
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        var minus5 = grist.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -5);
        // Need at least 5 loyalty to use the ultimate.
        grist.AddLoyalty(2); // 3 -> 5
        minus5.PayLoyaltyCost();
        // The −5 reads opponents off the LIVE resolution context — resolve its
        // effects through a GameContext exactly as the dispatch path does.
        ResolveLoyaltyWithGame(minus5, _alice, _alice, bob, carol);

        grist.Loyalty.Should().Be(0, "5 - 5 = 0");
        bob.LifeTotal.Should().Be(17, "20 - 3 creature cards");
        carol.LifeTotal.Should().Be(17, "20 - 3 creature cards");
    }

    [Fact]
    public void Grist_Minus5_ReadsOpponentsFromContext_NotControllerOrLostPlayers()
    {
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);
        carol.MarkLost(); // a player who has left the game is not hit (CR 800.4a).

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(creature); creature.SetZone(ZoneType.Graveyard);

        var grist = GristFactory.Create(_alice,
            zones: null,
            sacrificeResolver: null,
            destroyTargetResolver: null);
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        var minus5 = grist.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -5);
        grist.AddLoyalty(2);
        minus5.PayLoyaltyCost();
        ResolveLoyaltyWithGame(minus5, _alice, _alice, bob, carol);

        _alice.LifeTotal.Should().Be(20, "the controller is never their own opponent");
        bob.LifeTotal.Should().Be(19, "20 - 1 creature card");
        carol.LifeTotal.Should().Be(20, "a player who has lost the game is not affected");
    }

    /// <summary>
    /// PROD-PATH guard (the resolver-null bug class). The routed prod build
    /// resolves the loyalty ability through the stack
    /// (<c>TurnDriver.DispatchLoyalty → ActivatedAbility.ResolveAsync</c>),
    /// which threads the live <see cref="GameContext"/> into <c>rc.Game</c>.
    /// Grist built via <see cref="NamedCardFactory"/> (no captured resolver)
    /// must make each opponent lose life when resolved that way.
    /// </summary>
    [Fact]
    public void Grist_Minus5_EachOpponentLosesLife_OnProdBuild()
    {
        var bob = new Player("Bob", 20);

        var built = NamedCardFactory.Create("Grist, the Hunger Tide", _alice);
        built.Should().BeOfType<Planeswalker>();
        var grist = (Planeswalker)built;
        _alice.Zones.Battlefield.AddCard(grist);
        grist.SetZone(ZoneType.Battlefield);

        var creature = new Creature("Tarmogoyf", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(creature); creature.SetZone(ZoneType.Graveyard);

        var minus5 = grist.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -5);
        grist.AddLoyalty(2);
        minus5.PayLoyaltyCost();
        ResolveLoyaltyWithGame(minus5, _alice, _alice, bob);

        bob.LifeTotal.Should().Be(19,
            "the prod-built −5 reads opponents from the live context (not inert)");
    }

    /// <summary>
    /// Resolve a loyalty ability's effects through the async resolution path
    /// with a live <see cref="GameContext"/> built from <paramref name="players"/>,
    /// mirroring how <c>TurnDriver.DispatchLoyalty</c> builds the
    /// <see cref="Abilities.ActivatedAbility"/> stack object and resolves it.
    /// </summary>
    private static void ResolveLoyaltyWithGame(
        LoyaltyAbility loyalty, Player controller, params Player[] players)
    {
        var game = new GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var stackObject = new Majik.Core.Abilities.ActivatedAbility(
            source: loyalty.Source,
            controller: controller,
            costs: null,
            effects: loyalty.Effects);
        stackObject.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }
}
