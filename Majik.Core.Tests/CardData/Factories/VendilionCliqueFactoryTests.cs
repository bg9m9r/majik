using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VendilionCliqueFactory"/>.
///
/// Covers:
/// - Identity (Legendary Creature — Faerie Wizard 3/1 at {1}{U}{U}).
/// - Flash + Flying keyword abilities present.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one ETB triggered ability with a 1..1 "target player".
/// - ETB picks a nonland from the target's hand, bottoms it, and draws.
/// - ETB declines cleanly when the target's hand has only lands.
/// - ETB declines cleanly when the target's hand is empty.
/// - Lands in the target's hand are NOT picked.
/// </summary>
[Trait("Color", "U")]
public class VendilionCliqueFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void VendilionClique_Identity()
    {
        var c = VendilionCliqueFactory.Create(_alice);

        c.Name.Should().Be("Vendilion Clique");
        c.ManaCost.Should().Be("{1}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(1);
        c.Supertypes.Should().Contain(CardSupertype.Legendary);
        c.Owner.Should().BeSameAs(_alice);

        // Flash + Flying as KeywordAbility instances.
        var kws = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        kws.Should().Contain("Flash");
        kws.Should().Contain("Flying");

        // Exactly one ETB trigger.
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
    [Fact]
    public void Etb_BottomNonland_AndTargetDraws()
    {
        var clique = VendilionCliqueFactory.Create(_alice);
        clique.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(clique);

        // Bob's hand: one land + one nonland.
        var land = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob);
        goyf.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(goyf);

        // Seed a known card on top of Bob's library so the draw is observable.
        var top = new Creature("Top Bear", "1G", 2, 2);
        top.SetOwner(_bob);
        top.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(top);

        var etb = clique.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        // Goyf bottomed to library, Top Bear drawn.
        goyf.Zone.Should().Be(ZoneType.Library,
            "the chosen nonland card goes to the bottom of the target's library");
        _bob.Zones.Library.GetCards().Should().Contain(goyf);
        _bob.Zones.Library.GetCards().Last().Should().BeSameAs(goyf,
            "AddCard appends — Goyf is the new bottom card");

        top.Zone.Should().Be(ZoneType.Hand,
            "target then draws a card (top of library → hand, CR 121.2)");
        _bob.Zones.Hand.GetCards().Should().Contain(top);
        _bob.Zones.Hand.GetCards().Should().NotContain(goyf,
            "Goyf was bottomed, not left in hand");
        _bob.Zones.Hand.GetCards().Should().Contain(land,
            "lands are not picked by the 'nonland' filter");
    }

    [Fact]
    public void Etb_LandOnlyHand_NoBottomNoDraw()
    {
        var clique = VendilionCliqueFactory.Create(_alice);
        clique.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(clique);

        var land = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var top = new Creature("Top Bear", "1G", 2, 2);
        top.SetOwner(_bob);
        top.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(top);

        var etb = clique.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        land.Zone.Should().Be(ZoneType.Hand,
            "no nonland → nothing to bottom");
        top.Zone.Should().Be(ZoneType.Library,
            "'If you do' gates the draw on the choice; no choice → no draw");
        _bob.Zones.Hand.GetCards().Should().Contain(land);
        _bob.Zones.Hand.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void Etb_EmptyHand_NoBottomNoDraw()
    {
        var clique = VendilionCliqueFactory.Create(_alice);
        clique.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(clique);

        var top = new Creature("Top Bear", "1G", 2, 2);
        top.SetOwner(_bob);
        top.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(top);

        var etb = clique.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        top.Zone.Should().Be(ZoneType.Library,
            "empty hand → no choice; 'If you do' gates the draw, so no draw");
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
