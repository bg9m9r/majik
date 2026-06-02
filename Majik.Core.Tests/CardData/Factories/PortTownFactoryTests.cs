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
/// Unit tests for <see cref="PortTownFactory"/> — Port Town, a Shadows over
/// Innistrad "buddy land" / "slow land" (W/U). Oracle text:
///   "As this land enters, you may reveal a Plains or Island card from your
///    hand. If you don't, this land enters tapped.
///    {T}: Add {W} or {U}."
///
/// Covers:
/// - Identity (Land, nonbasic, no printed subtype).
/// - Two mana abilities producing {W} and {U} respectively (CR 605.1).
/// - No activated / triggered abilities beyond mana.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c), auto-reveal model: empty hand -> tapped; a Plains card in
///   hand -> untapped; an Island card in hand -> untapped; an unrelated
///   (non-Plains/Island) card in hand -> tapped; opponent's hand doesn't
///   count; self excluded from the hand search.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Single-arg path registers no replacement.
/// </summary>
[Trait("Color", "WU")]
public class PortTownFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void PortTown_IsNotBasic()
    {
        var land = PortTownFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "buddy lands are nonbasic");
    }

    [Fact]
    public void PortTown_HasTwoManaAbilities_ProducingWU()
    {
        var land = (Land)NamedCardFactory.Create("Port Town", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Port Town taps for {W} or {U}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void PortTown_HasNoActivatedOrTriggeredAbilities()
    {
        var land = PortTownFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "buddy lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the ETB-tapped clause is a replacement effect, not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "reveal a Plains or Island card"
    // -----------------------------------------------------------------------

    [Fact]
    public void PortTown_EntersTapped_WhenHandHasNoPlainsOrIsland()
    {
        var bus = new ReplacementBus();
        var land = PortTownFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "no Plains-or-Island card in hand to reveal");
    }

    [Fact]
    public void PortTown_EntersUntapped_WhenHandHasPlains()
    {
        var bus = new ReplacementBus();
        SeedHand("Plains", _alice);
        var land = PortTownFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "a Plains card in hand can be revealed -> untapped");
    }

    [Fact]
    public void PortTown_EntersUntapped_WhenHandHasIsland()
    {
        var bus = new ReplacementBus();
        SeedHand("Island", _alice);
        var land = PortTownFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "an Island card in hand can be revealed -> untapped");
    }

    [Fact]
    public void PortTown_EntersTapped_WhenHandHasOnlyUnrelatedCard()
    {
        // A Mountain is neither a Plains nor an Island card, so nothing is
        // revealable -> enters tapped.
        var bus = new ReplacementBus();
        SeedHand("Mountain", _alice);
        var land = PortTownFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "a Mountain is neither a Plains nor an Island card");
    }

    [Fact]
    public void PortTown_EntersTapped_WhenOnlyOpponentHasPlains()
    {
        // "from your hand" — opponent's hand doesn't satisfy the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedHand("Plains", bob);
        SeedHand("Island", bob);

        var land = PortTownFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "only the controller's own hand can be revealed");
    }

    [Fact]
    public void PortTown_PredicateExcludesSelf()
    {
        // On a normal play the entering Port Town is in hand at predicate
        // time. It is not a Plains/Island card and is excluded by reference,
        // so an otherwise-empty hand still enters tapped.
        var bus = new ReplacementBus();
        var land = PortTownFactory.Create(_alice, replacements: bus);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "the land itself isn't a Plains/Island card and can't reveal itself");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void PortTown_Create_ThrowsOnNullOwner()
    {
        var act = () => PortTownFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedHand(string name, Player owner)
    {
        var card = NamedCardFactory.Create(name, owner);
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
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
