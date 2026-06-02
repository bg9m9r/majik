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
/// Unit tests for <see cref="ForebodingRuinsFactory"/> — Foreboding Ruins, a
/// Shadows over Innistrad "buddy land" / "slow land" (B/R). Oracle text:
///   "As this land enters, you may reveal a Swamp or Mountain card from your
///    hand. If you don't, this land enters tapped.
///    {T}: Add {B} or {R}."
///
/// Covers:
/// - Identity (Land, nonbasic, no printed subtype).
/// - Two mana abilities producing {B} and {R} respectively (CR 605.1).
/// - No activated / triggered abilities beyond mana.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c), auto-reveal model: empty hand -> tapped; a Swamp card in
///   hand -> untapped; a Mountain card in hand -> untapped; an unrelated
///   (non-Swamp/Mountain) card in hand -> tapped; opponent's hand doesn't
///   count; self excluded from the hand search.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Single-arg path registers no replacement.
/// </summary>
[Trait("Color", "BR")]
public class ForebodingRuinsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void ForebodingRuins_IsNotBasic()
    {
        var land = ForebodingRuinsFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "buddy lands are nonbasic");
    }

    [Fact]
    public void ForebodingRuins_HasTwoManaAbilities_ProducingBR()
    {
        var land = (Land)NamedCardFactory.Create("Foreboding Ruins", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Foreboding Ruins taps for {B} or {R}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    [Fact]
    public void ForebodingRuins_HasNoActivatedOrTriggeredAbilities()
    {
        var land = ForebodingRuinsFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "buddy lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the ETB-tapped clause is a replacement effect, not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "reveal a Swamp or Mountain card"
    // -----------------------------------------------------------------------

    [Fact]
    public void ForebodingRuins_EntersTapped_WhenHandHasNoSwampOrMountain()
    {
        var bus = new ReplacementBus();
        var land = ForebodingRuinsFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "no Swamp-or-Mountain card in hand to reveal");
    }

    [Fact]
    public void ForebodingRuins_EntersUntapped_WhenHandHasSwamp()
    {
        var bus = new ReplacementBus();
        SeedHand("Swamp", _alice);
        var land = ForebodingRuinsFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "a Swamp card in hand can be revealed -> untapped");
    }

    [Fact]
    public void ForebodingRuins_EntersUntapped_WhenHandHasMountain()
    {
        var bus = new ReplacementBus();
        SeedHand("Mountain", _alice);
        var land = ForebodingRuinsFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "a Mountain card in hand can be revealed -> untapped");
    }

    [Fact]
    public void ForebodingRuins_EntersTapped_WhenHandHasOnlyUnrelatedCard()
    {
        // A Plains is neither a Swamp nor a Mountain card, so nothing is
        // revealable -> enters tapped.
        var bus = new ReplacementBus();
        SeedHand("Plains", _alice);
        var land = ForebodingRuinsFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "a Plains is neither a Swamp nor a Mountain card");
    }

    [Fact]
    public void ForebodingRuins_EntersTapped_WhenOnlyOpponentHasSwamp()
    {
        // "from your hand" — opponent's hand doesn't satisfy the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedHand("Swamp", bob);
        SeedHand("Mountain", bob);

        var land = ForebodingRuinsFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "only the controller's own hand can be revealed");
    }

    [Fact]
    public void ForebodingRuins_PredicateExcludesSelf()
    {
        // On a normal play the entering Foreboding Ruins is in hand at
        // predicate time. It is not a Swamp/Mountain card and is excluded by
        // reference, so an otherwise-empty hand still enters tapped.
        var bus = new ReplacementBus();
        var land = ForebodingRuinsFactory.Create(_alice, replacements: bus);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "the land itself isn't a Swamp/Mountain card and can't reveal itself");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void ForebodingRuins_Create_ThrowsOnNullOwner()
    {
        var act = () => ForebodingRuinsFactory.Create(null!);
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
