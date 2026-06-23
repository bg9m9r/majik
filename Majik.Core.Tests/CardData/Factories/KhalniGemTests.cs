using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KhalniGemFactory"/> — Khalni Gem (Rise of the
/// Eldrazi, {4}). Colourless artifact.
///
/// Oracle text (verified against Scryfall):
///   "When this artifact enters, return two lands you control to their
///    owner's hand.
///    {T}: Add two mana of any one color."
///
/// Covers the card's UNIQUE behaviour:
/// - {T}: Add two mana of any one color — five <see cref="ManaAbility"/>
///   instances (one per WUBRG), each producing two mana of that colour
///   (same any-one-colour modelling as Gilded Lotus, scaled to two pips).
/// - ETB trigger returns TWO lands the controller controls to hand
///   (CR 603.6a). Fewer than two lands → returns what it can (CR 608.2b).
/// - Identity (colourless {4} Artifact).
/// </summary>
[Trait("Color", "C")]
public class KhalniGemTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void KhalniGem_Identity()
    {
        var gem = (Artifact)NamedCardFactory.Create("Khalni Gem", _alice);

        gem.Name.Should().Be("Khalni Gem");
        gem.HasType(CardType.Artifact).Should().BeTrue();
        gem.ManaCost.Should().Be("{4}", "printed cost is {4}");
        gem.ManaCostValue.TotalValue.Should().Be(4);
        // Colourless: no coloured pips in the mana cost.
        (gem.ManaCostValue.White + gem.ManaCostValue.Blue + gem.ManaCostValue.Black
            + gem.ManaCostValue.Red + gem.ManaCostValue.Green).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // {T}: Add two mana of any one color
    // -----------------------------------------------------------------------

    [Fact]
    public void KhalniGem_HasFiveManaAbilities_EachProducingTwoOfOneColour()
    {
        var gem = (Artifact)NamedCardFactory.Create("Khalni Gem", _alice);

        var manaAbilities = gem.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5,
            "one mana ability per colour models \"any one color\" (CR 605.1a)");

        foreach (var ability in manaAbilities)
        {
            var m = ability.ManaGenerated;
            var total = m.White + m.Blue + m.Black + m.Red + m.Green;
            total.Should().Be(2, "each tap adds two mana of a single colour");
            // Exactly one colour is non-zero (it's two of ONE colour).
            new[] { m.White, m.Blue, m.Black, m.Red, m.Green }
                .Count(c => c > 0).Should().Be(1, "two mana of any ONE color");
        }

        // The five abilities cover all five colours.
        var colours = manaAbilities.Select(a =>
        {
            var m = a.ManaGenerated;
            return (m.White, m.Blue, m.Black, m.Red, m.Green);
        }).ToList();
        colours.Should().Contain((2, 0, 0, 0, 0));
        colours.Should().Contain((0, 2, 0, 0, 0));
        colours.Should().Contain((0, 0, 2, 0, 0));
        colours.Should().Contain((0, 0, 0, 2, 0));
        colours.Should().Contain((0, 0, 0, 0, 2));
    }

    // -----------------------------------------------------------------------
    // ETB — return TWO lands you control to their owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void KhalniGem_HasEtbReturnTrigger()
    {
        var gem = (Artifact)NamedCardFactory.Create("Khalni Gem", _alice);

        gem.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "single ETB return-two-lands trigger");
    }

    [Fact]
    public void Etb_ReturnsTwoLandsControllerControls_ToOwnersHand()
    {
        var gem = (Artifact)NamedCardFactory.Create("Khalni Gem", _alice);
        var island = (Land)NamedCardFactory.Create("Island", _alice);
        var forest = (Land)NamedCardFactory.Create("Forest", _alice);

        _alice.Zones.Battlefield.AddCard(gem);
        gem.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island);
        island.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var etb = gem.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(island);
        _alice.Zones.Hand.GetCards().Should().Contain(forest);
        island.Zone.Should().Be(ZoneType.Hand);
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Battlefield.GetCards().Should().Contain(gem,
            "the gem itself stays on the battlefield (it is not a land)");
    }

    [Fact]
    public void Etb_WithOnlyOneLand_ReturnsThatOneLand()
    {
        // CR 608.2b — "as much as possible": with a single land available the
        // effect returns that one; the second return has no legal object.
        var gem = (Artifact)NamedCardFactory.Create("Khalni Gem", _alice);
        var swamp = (Land)NamedCardFactory.Create("Swamp", _alice);

        _alice.Zones.Battlefield.AddCard(gem);
        gem.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(swamp);
        swamp.SetZone(ZoneType.Battlefield);

        var etb = gem.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(swamp);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Etb_WithNoLands_IsNoOp()
    {
        var gem = (Artifact)NamedCardFactory.Create("Khalni Gem", _alice);
        _alice.Zones.Battlefield.AddCard(gem);
        gem.SetZone(ZoneType.Battlefield);

        var etb = gem.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no lands to return → nothing happens");
        _alice.Zones.Battlefield.GetCards().Should().Contain(gem);
    }

    [Fact]
    public void Etb_DoesNotReturnOpponentsLand()
    {
        // "Return two lands YOU control" — opponent's lands aren't candidates.
        var bob = new Player("Bob", 20);
        var gem = (Artifact)NamedCardFactory.Create("Khalni Gem", _alice);
        var bobForest = (Land)NamedCardFactory.Create("Forest", bob);

        _alice.Zones.Battlefield.AddCard(gem);
        gem.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobForest);
        bobForest.SetZone(ZoneType.Battlefield);

        var etb = gem.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        bob.Zones.Battlefield.GetCards().Should().Contain(bobForest,
            "opponent's lands aren't eligible for the bounce");
        bob.Zones.Hand.GetCards().Should().NotContain(bobForest);
    }
}
