using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SilundiVisionFactory"/> and
/// <see cref="SilundiIsleFactory"/> — the front + back faces of the Zendikar
/// Rising modal double-faced card Silundi Vision // Silundi Isle.
///
/// Front face (Silundi Vision, {2}{U}):
///   Instant. "Look at the top six cards of your library. You may reveal an
///   instant or sorcery card from among them and put it into your hand. Put
///   the rest on the bottom of your library in a random order."
///
/// Back face (Silundi Isle):
///   Land. "This land enters tapped." "{T}: Add {U}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - NamedCardFactory dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: default selector picks first instant/sorcery; rest to bottom.
/// - Front: no instant/sorcery → nothing revealed, all six bottomed.
/// - Front: short library handled.
/// - Front: empty library no-op.
/// - Front: "may" opt-out via a declining selector.
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {U} mana ability.
/// - Back: unconditional enters-tapped replacement.
/// </summary>
[Trait("Color", "U")]
public class SilundiVisionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void SilundiVision_Identity_2U_Instant()
    {
        var card = SilundiVisionFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Silundi Vision");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SilundiVision_IsBlue()
    {
        var card = SilundiVisionFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue, "the {U} pip makes it blue");
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Black);
    }

    [Fact]
    public void SilundiVision_CarriesMdfcState_FrontFace()
    {
        var card = SilundiVisionFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Silundi Vision is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Silundi Vision");
        card.MdfcState!.BackFaceName.Should().Be("Silundi Isle");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Silundi Vision");
    }

    // =========================================================================
    // Front face — resolve dig
    // =========================================================================

    [Fact]
    public void Resolve_LibraryWithInstant_PicksIt_RestToBottom()
    {
        // Top six: a creature, an instant, then four others. Default selector
        // picks the first instant-or-sorcery — the instant at index 1.
        var top6 = new List<ICard>
        {
            SeedLibrary(_alice, "Grizzly Bears", "{1}{G}", CardType.Creature),
            SeedLibrary(_alice, "Lightning Bolt", "{R}", CardType.Instant),
            SeedLibrary(_alice, "Llanowar Elves", "{G}", CardType.Creature),
            SeedLibrary(_alice, "Memnite", "", CardType.Artifact),
            SeedLibrary(_alice, "Plains", "", CardType.Land),
            SeedLibrary(_alice, "Forest", "", CardType.Land),
        };
        var bolt = top6[1];

        var effects = SilundiVisionFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
        _alice.Zones.Hand.GetCards().Count().Should().Be(1);

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Count.Should().Be(5);
        lib.Should().NotContain(bolt);
        lib.Should().BeEquivalentTo(new[] { top6[0], top6[2], top6[3], top6[4], top6[5] });
    }

    [Fact]
    public void Resolve_PicksSorcery_WhenNoInstant()
    {
        var top = new List<ICard>
        {
            SeedLibrary(_alice, "Grizzly Bears", "{1}{G}", CardType.Creature),
            SeedLibrary(_alice, "Divination", "{2}{U}", CardType.Sorcery),
            SeedLibrary(_alice, "Forest", "", CardType.Land),
        };
        var divination = top[1];

        var effects = SilundiVisionFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(divination);
        _alice.Zones.Hand.GetCards().Count().Should().Be(1);
    }

    [Fact]
    public void Resolve_NoInstantOrSorcery_NothingRevealed_AllBottomed()
    {
        var top = new List<ICard>
        {
            SeedLibrary(_alice, "Grizzly Bears", "{1}{G}", CardType.Creature),
            SeedLibrary(_alice, "Llanowar Elves", "{G}", CardType.Creature),
            SeedLibrary(_alice, "Forest", "", CardType.Land),
        };

        var effects = SilundiVisionFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Count.Should().Be(3);
        lib.Should().BeEquivalentTo(top);
    }

    [Fact]
    public void Resolve_LooksAtTopSix_RemainderBottomed()
    {
        // Seven cards: a sorcery at index 6 is OUTSIDE the top six, so it
        // must NOT be picked even though it's an instant/sorcery.
        var cards = new List<ICard>();
        for (var i = 0; i < 6; i++)
            cards.Add(SeedLibrary(_alice, $"Creature{i}", "{1}{G}", CardType.Creature));
        var deepSorcery = SeedLibrary(_alice, "Deep Sorcery", "{1}{U}", CardType.Sorcery);

        var effects = SilundiVisionFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "the only instant/sorcery is the 7th card, below the top six");
        _alice.Zones.Library.GetCards().Should().Contain(deepSorcery);
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoOp()
    {
        var effects = SilundiVisionFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_DecliningSelector_RevealsNothing()
    {
        // CR 116.1b — "you may" opt-out: a selector that bottoms everything.
        var top = new List<ICard>
        {
            SeedLibrary(_alice, "Lightning Bolt", "{R}", CardType.Instant),
            SeedLibrary(_alice, "Forest", "", CardType.Land),
        };

        var effects = SilundiVisionFactory.BuildResolveEffect(
            _alice,
            selector: peeked => (Array.Empty<ICard>(), peeked));
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty("the declining selector revealed nothing");
        _alice.Zones.Library.GetCards().Count().Should().Be(2);
    }

    [Fact]
    public void DefaultSelector_PicksFirstInstantOrSorcery()
    {
        var peeked = new List<ICard>
        {
            MakeCard("Bear", "{1}{G}", CardType.Creature),
            MakeCard("Bolt", "{R}", CardType.Instant),     // first instant/sorcery
            MakeCard("Divination", "{2}{U}", CardType.Sorcery),
        };

        var (toHand, toBottom) = SilundiVisionFactory.DefaultVisionSelector(peeked);

        toHand.Should().HaveCount(1);
        toHand[0].Name.Should().Be("Bolt");
        toBottom.Should().HaveCount(2);
        toBottom.Should().NotContain(c => c.Name == "Bolt");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void SilundiIsle_Identity_Land()
    {
        var land = SilundiIsleFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Silundi Isle");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Silundi Isle is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SilundiIsle_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = SilundiIsleFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Silundi Isle is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Silundi Vision");
        land.MdfcState!.BackFaceName.Should().Be("Silundi Isle");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Silundi Isle");
    }

    [Fact]
    public void SilundiIsle_HasSingleManaAbility_AddingBlue()
    {
        var land = SilundiIsleFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {U} ability");
        manaAbilities[0].ManaGenerated.Blue.Should().BeGreaterThan(0, "produces blue mana");
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }

    [Fact]
    public void SilundiIsle_EntersTapped_Unconditionally()
    {
        var bus = new ReplacementBus();
        var land = SilundiIsleFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Silundi Isle always enters tapped (CR 614.1c) — no opt-out");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private ICard SeedLibrary(Player p, string name, string manaCost, CardType type)
    {
        var c = MakeCard(name, manaCost, type);
        c.SetOwner(p);
        c.SetZone(ZoneType.Library);
        p.Zones.Library.AddCard(c);
        return c;
    }

    private static ICard MakeCard(string name, string manaCost, CardType type) => type switch
    {
        CardType.Instant => new Instant(name, manaCost),
        CardType.Sorcery => new Sorcery(name, manaCost),
        CardType.Creature => new Creature(name, manaCost, 1, 1),
        CardType.Land => new Land(name, supertypes: null, subtypes: null),
        _ => new Artifact(name, manaCost),
    };
}
