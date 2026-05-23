using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Scapeshift (Morningtide, {2}{G}{G}, Sorcery).
///
/// Oracle: "Sacrifice any number of lands. Search your library for that
/// many land cards, put them onto the battlefield, then shuffle."
/// (CR 701.16 + CR 701.19a)
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Sacrifice N lands → tutor exactly N lands from library to
///     battlefield untapped (Titanshift combo backbone).
///   - Sacrifice 0 lands → no-op (lower bound of "any number").
///   - Tutor side is clamped to N: extra picks beyond the sacrificed
///     count are ignored.
/// </summary>
public class ScapeshiftTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land MakeLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    private static Land MakeNonbasicLand(string name, Player owner)
    {
        var land = new Land(name, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Scapeshift_IsSorcery_At2GG()
    {
        var s = ScapeshiftFactory.Create(_alice);

        s.Name.Should().Be("Scapeshift");
        s.ManaCost.Should().Be("{2}{G}{G}");
        s.HasType(CardType.Sorcery).Should().BeTrue();
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Scapeshift()
    {
        var card = NamedCardFactory.Create("Scapeshift", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Scapeshift");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{G}{G}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — sac N → tutor N
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_Sac2Lands_Tutors2Lands_OntoBattlefield()
    {
        // Battlefield: two basic Forests to sacrifice.
        // Library: two Mountains (the canonical Valakut/Titanshift target).
        var forest1 = MakeLand("Forest", _alice, CardSubtype.Forest);
        var forest2 = MakeLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Battlefield.AddCard(forest1);
        _alice.Zones.Battlefield.AddCard(forest2);

        var mountain1 = MakeLand("Mountain", _alice, CardSubtype.Mountain);
        var mountain2 = MakeLand("Mountain", _alice, CardSubtype.Mountain);
        _alice.Zones.Library.AddCard(mountain1);
        _alice.Zones.Library.AddCard(mountain2);

        var effects = ScapeshiftFactory.BuildResolveEffect(
            _alice,
            sacSelector: _ => new ICard[] { forest1, forest2 },
            tutorSelector: _ => new ICard[] { mountain1, mountain2 });
        foreach (var fx in effects) fx.Execute();

        // Sacrificed Forests landed in the graveyard.
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(new[] { forest1, forest2 });

        // Both Mountains came onto the battlefield untapped (CR 701.19a).
        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(new[] { mountain1, mountain2 });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        mountain1.Zone.Should().Be(ZoneType.Battlefield);
        mountain2.Zone.Should().Be(ZoneType.Battlefield);
        // Printed oracle says "put them onto the battlefield" (no "tapped").
        mountain1.As<Permanent>().IsTapped.Should().BeFalse();
        mountain2.As<Permanent>().IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Sac0Lands_IsCleanNoOp()
    {
        // "Any number" lower bound — sac zero lands means tutor zero
        // lands. Even if the caller offers tutor picks, the closure
        // must short-circuit before touching the library.
        var forest = MakeLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Battlefield.AddCard(forest);

        var mountain = MakeLand("Mountain", _alice, CardSubtype.Mountain);
        _alice.Zones.Library.AddCard(mountain);

        var effects = ScapeshiftFactory.BuildResolveEffect(
            _alice,
            sacSelector: _ => Array.Empty<ICard>(),
            tutorSelector: _ => new ICard[] { mountain });
        foreach (var fx in effects) fx.Execute();

        // Forest still on the battlefield, graveyard empty.
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle().Which.Should().BeSameAs(forest);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();

        // Library untouched — short-circuit fires before tutor.
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(mountain);
    }

    [Fact]
    public void Resolve_TutorPicksClampedToSacrificeCount()
    {
        // Sacrifice 1 land. Caller offers 2 library picks — only the
        // first should land on the battlefield. The second pick stays
        // in the library (CR 701.19a — fetched count = sacrificed
        // count, not the size of the offered candidate set).
        var forest = MakeLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Battlefield.AddCard(forest);

        var mountain = MakeLand("Mountain", _alice, CardSubtype.Mountain);
        var tower = MakeNonbasicLand("Urza's Tower", _alice);
        _alice.Zones.Library.AddCard(mountain);
        _alice.Zones.Library.AddCard(tower);

        var effects = ScapeshiftFactory.BuildResolveEffect(
            _alice,
            sacSelector: _ => new ICard[] { forest },
            tutorSelector: _ => new ICard[] { mountain, tower });
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(forest);
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle().Which.Should().BeSameAs(mountain);
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(tower);
    }

    [Fact]
    public void Resolve_NonLandSacrificePicksAreFilteredDefensively()
    {
        // Mis-selector hands the closure a creature alongside one land —
        // only the land is sacrificed, only one library land is fetched.
        // (Engine-level defence; production agent should never propose a
        // non-land sac for Scapeshift, but the closure must not crash.)
        var forest = MakeLand("Forest", _alice, CardSubtype.Forest);
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        _alice.Zones.Battlefield.AddCard(grizzly);

        var mountain = MakeLand("Mountain", _alice, CardSubtype.Mountain);
        _alice.Zones.Library.AddCard(mountain);

        var effects = ScapeshiftFactory.BuildResolveEffect(
            _alice,
            sacSelector: _ => new ICard[] { grizzly, forest },
            tutorSelector: _ => new ICard[] { mountain });
        foreach (var fx in effects) fx.Execute();

        // Forest sacrificed; bears untouched.
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(forest);
        _alice.Zones.Battlefield.GetCards().Should().Contain(grizzly);

        // Exactly one tutor pick landed (N = 1, not 2).
        _alice.Zones.Battlefield.GetCards().Should().Contain(mountain);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
