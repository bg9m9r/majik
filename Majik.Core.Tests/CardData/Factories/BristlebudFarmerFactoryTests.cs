using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BristlebudFarmerFactory"/> — Creature — Plant Druid
/// {2}{G}{G} 5/5 (Bloomburrow). Oracle text (verified against the embedded
/// Scryfall seed):
///   "Trample
///    When this creature enters, create two Food tokens.
///    Whenever this creature attacks, you may sacrifice a Food. If you do,
///    mill three cards. You may put a permanent card from among them into
///    your hand."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity: 5/5 Plant Druid, {2}{G}{G}, Trample (single *_Identity
///     assert for the non-vanilla stats / keyword).
///   - ETB trigger creates exactly two Food tokens.
///   - Attack trigger with no Food → clean no-op ("if you do" fails).
///   - Attack trigger with a Food → sacrifices it, mills three, and puts a
///     permanent card from among them into hand (rest to graveyard).
///   - Attack trigger: no permanent among the milled three → nothing to hand,
///     all three milled into the graveyard.
/// </summary>
[Trait("Color", "G")]
public class BristlebudFarmerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ───────────────────────────────────────────────────────────────────
    // Identity
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void BristlebudFarmer_IsPlantDruid5_5_Trample_At2GG()
    {
        var card = BristlebudFarmerFactory.Create(_alice);

        card.Name.Should().Be("Bristlebud Farmer");
        card.ManaCost.Should().Be("{2}{G}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Plant).Should().BeTrue();
        card.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        card.Power.Should().Be(5);
        card.Toughness.Should().Be(5);
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "the JSON keyword marker grants Trample (CR 702.19)");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BristlebudFarmer_HasTwoTriggeredAbilities()
    {
        var card = BristlebudFarmerFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "one ETB-two-Food trigger and one attack mill-and-recur trigger.");
    }

    // ───────────────────────────────────────────────────────────────────
    // ETB — create two Food tokens
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EtbTrigger_CreatesTwoFoodTokens()
    {
        var card = BristlebudFarmerFactory.Create(_alice);
        card.SetController(_alice);
        var etb = card.Abilities.OfType<TriggeredAbility>().First();

        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Artifact) && c.HasSubtype(CardSubtype.Food))
            .Should().Be(2, "the ETB trigger creates two Food tokens (CR 111.10).");
    }

    // ───────────────────────────────────────────────────────────────────
    // Attack — may sacrifice a Food → mill three → may recur a permanent
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void AttackTrigger_NoFood_IsCleanNoOp()
    {
        // No Food in play → "if you do" fails: no sacrifice, no mill.
        var i1 = SeedLibraryCard(new Instant("Lightning Bolt", "{R}"));
        var i2 = SeedLibraryCard(new Instant("Shock", "{R}"));
        var i3 = SeedLibraryCard(new Sorcery("Doom Blade", "{1}{B}"));

        ResolveAttack();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "with no Food to sacrifice, the mill-and-recur never happens.");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "no cards are milled when the Food sacrifice can't be paid.");
        _alice.Zones.Library.GetCards()
            .Should().Contain(new Card[] { i1, i2, i3 }, "the library is untouched.");
    }

    [Fact]
    public void AttackTrigger_WithFood_SacrificesIt_Mills3_RecursPermanent()
    {
        // A Food to pay the sacrifice cost.
        var food = TokenFactory.CreateFood(_alice);
        food.HasSubtype(CardSubtype.Food).Should().BeTrue();

        // Top three: 2 nonpermanent + 1 permanent (a creature). The permanent
        // goes to hand; the two nonpermanent cards go to the graveyard.
        var bolt = SeedLibraryCard(new Instant("Lightning Bolt", "{R}"));
        var shock = SeedLibraryCard(new Instant("Shock", "{R}"));
        var bear = SeedLibraryCard(new Creature("Grizzly Bears", "{1}{G}", 2, 2));

        ResolveAttack();

        // Food sacrificed (CR 701.16 — left the battlefield to the graveyard).
        _alice.Zones.Battlefield.GetCards().Should().NotContain(food,
            "the Food was sacrificed to pay the attack trigger.");

        // Permanent card put into hand; the two nonpermanents milled.
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bear, "a permanent card from the milled three goes to hand.");
        bear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new Card[] { bolt, shock },
                "the rest of the milled three go to the graveyard.");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void AttackTrigger_NoPermanentMilled_NothingToHand_AllThreeMilled()
    {
        TokenFactory.CreateFood(_alice);

        var cards = new[] { "A", "B", "C" }
            .Select(n => SeedLibraryCard(new Instant(n, "{R}")))
            .ToList();

        ResolveAttack();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no permanent among the milled three → nothing goes to hand.");
        _alice.Zones.Graveyard.GetCards().Should().Contain(cards,
            "with no permanent taken, all three milled cards are in the graveyard.");
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private void ResolveAttack()
    {
        var card = BristlebudFarmerFactory.Create(_alice);
        card.SetController(_alice);
        var attack = card.Abilities.OfType<TriggeredAbility>().Last();
        foreach (var e in attack.Effects) e.Execute();
    }

    private T SeedLibraryCard<T>(T card) where T : Card
    {
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }
}
