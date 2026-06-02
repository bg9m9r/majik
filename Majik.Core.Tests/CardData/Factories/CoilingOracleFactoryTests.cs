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
/// Unit tests for <see cref="CoilingOracleFactory"/>
/// (Ravnica: City of Guilds, {G}{U}).
///
/// Covers:
/// - Identity ({G}{U} Creature — Snake Elf Druid, 1/1, green + blue).
/// - Mana value 2 (CR 202.3).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one battlefield-active ETB triggered ability (no intervening-if).
/// - ETB with land on top → land goes to battlefield under controller (CR 305.1).
/// - ETB with non-land on top → card goes to hand.
/// - ETB with empty library → no-op (no crash, no zone moves).
/// </summary>
[Trait("Color", "M")]
public class CoilingOracleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CoilingOracle_Identity()
    {
        var c = CoilingOracleFactory.Create(_alice);

        c.Name.Should().Be("Coiling Oracle");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue("Coiling Oracle is a Snake");
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue("Coiling Oracle is an Elf");
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue("Coiling Oracle is a Druid");
        c.ManaCost.Should().Be("{G}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CoilingOracle_IsGreenAndBlue()
    {
        var c = CoilingOracleFactory.Create(_alice);

        var colors = Majik.Core.Cards.CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green,
            "Coiling Oracle has a {G} pip in its mana cost");
        colors.Should().Contain(ManaColor.Blue,
            "Coiling Oracle has a {U} pip in its mana cost");
        colors.Should().HaveCount(2, "exactly two color identities: green and blue");
    }

    [Fact]
    public void CoilingOracle_ManaValue_IsTwo()
    {
        var c = CoilingOracleFactory.Create(_alice);

        // {G}{U} = mana value 2 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(2, "CR 202.3 — {G}{U} has mana value 2");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CoilingOracle_HasExactlyOneTriggeredAbility_BattlefieldActive_NoInterveningIf()
    {
        var c = CoilingOracleFactory.Create(_alice);

        var triggerList = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggerList.Should().HaveCount(1, "exactly one ETB trigger");

        var etb = triggerList.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull(
            "no intervening-if — the reveal is unconditional on ETB (CR 603.4 does not apply)");
    }

    // -----------------------------------------------------------------------
    // ETB resolve — land on top → battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void CoilingOracle_Etb_LandOnTop_GoesToBattlefield()
    {
        var alice = new Player("Alice", 20);
        var forest = new Land("Forest");
        forest.SetOwner(alice);
        alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var oracle = CoilingOracleFactory.Create(alice);
        var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        forest.Zone.Should().Be(ZoneType.Battlefield,
            "land card on top → put onto the battlefield (CR 305.1)");
        alice.Zones.Battlefield.GetCards().Should().Contain(forest,
            "battlefield zone collection should hold the land");
        alice.Zones.Library.GetCards().Should().BeEmpty(
            "the single library card was moved to the battlefield");
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "land went to battlefield, not hand");
    }

    [Fact]
    public void CoilingOracle_Etb_Land_Controller_IsSet()
    {
        var alice = new Player("Alice", 20);
        var island = new Land("Island");
        island.SetOwner(alice);
        alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var oracle = CoilingOracleFactory.Create(alice);
        var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        island.Controller.Should().BeSameAs(alice,
            "land entering the battlefield is under the controller's control (CR 110.2a)");
    }

    // -----------------------------------------------------------------------
    // ETB resolve — non-land on top → hand
    // -----------------------------------------------------------------------

    [Fact]
    public void CoilingOracle_Etb_NonLandOnTop_GoesToHand()
    {
        var alice = new Player("Alice", 20);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var oracle = CoilingOracleFactory.Create(alice);
        var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        bolt.Zone.Should().Be(ZoneType.Hand,
            "non-land card → put into hand");
        alice.Zones.Hand.GetCards().Should().Contain(bolt,
            "hand zone collection should hold the revealed non-land card");
        alice.Zones.Library.GetCards().Should().BeEmpty(
            "the single library card was moved to hand");
        alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "no land entered the battlefield");
    }

    [Fact]
    public void CoilingOracle_Etb_NonLandCreatureOnTop_GoesToHand()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var oracle = CoilingOracleFactory.Create(alice);
        var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Hand,
            "a creature is not a land card — it goes to hand");
        alice.Zones.Hand.GetCards().Should().Contain(bear);
    }

    // -----------------------------------------------------------------------
    // ETB resolve — empty library → no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void CoilingOracle_Etb_EmptyLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        // Library is intentionally empty.

        var oracle = CoilingOracleFactory.Create(alice);
        var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow(
            "empty library is a legal no-op per CR 701.16 — nothing to reveal");
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
