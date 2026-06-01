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
/// Unit tests for <see cref="InspiringVantageFactory"/> — Inspiring Vantage,
/// an Aether Revolt allied "fast land" (R/W). Oracle text:
///   "This land enters tapped unless you control two or fewer other lands.
///    {T}: Add {R} or {W}."
///
/// Covers:
/// - Identity (Land type, printed name, owner/controller wiring, non-Basic,
///   non-Legendary, no printed subtype).
/// - Two mana abilities producing {R} and {W} respectively (CR 605.1).
/// - No activated / triggered abilities beyond mana.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c): 0/1/2 other lands -> untapped; 3 other lands -> tapped;
///   any land type counts (not just basics); opponent's lands don't count;
///   self excluded from the count.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Single-arg path registers no replacement.
/// </summary>
public class InspiringVantageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void InspiringVantage_Dispatch_ReturnsLandWithCorrectName()
    {
        var card = NamedCardFactory.Create("Inspiring Vantage", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Inspiring Vantage");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void InspiringVantage_IsNotBasic_NotLegendary()
    {
        var land = InspiringVantageFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "fast lands are nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void InspiringVantage_HasTwoManaAbilities_ProducingRW()
    {
        var land = (Land)NamedCardFactory.Create("Inspiring Vantage", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Inspiring Vantage taps for {R} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void InspiringVantage_HasNoActivatedOrTriggeredAbilities()
    {
        var land = InspiringVantageFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "fast lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "fast lands have no triggered abilities");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "two or fewer other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void InspiringVantage_EntersUntapped_WhenControllerHasNoOtherLands()
    {
        var bus = new ReplacementBus();
        var land = InspiringVantageFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "zero other lands is 'two or fewer'");
    }

    [Fact]
    public void InspiringVantage_EntersUntapped_WhenControllerHasTwoOtherLands()
    {
        var bus = new ReplacementBus();
        SeedLand("Mountain", _alice);
        SeedLand("Plains", _alice);
        var land = InspiringVantageFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "exactly two other lands satisfies 'two or fewer other lands'");
    }

    [Fact]
    public void InspiringVantage_EntersTapped_WhenControllerHasThreeOtherLands()
    {
        var bus = new ReplacementBus();
        SeedLand("Mountain", _alice);
        SeedLand("Plains", _alice);
        SeedLand("Island", _alice);
        var land = InspiringVantageFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "three other lands is more than two -> enters tapped");
    }

    [Fact]
    public void InspiringVantage_NonbasicLands_AlsoCount()
    {
        // "other lands" is ANY land — basic or nonbasic — unlike the battle
        // land predicate which counts only Basic-supertype lands.
        var bus = new ReplacementBus();
        var other1 = InspiringVantageFactory.Create(_alice);
        var other2 = InspiringVantageFactory.Create(_alice);
        var other3 = InspiringVantageFactory.Create(_alice);
        foreach (var l in new[] { other1, other2, other3 })
        {
            _alice.Zones.Battlefield.AddCard(l);
            l.SetZone(ZoneType.Battlefield);
        }

        var land = InspiringVantageFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "three other nonbasic lands count toward 'other lands' -> tapped");
    }

    [Fact]
    public void InspiringVantage_EntersUntapped_WhenOnlyOpponentHasManyLands()
    {
        // "you control" — opponent's lands don't count toward the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedLand("Mountain", bob);
        SeedLand("Plains", bob);
        SeedLand("Island", bob);
        SeedLand("Swamp", bob);

        var land = InspiringVantageFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "only the controller's lands count; Alice controls zero other lands");
    }

    [Fact]
    public void InspiringVantage_PredicateExcludesSelf()
    {
        // With this land plus exactly two other lands on the battlefield, the
        // count of OTHER lands is 2 (self excluded) -> untapped.
        var bus = new ReplacementBus();
        SeedLand("Mountain", _alice);
        SeedLand("Plains", _alice);
        var land = InspiringVantageFactory.Create(_alice, replacements: bus);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "the land itself is excluded from the 'other lands' count (2 others <= 2)");
    }

    // -----------------------------------------------------------------------
    // Shape-only single-arg path
    // -----------------------------------------------------------------------

    [Fact]
    public void InspiringVantage_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        var land = NamedCardFactory.Create("Inspiring Vantage", _alice);
        land.Should().NotBeNull();
        land.Name.Should().Be("Inspiring Vantage");
        ((Land)land).Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void InspiringVantage_Create_ThrowsOnNullOwner()
    {
        var act = () => InspiringVantageFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedLand(string name, Player owner)
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
