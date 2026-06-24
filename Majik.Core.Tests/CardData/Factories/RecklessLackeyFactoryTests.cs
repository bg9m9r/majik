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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Reckless Lackey (Outlaws of Thunder Junction, {R}, Creature —
/// Goblin Pirate 1/2). Oracle text (verified against Scryfall):
///   "First strike, haste
///    {2}{R}, Sacrifice this creature: Draw a card and create a Treasure
///    token. (It's an artifact with "{T}, Sacrifice this token: Add one
///    mana of any color.")"
///
/// Covers ONLY the card's unique behaviour (the contract test already
/// asserts dispatch + well-formedness):
///   - Identity: {R}, Goblin Pirate 1/2, intrinsic First strike + Haste.
///   - The activated ability cost shape: {2}{R} mana + Sacrifice this.
///   - Resolve: the controller draws a card and gets a Treasure token.
/// </summary>
[Trait("Color", "R")]
public class RecklessLackeyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        ICard c = new Card(name, "R");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return (Card)c;
    }

    // -----------------------------------------------------------------------
    // Identity — {R}, Goblin Pirate 1/2, First strike + Haste.
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_GoblinPirate_1_2_AtCostR_FirstStrikeHaste()
    {
        var card = RecklessLackeyFactory.Create(_alice);

        card.Name.Should().Be("Reckless Lackey");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(2);

        var keywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("First strike");
        keywords.Should().Contain("Haste");

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Activated ability: {2}{R}, Sacrifice this creature.
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_SacForDrawAndTreasure_HasManaAndSacCost()
    {
        var card = RecklessLackeyFactory.Create(_alice);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        // No timing rider — instant speed (CR 602.2).
        ability.IsSorcerySpeed.Should().BeFalse();
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        ability.Costs.OfType<SacrificeSelfCost>().Should().ContainSingle();
    }

    [Fact]
    public void ActivatedAbility_Resolve_DrawsACard_AndCreatesTreasure()
    {
        var card = RecklessLackeyFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Seed the library so the draw is observable (and does not flag the
        // empty-library SBA loss).
        var top = NewCardInLibrary(_alice, "Mountain");

        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // CR 121.1 — drew the top card.
        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1);

        // CR 111.10 — a colourless Treasure artifact token under Alice's control.
        var treasure = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .FirstOrDefault(a => a.IsToken && a.Name == "Treasure");

        treasure.Should().NotBeNull("the ability mints one Treasure token");
        treasure!.HasSubtype(CardSubtype.Treasure).Should().BeTrue();
        CardColors.GetColors(treasure).Should().BeEmpty("Treasure is colourless");
    }
}
