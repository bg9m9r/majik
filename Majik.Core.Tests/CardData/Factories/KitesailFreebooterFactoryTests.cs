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
/// Unit tests for <see cref="KitesailFreebooterFactory"/>.
///
/// Covers:
/// - Identity (Creature — Human Pirate 1/2 at {1}{B}, owner / controller
///   wired) loaded from the embedded JSON definition.
/// - Flying keyword marker.
/// - NamedCardFactory dispatch.
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a noncreature, nonland card from a target opponent's hand.
/// - ETB skips lands AND creatures (oracle "noncreature, nonland").
/// - ETB with empty target hand no-ops cleanly.
/// - LTB returns the exiled card to its owner's hand.
/// - LTB without an exiled card no-ops.
/// </summary>
[Trait("Color", "B")]
public class KitesailFreebooterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void KitesailFreebooter_Identity()
    {
        var c = KitesailFreebooterFactory.Create(_alice);

        c.Name.Should().Be("Kitesail Freebooter");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "Kitesail Freebooter has Flying (CR 702.9)");

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }
    [Fact]
    public void KitesailFreebooter_Etb_ExilesNoncreatureNonlandFromOpponentHand()
    {
        var pirate = KitesailFreebooterFactory.Create(_alice);
        pirate.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(pirate);

        // Bob's hand: a land, a creature, and a noncreature/nonland spell.
        // ETB should pick the noncreature, nonland spell.
        var land = new Land("Swamp");
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var creature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        creature.SetOwner(_bob);
        creature.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(creature);

        var spell = new Instant("Fatal Push", "{B}");
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        var etb = pirate.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        spell.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles a noncreature, nonland card from the target opponent's hand (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(spell);
        _bob.Zones.Hand.GetCards().Should().NotContain(spell);
        _bob.Zones.Hand.GetCards().Should().Contain(land,
            "lands are skipped by the printed 'nonland' filter");
        _bob.Zones.Hand.GetCards().Should().Contain(creature,
            "creatures are skipped by the printed 'noncreature' filter");
    }

    [Fact]
    public void KitesailFreebooter_Etb_CreatureAndLandOnlyHand_NoExile()
    {
        var pirate = KitesailFreebooterFactory.Create(_alice);
        pirate.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(pirate);

        var land = new Land("Swamp");
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var creature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        creature.SetOwner(_bob);
        creature.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(creature);

        var etb = pirate.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        land.Zone.Should().Be(ZoneType.Hand,
            "no noncreature, nonland card in hand → no exile (CR 701.21)");
        creature.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void KitesailFreebooter_Etb_EmptyHand_NoExile()
    {
        var pirate = KitesailFreebooterFactory.Create(_alice);
        pirate.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(pirate);

        var etb = pirate.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in etb.Effects) e.Execute();

        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void KitesailFreebooter_Ltb_ReturnsExiledCardToOwnersHand()
    {
        var pirate = KitesailFreebooterFactory.Create(_alice);
        pirate.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(pirate);

        var spell = new Instant("Fatal Push", "{B}");
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        var etb = pirate.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();
        spell.Zone.Should().Be(ZoneType.Exile);

        // LTB — Freebooter leaves the battlefield.
        var ltb = pirate.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        spell.Zone.Should().Be(ZoneType.Hand,
            "LTB returns the exiled card to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(spell);
        _bob.Zones.Exile.GetCards().Should().NotContain(spell);
    }

    [Fact]
    public void KitesailFreebooter_Ltb_WithoutExile_NoOp()
    {
        var pirate = KitesailFreebooterFactory.Create(_alice);
        pirate.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(pirate);

        var ltb = pirate.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
