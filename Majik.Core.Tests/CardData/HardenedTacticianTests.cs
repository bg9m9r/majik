using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HardenedTacticianFactory"/> — Creature — Human
/// Warrior, {1}{W}{B}, 2/4.
///
/// Oracle text (verified against Scryfall):
///   "{1}, Sacrifice a token: Draw a card."
///
/// Covers ONLY the card's unique behaviour (the activated ability) plus a
/// single identity assert (exact mana cost / P-T / subtypes / colours). The
/// dispatch + well-formedness checks are owned by
/// <c>CardFactoryContractTests</c>, so no NamedCardFactory-dispatch test lives
/// here.
///
/// Multicolour (white + black) → sharded as [Trait("Color", "M")].
/// </summary>
[Trait("Color", "M")]
public class HardenedTacticianTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity — {1}{W}{B}, 2/4, Human Warrior, white + black.
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_ManaCost_PowerToughness_Subtypes_Colours()
    {
        var card = HardenedTacticianFactory.Create(_alice);

        card.Name.Should().Be("Hardened Tactician");
        card.ManaCost.Should().Be("{1}{W}{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(4);
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();

        CardColors.GetColors(card).Should().BeEquivalentTo(
            new[] { ManaColor.White, ManaColor.Black },
            "the {W} and {B} pips make it white + black");
    }

    // -----------------------------------------------------------------------
    // {1}, Sacrifice a token: Draw a card. — cost stack.
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawAbility_CostStack_Is_1Generic_SacToken()
    {
        var card = HardenedTacticianFactory.Create(_alice);

        var ability = DrawAbility(card);
        ability.Costs.Should().HaveCount(2);

        ability.Costs.OfType<ManaCostCost>().Single().Cost.Generic.Should().Be(1,
            "the activation cost includes {1}");

        ability.Costs.OfType<SacrificeFilteredCost>().Should().ContainSingle(
            "the activation cost includes 'Sacrifice a token' (CR 111.8 / 701.16)");
    }

    // -----------------------------------------------------------------------
    // The sacrifice-a-token cost only accepts a token (CR 111.8 / 701.16).
    // -----------------------------------------------------------------------

    [Fact]
    public void SacToken_Cost_RequiresAToken_NontokenIsNotEligible()
    {
        var card = HardenedTacticianFactory.Create(_alice);
        var sac = DrawAbility(card).Costs.OfType<SacrificeFilteredCost>().Single();

        // A nontoken creature on the battlefield is NOT a legal sacrifice.
        var nontoken = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, nontoken);
        sac.CanPay(_alice).Should().BeFalse("no token is controlled");

        // A token creature IS a legal sacrifice; paying moves it to graveyard.
        var token = new Creature("Soldier", "", 1, 1);
        token.MarkAsToken();
        PutOnBattlefield(_alice, token);

        sac.CanPay(_alice).Should().BeTrue();
        sac.Pay(_alice);
        token.Zone.Should().Be(ZoneType.Graveyard);
        nontoken.Zone.Should().Be(ZoneType.Battlefield,
            "only the token is sacrificed, never the nontoken creature");
    }

    // -----------------------------------------------------------------------
    // Resolve — draws one card for the controller (CR 121.1).
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawAbility_Resolve_DrawsOneCard_ForController()
    {
        var card = HardenedTacticianFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);

        var top = new Card("Plains", "", new[] { CardType.Land });
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        _alice.Zones.Hand.Count.Should().Be(0);

        DrawAbility(card).Resolve();

        _alice.Zones.Hand.Count.Should().Be(1, "draw resolved → +1 card in hand");
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top);
    }

    [Fact]
    public void DrawAbility_Resolve_EmptyLibrary_FlagsSbaLoss_DoesNotThrow()
    {
        var card = HardenedTacticianFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);

        var act = () => DrawAbility(card).Resolve();

        act.Should().NotThrow();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library stamps the SBA loss flag (CR 704.5b)");
    }

    // -----------------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------------

    private static ActivatedAbility DrawAbility(Creature creature) =>
        creature.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<SacrificeFilteredCost>().Any());

    private static void PutOnBattlefield(Player owner, Permanent card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
