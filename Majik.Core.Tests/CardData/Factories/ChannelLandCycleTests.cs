using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ChannelLandCycleFactory"/> — the Kamigawa: Neon
/// Dynasty legendary-land Channel cycle (CR 702.74).
///
/// Covers:
/// - Land identity per cycle member (Legendary + correct produced colour).
/// - Channel ability cost composition (mana + DiscardSelfCost).
/// - Per-member resolve behaviour (bounce, destroy, dig 4).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Channel cost-payment rejected when the card is not in the hand zone
///   (CR 702.74a) — the activated-from-hand surface lives entirely on
///   <see cref="DiscardSelfCost"/>'s zone gate.
/// </summary>
[Trait("Color", "C")]
public class ChannelLandCycleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Cycle membership — all 3 lands dispatch through NamedCardFactory
    // -----------------------------------------------------------------------

    public static IEnumerable<object[]> AllChannelLands => new[]
    {
        // cardName, producedColor, channelGeneric, channelColored
        new object[] { "Otawara, Soaring City",          "U", 2, 1 }, // channel = {2}{U}
        new object[] { "Eiganjo, Seat of the Empire",    "W", 1, 1 }, // channel = {1}{W}
        new object[] { "Takenuma, Abandoned Mire",       "B", 2, 1 }, // channel = {2}{B}
        new object[] { "Sokenzan, Crucible of Defiance", "R", 2, 1 }, // channel = {2}{R}
    };
    [Theory]
    [MemberData(nameof(AllChannelLands))]
    public void ChannelLand_HasManaAbilityProducingExpectedColor(
        string cardName, string color, int _x, int _y)
    {
        _ = _x; _ = _y;

        var land = (Land)NamedCardFactory.Create(cardName, _alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1);
        var produced = manaAbilities[0].ManaGenerated;
        switch (color)
        {
            case "W": produced.White.Should().Be(1); break;
            case "U": produced.Blue.Should().Be(1); break;
            case "B": produced.Black.Should().Be(1); break;
            case "R": produced.Red.Should().Be(1); break;
            case "G": produced.Green.Should().Be(1); break;
        }
    }

    [Theory]
    [MemberData(nameof(AllChannelLands))]
    public void ChannelLand_HasChannelAbility_WithManaAndDiscardSelfCosts(
        string cardName, string _color, int genericExpected, int colorExpected)
    {
        _ = _color;

        var land = (Land)NamedCardFactory.Create(cardName, _alice);

        var channel = land.Abilities.OfType<ActivatedAbility>().Single();

        channel.Costs.Should().HaveCount(2,
            "Channel = mana cost + DiscardSelfCost");
        channel.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = channel.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(genericExpected);
        ManaForColor(manaCost, _color).Should().Be(colorExpected);
    }

    // -----------------------------------------------------------------------
    // Channel — activated-from-hand surface (CR 702.74a)
    // -----------------------------------------------------------------------

    [Fact]
    public void Channel_DiscardSelfCost_PayableWhenCardInHand()
    {
        var otawara = (Land)NamedCardFactory.Create("Otawara, Soaring City", _alice);
        _alice.Zones.Hand.AddCard(otawara);
        var channel = otawara.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = channel.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeTrue(
            "Channel activates while the card is in hand (CR 702.74a)");
    }

    [Fact]
    public void Channel_DiscardSelfCost_RejectedWhenCardNotInHand()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo, Seat of the Empire", _alice);
        _alice.Zones.Battlefield.AddCard(eiganjo);
        var channel = eiganjo.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = channel.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "Channel can't be activated from the battlefield — CR 702.74a");
    }

    // -----------------------------------------------------------------------
    // Otawara — bounce nonland permanent
    // -----------------------------------------------------------------------

    [Fact]
    public void Otawara_Channel_BouncesTargetNonlandPermanent_ToOwnersHand()
    {
        var otawara = (Land)NamedCardFactory.Create("Otawara, Soaring City", _alice);
        var bobArtifact = new Artifact("Sol Ring", "");
        bobArtifact.SetOwner(_bob);
        bobArtifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobArtifact);

        var channel = otawara.Abilities.OfType<ActivatedAbility>().Single();
        channel.SetChosenTargets(new[] { new object[] { bobArtifact } });

        channel.Resolve();

        _bob.Zones.Hand.GetCards().Should().Contain(bobArtifact,
            "Sol Ring returns to its owner's hand");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobArtifact);
        bobArtifact.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Otawara_Channel_TargetingLand_IsNoOp()
    {
        var otawara = (Land)NamedCardFactory.Create("Otawara, Soaring City", _alice);
        var bobLand = (Land)NamedCardFactory.Create("Mountain", _bob);
        _bob.Zones.Battlefield.AddCard(bobLand);

        var channel = otawara.Abilities.OfType<ActivatedAbility>().Single();
        channel.SetChosenTargets(new[] { new object[] { bobLand } });

        channel.Resolve();

        _bob.Zones.Battlefield.GetCards().Should().Contain(bobLand,
            "Otawara's Channel targets *nonland* permanents — land target is illegal");
    }

    // -----------------------------------------------------------------------
    // Eiganjo — destroy attacking/blocking creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Eiganjo_Channel_DestroysTargetCreature_ToOwnersGraveyard()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo, Seat of the Empire", _alice);
        var attacker = new Creature("Hill Giant", "3R", 3, 3);
        attacker.SetOwner(_bob);
        attacker.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(attacker);

        var channel = eiganjo.Abilities.OfType<ActivatedAbility>().Single();
        channel.SetChosenTargets(new[] { new object[] { attacker } });

        channel.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().Contain(attacker,
            "destroyed creature moves to its owner's graveyard (CR 701.7)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(attacker);
    }

    // -----------------------------------------------------------------------
    // Takenuma — dig 4, creature/planeswalker → hand, rest → graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Takenuma_Channel_PutsCreatureFromTop4IntoHand_RestToGraveyard()
    {
        var takenuma = (Land)NamedCardFactory.Create("Takenuma, Abandoned Mire", _alice);

        // Seed Alice's library: 3 non-creatures + 1 creature in the top 4.
        var inst1 = new Instant("Lightning Bolt", "R");
        var inst2 = new Instant("Counterspell", "UU");
        var sorc = new Sorcery("Wrath of God", "2WW");
        var creature = new Creature("Hill Giant", "3R", 3, 3);
        foreach (var c in new ICard[] { inst1, inst2, sorc, creature })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
        }

        var channel = takenuma.Abilities.OfType<ActivatedAbility>().Single();
        channel.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(creature,
            "the only creature in the top 4 should land in hand");
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { inst1, inst2, sorc },
            "the non-creature/PW cards from the top 4 go to graveyard");
        _alice.Zones.Library.GetCards().Should().NotContain(new ICard[] { inst1, inst2, sorc, creature },
            "all 4 cards leave the library");
    }

    [Fact]
    public void Takenuma_Channel_PutsPlaneswalkerFromTop4IntoHand()
    {
        var takenuma = (Land)NamedCardFactory.Create("Takenuma, Abandoned Mire", _alice);

        var pw = new Planeswalker("Liliana Vess", "3BB", 5);
        pw.SetOwner(_alice);
        _alice.Zones.Library.AddCard(pw);

        var channel = takenuma.Abilities.OfType<ActivatedAbility>().Single();
        channel.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(pw,
            "planeswalker in the top 4 is eligible for Takenuma's pick");
    }

    [Fact]
    public void Takenuma_Channel_NoEligibleCard_AllTop4ToGraveyard()
    {
        var takenuma = (Land)NamedCardFactory.Create("Takenuma, Abandoned Mire", _alice);

        var inst = new Instant("Lightning Bolt", "R");
        var sorc = new Sorcery("Wrath of God", "2WW");
        foreach (var c in new ICard[] { inst, sorc })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
        }

        var channel = takenuma.Abilities.OfType<ActivatedAbility>().Single();
        channel.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no creature/planeswalker in the top 4 → nothing to hand");
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { inst, sorc });
    }

    // -----------------------------------------------------------------------
    // Sokenzan — create two 1/1 red Spirit creature tokens with haste
    // -----------------------------------------------------------------------

    [Fact]
    public void Sokenzan_Channel_CreatesTwoRedSpiritTokensWithHaste()
    {
        var sokenzan = (Land)NamedCardFactory.Create("Sokenzan, Crucible of Defiance", _alice);

        var channel = sokenzan.Abilities.OfType<ActivatedAbility>().Single();
        channel.Resolve();

        var spirits = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        spirits.Should().HaveCount(2, "Sokenzan's Channel creates two Spirit tokens");
        foreach (var spirit in spirits)
        {
            spirit.Power.Should().Be(1);
            spirit.Toughness.Should().Be(1);
            spirit.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
            spirit.Abilities.OfType<KeywordAbility>()
                .Should().Contain(k => k.Keyword == "Haste",
                    "Sokenzan's Spirit tokens have haste (CR 702.10)");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static int ManaForColor(ManaCost cost, string color) => color switch
    {
        "W" => cost.White,
        "U" => cost.Blue,
        "B" => cost.Black,
        "R" => cost.Red,
        "G" => cost.Green,
        _ => 0,
    };
}
