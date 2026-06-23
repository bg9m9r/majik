using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SteamcoreScholarFactory"/>.
///
/// Steamcore Scholar (Murders at Karlov Manor, {2}{U}). Creature — Weird
/// Detective 2/2. Oracle text (verified against Scryfall):
///   "Flying, vigilance
///    When this creature enters, draw two cards. Then discard two cards
///    unless you discard an instant or sorcery card or a creature card with
///    flying."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({2}{U} Creature — Weird Detective, 2/2, mono-U) + the
///   Flying + Vigilance keyword markers.
/// - ETB: draw two cards (CR 121.1), then the "discard two unless …" rider
///   (CR 701.8) — discards two when the hand has no qualifying card, but only
///   ONE when an instant/sorcery or a creature-with-flying is available.
/// </summary>
[Trait("Color", "U")]
public class SteamcoreScholarFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity + keywords
    // -----------------------------------------------------------------------

    [Fact]
    public void SteamcoreScholar_Identity()
    {
        var c = SteamcoreScholarFactory.Create(_alice);

        c.Name.Should().Be("Steamcore Scholar");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Weird).Should().BeTrue();
        c.HasSubtype(CardSubtype.Detective).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.ManaCost.Should().Be("{2}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().HaveCount(1);
    }

    [Fact]
    public void SteamcoreScholar_HasFlyingAndVigilance()
    {
        var c = SteamcoreScholarFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Vigilance");
    }

    [Fact]
    public void SteamcoreScholar_HasSingleEtbTrigger_BattlefieldActive()
    {
        var c = SteamcoreScholarFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the ETB draw-then-discard trigger");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void StockLibrary(int count)
    {
        for (var i = 0; i < count; i++)
            _alice.Zones.Library.AddCard(new Creature($"Lib{i}", "{1}", 1, 1));
    }

    private void AddVanillaToHand(int count)
    {
        for (var i = 0; i < count; i++)
            _alice.Zones.Hand.AddCard(new Creature($"Vanilla{i}", "{1}", 1, 1));
    }

    private static Creature FlyingCreature(string name)
    {
        var c = new Creature(name, "{1}{U}", 1, 1);
        c.AddAbility(new KeywordAbility("Flying", c, null!));
        return c;
    }

    private int HandCount() => _alice.Zones.Hand.GetCards().Count();
    private int GraveyardCount() => _alice.Zones.Graveyard.GetCards().Count();

    // -----------------------------------------------------------------------
    // ETB — draw two, then discard
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_DrawsTwoCards()
    {
        StockLibrary(4);
        // Hand starts empty → after draw two, only those two are in hand and
        // neither qualifies (vanilla creatures), so both get discarded.
        SteamcoreScholarFactory.ResolveEtb(_alice);

        _alice.Zones.Library.GetCards().Should().HaveCount(2, "two of four library cards were drawn");
    }

    [Fact]
    public void Etb_NoQualifyingCard_DiscardsTwo()
    {
        StockLibrary(2);
        AddVanillaToHand(3); // pre-existing non-qualifying cards

        // Hand: 3 vanilla. Draw two more vanilla → 5 vanilla. None qualify.
        SteamcoreScholarFactory.ResolveEtb(_alice);

        // 3 + 2 drawn − 2 discarded = 3.
        HandCount().Should().Be(3, "no qualifying card → discard the full two (CR 701.8)");
        GraveyardCount().Should().Be(2);
    }

    [Fact]
    public void Etb_QualifyingFlyingCreature_DiscardsOnlyOne()
    {
        StockLibrary(2);
        _alice.Zones.Hand.AddCard(FlyingCreature("SkyFlyer"));
        AddVanillaToHand(1);

        // Hand: 1 flyer + 1 vanilla. Draw two more vanilla → 4 cards, one of
        // which (the flyer) is a creature with flying → discard ONLY it.
        SteamcoreScholarFactory.ResolveEtb(_alice);

        // 2 + 2 drawn − 1 discarded = 3.
        HandCount().Should().Be(3,
            "discarding the flying creature satisfies the rider → only one discard (CR 701.8)");
        GraveyardCount().Should().Be(1);
        _alice.Zones.Graveyard.GetCards().Single().Name.Should().Be("SkyFlyer");
    }

    [Fact]
    public void Etb_QualifyingInstant_DiscardsOnlyOne()
    {
        StockLibrary(2);
        _alice.Zones.Hand.AddCard(new Instant("Counterspell", "{U}{U}"));

        // Hand: 1 instant. Draw two vanilla → 3 cards. The instant qualifies
        // → discard ONLY it.
        SteamcoreScholarFactory.ResolveEtb(_alice);

        HandCount().Should().Be(2,
            "discarding the instant satisfies the rider → only one discard (CR 701.8)");
        GraveyardCount().Should().Be(1);
        _alice.Zones.Graveyard.GetCards().Single().Name.Should().Be("Counterspell");
    }

    [Fact]
    public void Etb_QualifyingSorcery_DiscardsOnlyOne()
    {
        StockLibrary(2);
        _alice.Zones.Hand.AddCard(new Sorcery("Divination", "{2}{U}"));

        SteamcoreScholarFactory.ResolveEtb(_alice);

        HandCount().Should().Be(2,
            "discarding the sorcery satisfies the rider → only one discard (CR 701.8)");
        GraveyardCount().Should().Be(1);
        _alice.Zones.Graveyard.GetCards().Single().Name.Should().Be("Divination");
    }

    // -----------------------------------------------------------------------
    // Qualifying-discard predicate (CR 701.8)
    // -----------------------------------------------------------------------

    [Fact]
    public void IsQualifyingDiscard_Classifies_Correctly()
    {
        SteamcoreScholarFactory.IsQualifyingDiscard(new Instant("I", "{U}")).Should().BeTrue();
        SteamcoreScholarFactory.IsQualifyingDiscard(new Sorcery("S", "{U}")).Should().BeTrue();
        SteamcoreScholarFactory.IsQualifyingDiscard(FlyingCreature("F")).Should().BeTrue();

        SteamcoreScholarFactory.IsQualifyingDiscard(new Creature("Ground", "{1}", 1, 1))
            .Should().BeFalse("a non-flying creature does not satisfy the rider");
    }
}
