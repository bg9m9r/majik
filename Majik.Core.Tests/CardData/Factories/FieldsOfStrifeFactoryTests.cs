using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FieldsOfStrifeFactory"/>.
///
/// Oracle (Scryfall-confirmed, Tempest):
///   "This land enters tapped.
///    {T}: Add {R} or {W}.
///    {2}{R}{W}, {T}: Surveil 1."
///
/// Scryfall type line: Land (no basic supertype, no subtypes). Colourless.
///
/// Same JSON-driven posture as <see cref="CastleVantressFactory"/> (activated
/// peek ability) + <see cref="TranquilCoveFactory"/> (unconditional
/// enters-tapped wiring). Identity, both {R}/{W} mana abilities, and the
/// {2}{R}{W},{T}: Surveil 1 activated ability are loaded from
/// <c>fields-of-strife.json</c> via the CardDefinition path; the unconditional
/// enters-tapped replacement (CR 614.1c) is wired in code on the two-arg path.
///
/// Covers the card's UNIQUE behaviour:
/// - Two {R}/{W} mana abilities (CR 605.1).
/// - The {2}{R}{W} + tap activated ability cost stack.
/// - Surveil 1 resolve (CR 701.42): default decision (no agent) sends the
///   top card to the graveyard.
/// - Unconditional "This land enters tapped" replacement (CR 614.1c).
/// </summary>
[Trait("Color", "C")]
public class FieldsOfStrifeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (single Identity assert — colourless nonbasic Land)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_Identity_IsNonbasicLand_NoSubtypes()
    {
        var land = FieldsOfStrifeFactory.Create(_alice);

        land.Name.Should().Be("Fields of Strife");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Fields of Strife is nonbasic");
        land.Subtypes.Should().BeEmpty("Fields of Strife has no basic-land subtypes");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape: two mana abilities + one activated ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasTwoManaAbilities_AndOneActivatedAbility()
    {
        var land = FieldsOfStrifeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {R} or {W} is modeled as two single-colour mana abilities");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{2}{R}{W},{T}: Surveil 1 is one activated ability");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {R} or {W} — one mana ability per colour
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbilities_ProduceRedAndWhite()
    {
        var land = FieldsOfStrifeFactory.Create(_alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().ContainSingle(m =>
            m.ManaGenerated.Red == 1 && m.ManaGenerated.White == 0,
            "one mana ability adds {R}");
        manaAbilities.Should().ContainSingle(m =>
            m.ManaGenerated.White == 1 && m.ManaGenerated.Red == 0,
            "one mana ability adds {W}");
    }

    // -----------------------------------------------------------------------
    // Activated ability: {2}{R}{W}, {T} cost gates
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasManaPlusTapCosts()
    {
        var land = FieldsOfStrifeFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2, "costs are {2}{R}{W} mana + tap");
        ability.Costs.Should().ContainItemsAssignableTo<ManaCostCost>(
            "one cost must be the {2}{R}{W} mana cost");
        ability.Costs.Should().Contain(c => c is AdditionalCost,
            "one cost must be the {T} tap cost");
    }

    // -----------------------------------------------------------------------
    // Surveil 1 resolve (CR 701.42)
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_Surveil1_PutsTopCardInGraveyard_WithNoAgent()
    {
        // No agent registered → the default surveil decision puts all peeked
        // cards (here, the single top card) into the graveyard.
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = FieldsOfStrifeFactory.Create(alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        // Execute the resolve body directly (cost gates verified above).
        ability.Effects.Single().Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c (unconditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_Unconditionally()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = FieldsOfStrifeFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "\"This land enters tapped\" is unconditional (CR 614.1c)");
    }
}
