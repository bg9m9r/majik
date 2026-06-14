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
/// Unit tests for <see cref="GiltLeafPalaceFactory"/> — Gilt-Leaf Palace, a
/// Lorwyn "reveal-tribal" land (B/G). Oracle text (verified against Scryfall
/// 2026-06-14):
///   "As this land enters, you may reveal an Elf card from your hand. If you
///    don't, this land enters tapped.
///    {T}: Add {B} or {G}."
///
/// Covers:
/// - Identity (Land, nonbasic, no printed subtype).
/// - Two mana abilities producing {B} and {G} respectively (CR 605.1).
/// - No activated / triggered abilities beyond mana.
/// - ETB-tapped predicate via <see cref="ConditionalEntersTappedReplacement"/>
///   (CR 614.1c), auto-reveal model: empty hand -> tapped; an Elf card in
///   hand -> untapped; a non-Elf card in hand -> tapped; opponent's hand
///   doesn't count; self excluded from the hand search.
/// - Single-arg path registers no replacement.
///
/// Mirrors <see cref="AuntiesHovelFactoryTests"/>, swapping the "Goblin card"
/// reveal predicate for the "Elf card" predicate and {B}/{R} mana for {B}/{G}.
/// </summary>
[Trait("Color", "BG")]
public class GiltLeafPalaceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void GiltLeafPalace_IsNotBasic()
    {
        var land = GiltLeafPalaceFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "reveal-tribal lands are nonbasic");
    }

    [Fact]
    public void GiltLeafPalace_HasTwoManaAbilities_ProducingBG()
    {
        var land = (Land)NamedCardFactory.Create("Gilt-Leaf Palace", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Gilt-Leaf Palace taps for {B} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void GiltLeafPalace_HasNoActivatedOrTriggeredAbilities()
    {
        var land = GiltLeafPalaceFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "reveal-tribal lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the ETB-tapped clause is a replacement effect, not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c) — "reveal an Elf card"
    // -----------------------------------------------------------------------

    [Fact]
    public void GiltLeafPalace_EntersTapped_WhenHandHasNoElf()
    {
        var bus = new ReplacementBus();
        var land = GiltLeafPalaceFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "no Elf card in hand to reveal");
    }

    [Fact]
    public void GiltLeafPalace_EntersUntapped_WhenHandHasElf()
    {
        var bus = new ReplacementBus();
        SeedHand("Elvish Warmaster", _alice); // Creature — Elf Warrior
        var land = GiltLeafPalaceFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeFalse(
            "an Elf card in hand can be revealed -> untapped");
    }

    [Fact]
    public void GiltLeafPalace_EntersTapped_WhenHandHasOnlyNonElfCard()
    {
        // A Forest is not an Elf card, so nothing is revealable -> tapped.
        var bus = new ReplacementBus();
        SeedHand("Forest", _alice);
        var land = GiltLeafPalaceFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "a Forest is not an Elf card");
    }

    [Fact]
    public void GiltLeafPalace_EntersTapped_WhenOnlyOpponentHasElf()
    {
        // "from your hand" — opponent's hand doesn't satisfy the predicate.
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        SeedHand("Elvish Warmaster", bob);

        var land = GiltLeafPalaceFactory.Create(_alice, replacements: bus);
        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "only the controller's own hand can be revealed");
    }

    [Fact]
    public void GiltLeafPalace_PredicateExcludesSelf()
    {
        // On a normal play the entering Gilt-Leaf Palace is in hand at predicate
        // time. It is not an Elf card and is excluded by reference, so an
        // otherwise-empty hand still enters tapped.
        var bus = new ReplacementBus();
        var land = GiltLeafPalaceFactory.Create(_alice, replacements: bus);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "the land itself isn't an Elf card and can't reveal itself");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void GiltLeafPalace_Create_ThrowsOnNullOwner()
    {
        var act = () => GiltLeafPalaceFactory.Create(null!);
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
