using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Karn, Scion of Urza (Dominaria, {4}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Karn, loyalty 5, mana
///     cost {4}).
///   - Loyalty ability shape: three abilities at +1 / -1 / -2.
///   - +1: reveal 2 → higher-mv to hand, lower-mv to bottom of library.
///   - +1: single-card library — that card goes to hand (single pile).
///   - -2: spawns a 0/0 Construct artifact creature token.
///   - -2: Construct token's +1/+1-per-artifact-you-control wires up via
///     the supplied <see cref="ContinuousEffectsService"/>.
///   - NamedCardFactory dispatch.
/// </summary>
public class KarnScionOfUrzaTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void KarnScionOfUrza_IsLegendaryPlaneswalker_Karn_5Loyalty_AtCost4()
    {
        var karn = KarnScionOfUrzaFactory.Create(_alice);

        karn.Name.Should().Be("Karn, Scion of Urza");
        karn.ManaCost.Should().Be("{4}");
        karn.HasType(CardType.Planeswalker).Should().BeTrue();
        karn.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        karn.HasSubtype(CardSubtype.Karn).Should().BeTrue();
        karn.Loyalty.Should().Be(5);
        karn.StartingLoyalty.Should().Be(5);
        karn.Owner.Should().BeSameAs(_alice);
        karn.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KarnScionOfUrza_HasThreeLoyaltyAbilities_Plus1_Minus1_Minus2()
    {
        var karn = KarnScionOfUrzaFactory.Create(_alice);
        var loyaltyAbilities = karn.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -1, -2 });
    }

    [Fact]
    public void KarnScionOfUrza_Plus1_RevealsTwo_HigherMvToHand_OtherToBottomOfLibrary()
    {
        // Library top → bottom: Lightning Bolt ({R} mv=1), Ancestral
        // Recall ({U} mv=1)? Use a mv mismatch: bolt mv=1 and a sorcery
        // mv=3 so the deterministic split is unambiguous.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var bigger = new Sorcery("Cruel Ultimatum", "UUBBBRR") { Owner = _alice };
        // Add a third card on the bottom to assert "other goes to
        // *bottom* of library" (after the existing cards).
        var filler = new Instant("Counterspell", "UU") { Owner = _alice };

        _alice.Zones.Library.AddCard(bolt);       // index 0 — top
        _alice.Zones.Library.AddCard(bigger);     // index 1
        _alice.Zones.Library.AddCard(filler);     // index 2 — bottom

        var karn = KarnScionOfUrzaFactory.Create(_alice);
        var plus1 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        karn.Loyalty.Should().Be(6, "5 + 1 = 6");

        // Higher-mv card (Cruel Ultimatum mv=7) goes to hand.
        _alice.Zones.Hand.GetCards().Should().Contain(bigger);
        bigger.Zone.Should().Be(ZoneType.Hand);

        // Lower-mv card (Bolt mv=1) goes to the bottom of the library,
        // BELOW the existing filler card.
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
        var libCards = _alice.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(2);
        libCards[0].Should().BeSameAs(filler, "filler stays at its original position");
        libCards[1].Should().BeSameAs(bolt, "bolted card sinks to the bottom of the library");
        bolt.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void KarnScionOfUrza_Plus1_SingleCardLibrary_TakesIt()
    {
        var loneCard = new Instant("Lightning Bolt", "R") { Owner = _alice };
        _alice.Zones.Library.AddCard(loneCard);

        var karn = KarnScionOfUrzaFactory.Create(_alice);
        var plus1 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        karn.Loyalty.Should().Be(6);
        _alice.Zones.Hand.GetCards().Should().Contain(loneCard);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void KarnScionOfUrza_Minus2_SpawnsConstructArtifactCreatureToken()
    {
        var karn = KarnScionOfUrzaFactory.Create(_alice);
        var minus2 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        karn.Loyalty.Should().Be(3, "5 - 2 = 3");

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Construct")
            .ToList();
        tokens.Should().HaveCount(1, "the -2 creates a single Construct token");

        var token = tokens[0];
        token.HasType(CardType.Creature).Should().BeTrue();
        token.HasType(CardType.Artifact).Should().BeTrue(
            "the printed text is 'colorless Construct artifact creature token'");
        token.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
        token.Owner.Should().BeSameAs(_alice);
        token.BasePower.Should().Be(0, "the token is a printed 0/0");
        token.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void KarnScionOfUrza_Minus2_ConstructToken_GetsPlusOnePlusOneForEachArtifact()
    {
        // Wire a ContinuousEffectsService so the CDA-style +1/+1 rider
        // attaches. Drop two artifacts on Alice's battlefield BEFORE the
        // -2, then a third AFTER, to assert the count is dynamic at
        // compute time (not snapshotted at token creation).
        var effects = new ContinuousEffectsService();

        var sol = new Artifact("Sol Ring", "1") { Owner = _alice, Controller = _alice };
        var mox = new Artifact("Mox Opal", "0") { Owner = _alice, Controller = _alice };
        _alice.Zones.Battlefield.AddCard(sol);
        _alice.Zones.Battlefield.AddCard(mox);
        sol.SetZone(ZoneType.Battlefield);
        mox.SetZone(ZoneType.Battlefield);

        var karn = KarnScionOfUrzaFactory.Create(_alice, zoneService: null, effects: effects);
        var minus2 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Construct");

        // Karn himself is a Planeswalker — not an Artifact — so he
        // doesn't count. The token itself IS an Artifact (per its
        // Layer 4-equivalent additive type stamp at creation) so the
        // count includes itself: Sol Ring + Mox Opal + Construct token
        // = 3.
        token.GetPower().Should().Be(3,
            "Sol Ring + Mox Opal + the Construct token itself = 3 artifacts");
        token.GetToughness().Should().Be(3);

        // Add a third printed artifact — the token's P/T tracks it.
        // Wire the bystander's ActiveEffects so its zone entry invalidates the
        // layer-system cache (as production does for battlefield permanents).
        var another = new Artifact("Aether Vial", "1") { Owner = _alice, Controller = _alice };
        another.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(another);
        another.SetZone(ZoneType.Battlefield);

        token.GetPower().Should().Be(4);
        token.GetToughness().Should().Be(4);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KarnScionOfUrza()
    {
        var card = NamedCardFactory.Create("Karn, Scion of Urza", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Karn, Scion of Urza");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Karn).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(5);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }
}
