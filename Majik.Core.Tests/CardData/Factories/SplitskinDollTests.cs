using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SplitskinDollFactory"/>.
///
/// Splitskin Doll (Duskmourn, {1}{W}):
///   Artifact Creature — Toy 2/1.
///   "When this creature enters, draw a card. Then discard a card unless you
///    control another creature with power 2 or less." (CR 603.1 / CR 121.1 /
///    CR 701.8).
///
/// The ETB always draws; the discard is gated on a BOARD-STATE check — the
/// controller must control ANOTHER creature (CR 109.5, excluding the Doll
/// itself per "another") with power 2 or less. Both halves are exercised here.
/// </summary>
[Trait("Color", "W")]
public class SplitskinDollTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Instant LibraryCard(Player owner, string name = "Lightning Bolt") =>
        new(name, "{R}") { Owner = owner };

    private static Instant HandCard(Player owner, string name = "Opt") =>
        new(name, "{U}") { Owner = owner };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SplitskinDoll_Identity_ArtifactCreatureToy_2_1_White()
    {
        var card = SplitskinDollFactory.Create(_alice);

        card.Name.Should().Be("Splitskin Doll");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue(
            because: "Splitskin Doll is an Artifact Creature (CR 301.1 / 302.1)");
        card.HasSubtype(CardSubtype.Toy).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{W} = mana value 2");
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB — discard branch (no qualifying other creature)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_NoOtherSmallCreature_DrawsThenDiscards()
    {
        // Alice controls only the Doll; no OTHER creature → must discard.
        _alice.Zones.Library.AddCard(LibraryCard(_alice));

        var doll = SplitskinDollFactory.Create(_alice);
        doll.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(doll);

        var etb = doll.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        // Drew the card, then discarded a card → net 0 hand, +1 graveyard, -1 library.
        _alice.Zones.Hand.GetCards().Should().HaveCount(0,
            because: "draw 1 then discard 1 with no qualifying creature nets 0 cards");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1,
            because: "the drawn card is discarded — no other creature with power 2 or less");
        _alice.Zones.Library.GetCards().Should().HaveCount(0);
    }

    // -----------------------------------------------------------------------
    // ETB — no-discard branch (controls another creature with power <= 2)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_AnotherSmallCreature_DrawsAndKeeps()
    {
        _alice.Zones.Library.AddCard(LibraryCard(_alice));

        // Another creature Alice controls with power 2 → satisfies the "unless".
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var doll = SplitskinDollFactory.Create(_alice);
        doll.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(doll);

        var etb = doll.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        // Drew and KEPT — the "unless" condition was met, so no discard.
        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            because: "controls another creature with power 2 or less → keep the drawn card");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(0);
    }

    [Fact]
    public void Etb_OnlyOtherCreatureIsBig_DrawsThenDiscards()
    {
        _alice.Zones.Library.AddCard(LibraryCard(_alice));

        // The only OTHER creature has power 3 → does NOT satisfy "power 2 or less".
        var ogre = new Creature("Hulking Ogre", "{2}{R}", 3, 3) { Owner = _alice, Controller = _alice };
        ogre.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ogre);

        var doll = SplitskinDollFactory.Create(_alice);
        doll.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(doll);

        var etb = doll.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Should().HaveCount(0,
            because: "the only other creature has power 3 — discard fires");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Etb_DollItselfDoesNotSatisfyAnother()
    {
        // The Doll has power 2 but "ANOTHER creature" excludes the Doll (CR 109.5);
        // with no other creature, the discard must fire.
        _alice.Zones.Library.AddCard(LibraryCard(_alice));

        var doll = SplitskinDollFactory.Create(_alice);
        doll.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(doll);

        var etb = doll.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1,
            because: "the Doll's own power 2 does not count — 'another creature' excludes itself");
    }
}
