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
/// Unit tests for <see cref="AncientAmphitheaterFactory"/> — Ancient
/// Amphitheater, a Lorwyn tribal-reveal dual land (R/W). Oracle text:
///   "As this land enters, you may reveal a Giant card from your hand. If you
///    don't, this land enters tapped.
///    {T}: Add {R} or {W}."
///
/// Covers:
/// - Identity (Land, nonbasic, no printed subtype).
/// - Two mana abilities producing {R} and {W} respectively (CR 605.1).
/// - No activated / triggered abilities beyond mana.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c), auto-reveal model: empty hand -> tapped; a Giant card in
///   hand -> untapped; a non-Giant card in hand -> tapped; opponent's hand
///   doesn't count; self excluded from the hand search.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Single-arg path registers no replacement.
/// </summary>
[Trait("Color", "RW")]
public class AncientAmphitheaterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void AncientAmphitheater_IsNotBasic()
    {
        var land = AncientAmphitheaterFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "tribal-reveal dual lands are nonbasic");
    }

    [Fact]
    public void AncientAmphitheater_HasTwoManaAbilities_ProducingRW()
    {
        var land = (Land)NamedCardFactory.Create("Ancient Amphitheater", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Ancient Amphitheater taps for {R} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void AncientAmphitheater_HasNoActivatedOrTriggeredAbilities()
    {
        var land = AncientAmphitheaterFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "tribal-reveal lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the ETB-tapped clause is a replacement effect, not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "reveal a Giant card"
    // -----------------------------------------------------------------------

    [Fact]
    public void AncientAmphitheater_EntersTapped_WhenHandHasNoGiant()
    {
        var bus = new ReplacementBus();
        var land = AncientAmphitheaterFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "no Giant card in hand to reveal");
    }

    [Fact]
    public void AncientAmphitheater_EntersUntapped_WhenHandHasGiant()
    {
        // Primeval Titan is a Creature — Giant, so it satisfies "a Giant card".
        var bus = new ReplacementBus();
        SeedHand("Primeval Titan", _alice);
        var land = AncientAmphitheaterFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "a Giant card in hand can be revealed -> untapped");
    }

    [Fact]
    public void AncientAmphitheater_EntersTapped_WhenHandHasOnlyNonGiant()
    {
        // A Plains is not a Giant card, so nothing is revealable -> tapped.
        var bus = new ReplacementBus();
        SeedHand("Plains", _alice);
        var land = AncientAmphitheaterFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "a Plains is not a Giant card");
    }

    [Fact]
    public void AncientAmphitheater_EntersTapped_WhenOnlyOpponentHasGiant()
    {
        // "from your hand" — opponent's hand doesn't satisfy the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedHand("Primeval Titan", bob);

        var land = AncientAmphitheaterFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "only the controller's own hand can be revealed");
    }

    [Fact]
    public void AncientAmphitheater_PredicateExcludesSelf()
    {
        // On a normal play the entering land is in hand at predicate time. It
        // is not a Giant card and is excluded by reference, so an otherwise-
        // empty hand still enters tapped.
        var bus = new ReplacementBus();
        var land = AncientAmphitheaterFactory.Create(_alice, replacements: bus);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "the land itself isn't a Giant card and can't reveal itself");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void AncientAmphitheater_Create_ThrowsOnNullOwner()
    {
        var act = () => AncientAmphitheaterFactory.Create(null!);
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
