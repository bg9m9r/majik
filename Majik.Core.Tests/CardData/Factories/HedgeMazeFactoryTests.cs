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
/// Unit tests for <see cref="HedgeMazeFactory"/> (Murders at Karlov Manor
/// "surveil land" dual cycle).
///
/// G/U surveil tapland. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, surveil 1. (Look at the top card of your
///    library. You may put it into your graveyard.)
///    {T}: Add {G} or {U}."
///
/// Type line is <c>Land — Forest Island</c>. The whole shape (identity, dual
/// mana, ETB surveil) loads from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Identity (Land, Forest + Island subtypes, nonbasic, owner/controller).
/// - Two single-colour mana abilities — {G} and {U} (CR 605.1a).
/// - ETB triggered ability (CR 603.6a) that is battlefield-active.
/// - Surveil-1 default decision (CR 701.43) — top card to graveyard.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class HedgeMazeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HedgeMaze_Identity_LandWithForestIslandSubtypes()
    {
        var land = HedgeMazeFactory.Create(_alice);

        land.Name.Should().Be("Hedge Maze");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSubtype(CardSubtype.Forest).Should().BeTrue(
            "Hedge Maze's printed type line is 'Land — Forest Island'");
        land.HasSubtype(CardSubtype.Island).Should().BeTrue(
            "Hedge Maze's printed type line is 'Land — Forest Island'");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Hedge Maze is a nonbasic Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void HedgeMaze_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Hedge Maze", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hedge Maze");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {G} or {U} — two single-colour mana abilities (CR 605.1a)
    // -----------------------------------------------------------------------

    [Fact]
    public void HedgeMaze_HasManaAbility_ForGreen()
    {
        var land = HedgeMazeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void HedgeMaze_HasManaAbility_ForBlue()
    {
        var land = HedgeMazeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Green == 0);
    }

    // -----------------------------------------------------------------------
    // ETB surveil 1 (CR 603.6a + CR 701.43)
    // -----------------------------------------------------------------------

    [Fact]
    public void HedgeMaze_EtbTrigger_IsBattlefieldActive()
    {
        var land = HedgeMazeFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    /// <summary>
    /// CR 701.43 — surveil 1 with no registered agent defaults to putting the
    /// peeked top card into the controller's graveyard (same posture as the
    /// rest of the surveil-land cycle).
    /// </summary>
    [Fact]
    public void HedgeMaze_SurveilEffect_PutsTopCardInGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = HedgeMazeFactory.Create(alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Graveyard);
    }
}
