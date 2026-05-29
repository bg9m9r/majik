using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MonumentalHengeFactory"/>.
///
/// Land. Oracle text:
///   "This land enters tapped unless you control a Plains.
///    {T}: Add {W}.
///    {2}{W}{W}, {T}: Look at the top five cards of your library. You may
///    reveal a historic card from among them and put it into your hand. Put
///    the rest on the bottom of your library in a random order. (Artifacts,
///    legendaries, and Sagas are historic.)"
///
/// Covers:
/// - Identity (Land type, name, owner/controller, non-Basic / non-Legendary).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {W} mana ability.
/// - The {2}{W}{W}, {T} activated ability (cost shape).
/// - ETB-tapped-unless-Plains predicate via
///   <see cref="ConditionalEntersTappedReplacement"/>.
/// - Look-at-top-five: reveals a historic card to hand, re-bottoms the rest;
///   historic = Artifact / Legendary / Saga; no historic → no card to hand.
/// </summary>
public class MonumentalHengeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_IsLand()
    {
        var land = MonumentalHengeFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Monumental Henge");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Monumental Henge", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Monumental Henge");
    }

    // -----------------------------------------------------------------------
    // Abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void HasWhiteManaAbility()
    {
        var land = MonumentalHengeFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().ContainSingle("Monumental Henge has {T}: Add {W}");
        var white = ManaCost.Parse("W");
        mana.Should().Contain(m =>
            m.ManaGenerated.White == white.White &&
            m.ManaGenerated.Blue == 0 &&
            m.ManaGenerated.Black == 0 &&
            m.ManaGenerated.Red == 0 &&
            m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void HasActivatedLookAbility_WithTwoWWTapCost()
    {
        var land = MonumentalHengeFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().ContainSingle(
            "Monumental Henge has one non-mana activated ability ({2}{W}{W}, {T})");
        // The cost set is {2}{W}{W} plus a tap cost.
        activated[0].Costs.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // ETB tapped unless you control a Plains (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenControllerHasNoPlains()
    {
        var bus = new ReplacementBus();
        var land = MonumentalHengeFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Monumental Henge enters tapped when the controller has no Plains");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasPlains()
    {
        var bus = new ReplacementBus();
        var plains = (Land)NamedCardFactory.Create("Plains", _alice);
        _alice.Zones.Battlefield.AddCard(plains);
        plains.SetZone(ZoneType.Battlefield);

        var land = MonumentalHengeFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Monumental Henge enters untapped when the controller controls a Plains");
    }

    [Fact]
    public void EntersTapped_WhenOnlyOpponentHasPlains()
    {
        // "you control" — opponent's Plains does not satisfy the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        var bobPlains = (Land)NamedCardFactory.Create("Plains", bob);
        bob.Zones.Battlefield.AddCard(bobPlains);
        bobPlains.SetZone(ZoneType.Battlefield);

        var land = MonumentalHengeFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Monumental Henge enters tapped when only the opponent controls a Plains");
    }

    [Fact]
    public void SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // Shape-only dispatcher path — no ReplacementBus, so the ETB-tapped
        // predicate is not wired. Matches every ETB-replacement factory.
        var land = (Land)NamedCardFactory.Create("Monumental Henge", _alice);
        land.Abilities.OfType<ManaAbility>().Should().ContainSingle();
        land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle();
    }

    // -----------------------------------------------------------------------
    // {2}{W}{W}, {T}: look at top 5, may reveal a historic card to hand
    // -----------------------------------------------------------------------

    [Fact]
    public void IsHistoric_TrueForArtifact_Legendary_Saga()
    {
        var artifact = new Artifact("Sol Ring", "{1}");
        var legendary = new Creature(
            "Emrakul", "{15}", 15, 15,
            supertypes: new[] { CardSupertype.Legendary });
        var saga = new Enchantment(
            "The Eldest Reborn", "{4}{B}",
            subtypes: new[] { CardSubtype.Saga });

        MonumentalHengeFactory.IsHistoric(artifact).Should().BeTrue("artifacts are historic");
        MonumentalHengeFactory.IsHistoric(legendary).Should().BeTrue("legendaries are historic");
        MonumentalHengeFactory.IsHistoric(saga).Should().BeTrue("Sagas are historic");
    }

    [Fact]
    public void IsHistoric_FalseForVanillaNonlegendaryCreature()
    {
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        MonumentalHengeFactory.IsHistoric(grizzly).Should().BeFalse(
            "a non-artifact, non-legendary, non-Saga card is not historic");
    }

    [Fact]
    public void ResolveLook_RevealsHistoricCardToHand_RestToBottom()
    {
        // Top of library: a nonhistoric card, then a historic artifact.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);

        foreach (var c in new ICard[] { bolt, solRing, bear })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        MonumentalHengeFactory.ResolveLook(_alice, MonumentalHengeFactory.DefaultHistoricSelector);

        // The historic artifact went to hand.
        _alice.Zones.Hand.GetCards().Should().Contain(solRing,
            "the first historic card among the peeked cards goes to hand");
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
        _alice.Zones.Hand.GetCards().Should().NotContain(bear);

        // The rest are back in the library (on the bottom).
        var libCards = _alice.Zones.Library.GetCards();
        libCards.Should().Contain(bolt);
        libCards.Should().Contain(bear);
        libCards.Should().NotContain(solRing);
        libCards.Should().HaveCount(2);
    }

    [Fact]
    public void ResolveLook_NoHistoricCard_NothingToHand_AllReBottomed()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);

        foreach (var c in new ICard[] { bolt, bear })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        MonumentalHengeFactory.ResolveLook(_alice, MonumentalHengeFactory.DefaultHistoricSelector);

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no historic card among the peeked cards → nothing revealed to hand");
        _alice.Zones.Library.GetCards().Should().HaveCount(2,
            "all peeked cards return to the bottom of the library");
    }

    [Fact]
    public void ResolveLook_EmptyLibrary_IsNoOp()
    {
        var act = () => MonumentalHengeFactory.ResolveLook(
            _alice, MonumentalHengeFactory.DefaultHistoricSelector);

        act.Should().NotThrow("an empty library is a clean no-op");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
