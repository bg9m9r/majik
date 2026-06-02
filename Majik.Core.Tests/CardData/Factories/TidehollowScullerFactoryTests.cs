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
/// Unit tests for <see cref="TidehollowScullerFactory"/>.
///
/// Tidehollow Sculler (Shards of Alara, {W}{B}) — Artifact Creature —
/// Zombie 2/2. Oracle text (verified against Scryfall):
///   "When this creature enters, target opponent reveals their hand and
///    you choose a nonland card from it. Exile that card.
///    When this creature leaves the battlefield, return the exiled card
///    to its owner's hand."
///
/// Shares the ETB-exile / LTB-return pair with <see cref="BrainMaggotFactory"/>
/// (Tidehollow Sculler's big brother). Base shape loads from the embedded
/// JSON; the two triggered abilities are layered on in the factory because
/// the JSON ability schema doesn't express exile/return closures.
///
/// Covers:
/// - Identity (Artifact Creature — Zombie 2/2 at {W}{B}; owner/controller).
/// - Multi-type stamping (both Creature and Artifact types).
/// - NamedCardFactory dispatch.
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a nonland card from a target opponent's hand.
/// - ETB skips lands ("nonland").
/// - ETB with land-only / empty hand no-ops.
/// - LTB returns the exiled card to its owner's hand.
/// - LTB without an exiled card no-ops.
/// </summary>
[Trait("Color", "M")]
public class TidehollowScullerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void TidehollowSculler_Identity()
    {
        var c = TidehollowScullerFactory.Create(_alice);

        c.Name.Should().Be("Tidehollow Sculler");
        c.ManaCost.Should().Be("{W}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Tidehollow Sculler is an Artifact Creature (CR 301.1 / 302.1)");
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }
    [Fact]
    public void TidehollowSculler_Etb_ExilesNonlandFromOpponentHand()
    {
        var sculler = TidehollowScullerFactory.Create(_alice);
        sculler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sculler);

        var land = new Land("Swamp");
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var spell = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        var etb = sculler.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        spell.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles a nonland card from the target opponent's hand (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(spell);
        _bob.Zones.Hand.GetCards().Should().NotContain(spell);
        _bob.Zones.Hand.GetCards().Should().Contain(land,
            "lands are skipped by the printed 'nonland' filter");
    }

    [Fact]
    public void TidehollowSculler_Etb_LandOnlyHand_NoExile()
    {
        var sculler = TidehollowScullerFactory.Create(_alice);
        sculler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sculler);

        var land = new Land("Swamp");
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var etb = sculler.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        land.Zone.Should().Be(ZoneType.Hand,
            "no nonland card in hand → no exile (CR 701.21 — printed 'nonland' filter)");
        _bob.Zones.Exile.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void TidehollowSculler_Etb_EmptyHand_NoExile()
    {
        var sculler = TidehollowScullerFactory.Create(_alice);
        sculler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sculler);

        var etb = sculler.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in etb.Effects) e.Execute();

        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void TidehollowSculler_Ltb_ReturnsExiledCardToOwnersHand()
    {
        var sculler = TidehollowScullerFactory.Create(_alice);
        sculler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sculler);

        var spell = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        var etb = sculler.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();
        spell.Zone.Should().Be(ZoneType.Exile);

        var ltb = sculler.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        spell.Zone.Should().Be(ZoneType.Hand,
            "LTB returns the exiled card to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(spell);
        _bob.Zones.Exile.GetCards().Should().NotContain(spell);
    }

    [Fact]
    public void TidehollowSculler_Ltb_WithoutExile_NoOp()
    {
        var sculler = TidehollowScullerFactory.Create(_alice);
        sculler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sculler);

        var ltb = sculler.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
