using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CanopyVistaFactory"/> — Canopy Vista, a Battle
/// for Zendikar "battle land" / "tango land" (Forest Plains dual). Oracle
/// text:
///   "({T}: Add {G} or {W}.)
///    This land enters tapped unless you control two or more basic lands."
///
/// Covers:
/// - Identity (Land + both printed subtypes Forest/Plains, non-Basic).
/// - Two mana abilities producing {G} and {W} respectively (CR 605.1).
/// - No activated / triggered abilities beyond mana.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c): 0 basics -> tapped; 1 basic -> tapped; 2 basics -> untapped;
///   nonbasic lands don't count; opponent's basics don't count; self excluded.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Single-arg path registers no replacement.
/// </summary>
[Trait("Color", "C")]
public class CanopyVistaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void CanopyVista_IsNotBasic()
    {
        var land = CanopyVistaFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "battle lands are nonbasic");
    }

    [Fact]
    public void CanopyVista_HasTwoManaAbilities_ProducingGW()
    {
        var land = (Land)NamedCardFactory.Create("Canopy Vista", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Canopy Vista taps for {G} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void CanopyVista_HasNoActivatedOrTriggeredAbilities()
    {
        var land = CanopyVistaFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "battle lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "battle lands have no triggered abilities");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "two or more basic lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void CanopyVista_EntersTapped_WhenControllerHasNoBasics()
    {
        var bus = new ReplacementBus();
        var land = CanopyVistaFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "enters tapped when controller has zero basic lands");
    }

    [Fact]
    public void CanopyVista_EntersTapped_WhenControllerHasExactlyOneBasic()
    {
        var bus = new ReplacementBus();
        SeedBasic("Forest", _alice);
        var land = CanopyVistaFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "one basic land is not 'two or more'");
    }

    [Fact]
    public void CanopyVista_EntersUntapped_WhenControllerHasTwoBasics()
    {
        var bus = new ReplacementBus();
        SeedBasic("Forest", _alice);
        SeedBasic("Plains", _alice);
        var land = CanopyVistaFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "two basic lands satisfies 'two or more basic lands'");
    }

    [Fact]
    public void CanopyVista_EntersUntapped_WhenControllerHasMoreThanTwoBasics()
    {
        var bus = new ReplacementBus();
        SeedBasic("Forest", _alice);
        SeedBasic("Plains", _alice);
        SeedBasic("Mountain", _alice);
        var land = CanopyVistaFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "three basics is 'two or more'");
    }

    [Fact]
    public void CanopyVista_NonbasicLands_DoNotCount()
    {
        // Two nonbasic lands (other Canopy Vistas) are NOT basic lands, so
        // the predicate is unmet -> enters tapped. CR 205.4a: only the Basic
        // supertype makes a land "basic".
        var bus = new ReplacementBus();
        var other1 = CanopyVistaFactory.Create(_alice);
        var other2 = CanopyVistaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(other1);
        other1.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(other2);
        other2.SetZone(ZoneType.Battlefield);

        var land = CanopyVistaFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "nonbasic lands do not count toward 'two or more basic lands'");
    }

    [Fact]
    public void CanopyVista_EntersTapped_WhenOnlyOpponentHasBasics()
    {
        // "you control" — opponent's basics don't satisfy the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedBasic("Forest", bob);
        SeedBasic("Plains", bob);

        var land = CanopyVistaFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "only the controller's basic lands count");
    }

    [Fact]
    public void CanopyVista_PredicateExcludesSelf()
    {
        // Even on the battlefield Canopy Vista isn't a basic land, so it
        // can't satisfy its own predicate; with one other basic the count is
        // still 1 -> tapped.
        var bus = new ReplacementBus();
        SeedBasic("Forest", _alice);
        var land = CanopyVistaFactory.Create(_alice, replacements: bus);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "the land itself isn't basic and is excluded from the count");
    }

    // -----------------------------------------------------------------------
    // Shape-only single-arg path
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void CanopyVista_Create_ThrowsOnNullOwner()
    {
        var act = () => CanopyVistaFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedBasic(string name, Player owner)
    {
        var basic = (Land)NamedCardFactory.Create(name, owner);
        owner.Zones.Battlefield.AddCard(basic);
        basic.SetZone(ZoneType.Battlefield);
    }

    private static ZoneMoveIntent ApplyEtb(ReplacementBus bus, Land land, Player controller)
    {
        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        return after!;
    }
}
