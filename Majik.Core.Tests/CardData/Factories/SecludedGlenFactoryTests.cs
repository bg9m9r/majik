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
/// Unit tests for <see cref="SecludedGlenFactory"/> — Secluded Glen, a Lorwyn
/// tribal-reveal dual land (U/B). Oracle text:
///   "As this land enters, you may reveal a Faerie card from your hand. If you
///    don't, this land enters tapped.
///    {T}: Add {U} or {B}."
///
/// Covers:
/// - Identity (Land, nonbasic, no printed subtype).
/// - Two mana abilities producing {U} and {B} respectively (CR 605.1).
/// - No activated / triggered abilities beyond mana.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c), auto-reveal model: empty hand -> tapped; a Faerie card in
///   hand -> untapped; a non-Faerie card in hand -> tapped; opponent's hand
///   doesn't count; self excluded from the hand search.
/// - Single-arg path registers no replacement.
/// </summary>
[Trait("Color", "U")]
public class SecludedGlenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------
    [Fact]
    public void SecludedGlen_IsNotBasic()
    {
        var land = SecludedGlenFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "tribal-reveal dual lands are nonbasic");
    }

    [Fact]
    public void SecludedGlen_HasTwoManaAbilities_ProducingUB()
    {
        var land = (Land)NamedCardFactory.Create("Secluded Glen", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Secluded Glen taps for {U} or {B}");
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    [Fact]
    public void SecludedGlen_HasNoActivatedOrTriggeredAbilities()
    {
        var land = SecludedGlenFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "tribal-reveal lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the ETB-tapped clause is a replacement effect, not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "reveal a Faerie card"
    // -----------------------------------------------------------------------

    [Fact]
    public void SecludedGlen_EntersTapped_WhenHandHasNoFaerie()
    {
        var bus = new ReplacementBus();
        var land = SecludedGlenFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "no Faerie card in hand to reveal");
    }

    [Fact]
    public void SecludedGlen_EntersUntapped_WhenHandHasFaerie()
    {
        // Sprite Dragon is a Creature — Faerie Dragon, so it satisfies
        // "a Faerie card".
        var bus = new ReplacementBus();
        SeedHand("Sprite Dragon", _alice);
        var land = SecludedGlenFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "a Faerie card in hand can be revealed -> untapped");
    }

    [Fact]
    public void SecludedGlen_EntersTapped_WhenHandHasOnlyNonFaerie()
    {
        // A Plains is not a Faerie card, so nothing is revealable -> tapped.
        var bus = new ReplacementBus();
        SeedHand("Plains", _alice);
        var land = SecludedGlenFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "a Plains is not a Faerie card");
    }

    [Fact]
    public void SecludedGlen_EntersTapped_WhenOnlyOpponentHasFaerie()
    {
        // "from your hand" — opponent's hand doesn't satisfy the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedHand("Sprite Dragon", bob);

        var land = SecludedGlenFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "only the controller's own hand can be revealed");
    }

    [Fact]
    public void SecludedGlen_PredicateExcludesSelf()
    {
        // On a normal play the entering land is in hand at predicate time. It
        // is not a Faerie card and is excluded by reference, so an otherwise-
        // empty hand still enters tapped.
        var bus = new ReplacementBus();
        var land = SecludedGlenFactory.Create(_alice, replacements: bus);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "the land itself isn't a Faerie card and can't reveal itself");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void SecludedGlen_Create_ThrowsOnNullOwner()
    {
        var act = () => SecludedGlenFactory.Create(null!);
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
