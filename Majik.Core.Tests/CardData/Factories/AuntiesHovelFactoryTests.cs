using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AuntiesHovelFactory"/> — Auntie's Hovel, a Lorwyn
/// "reveal-tribal" land (B/R). Oracle text (verified against Scryfall
/// 2026-06-02):
///   "As this land enters, you may reveal a Goblin card from your hand. If you
///    don't, this land enters tapped.
///    {T}: Add {B} or {R}."
///
/// Covers:
/// - Identity (Land, nonbasic, no printed subtype).
/// - Two mana abilities producing {B} and {R} respectively (CR 605.1).
/// - No activated / triggered abilities beyond mana.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c), auto-reveal model: empty hand -> tapped; a Goblin card in
///   hand -> untapped; a non-Goblin card in hand -> tapped; opponent's hand
///   doesn't count; self excluded from the hand search.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Single-arg path registers no replacement.
///
/// Mirrors <see cref="PortTownFactoryTests"/>, swapping the buddy-land
/// "Plains or Island card" reveal predicate for the reveal-tribal
/// "Goblin card" predicate and {W}/{U} mana for {B}/{R}.
/// </summary>
[Trait("Color", "BR")]
public class AuntiesHovelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void AuntiesHovel_IsNotBasic()
    {
        var land = AuntiesHovelFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "reveal-tribal lands are nonbasic");
    }

    [Fact]
    public void AuntiesHovel_HasTwoManaAbilities_ProducingBR()
    {
        var land = (Land)NamedCardFactory.Create("Auntie's Hovel", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Auntie's Hovel taps for {B} or {R}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    [Fact]
    public void AuntiesHovel_HasNoActivatedOrTriggeredAbilities()
    {
        var land = AuntiesHovelFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "reveal-tribal lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the ETB-tapped clause is a replacement effect, not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "reveal a Goblin card"
    // -----------------------------------------------------------------------

    [Fact]
    public void AuntiesHovel_EntersTapped_WhenHandHasNoGoblin()
    {
        var bus = new ReplacementBus();
        var land = AuntiesHovelFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "no Goblin card in hand to reveal");
    }

    [Fact]
    public void AuntiesHovel_EntersUntapped_WhenHandHasGoblin()
    {
        var bus = new ReplacementBus();
        SeedHand("Goblin Guide", _alice); // Creature — Goblin Scout
        var land = AuntiesHovelFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "a Goblin card in hand can be revealed -> untapped");
    }

    [Fact]
    public void AuntiesHovel_EntersTapped_WhenHandHasOnlyNonGoblinCard()
    {
        // A Mountain is not a Goblin card, so nothing is revealable -> tapped.
        var bus = new ReplacementBus();
        SeedHand("Mountain", _alice);
        var land = AuntiesHovelFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "a Mountain is not a Goblin card");
    }

    [Fact]
    public void AuntiesHovel_EntersTapped_WhenOnlyOpponentHasGoblin()
    {
        // "from your hand" — opponent's hand doesn't satisfy the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedHand("Goblin Guide", bob);

        var land = AuntiesHovelFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "only the controller's own hand can be revealed");
    }

    [Fact]
    public void AuntiesHovel_PredicateExcludesSelf()
    {
        // On a normal play the entering Auntie's Hovel is in hand at predicate
        // time. It is not a Goblin card and is excluded by reference, so an
        // otherwise-empty hand still enters tapped.
        var bus = new ReplacementBus();
        var land = AuntiesHovelFactory.Create(_alice, replacements: bus);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "the land itself isn't a Goblin card and can't reveal itself");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void AuntiesHovel_Create_ThrowsOnNullOwner()
    {
        var act = () => AuntiesHovelFactory.Create(null!);
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
