using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ParadoxGardensFactory"/>.
///
/// Oracle (Scryfall-confirmed, Edge of Eternities):
///   "This land enters tapped.
///    {T}: Add {G} or {U}.
///    {2}{G}{U}, {T}: Surveil 1. (Look at the top card of your library. You
///    may put it into your graveyard.)"
///
/// Scryfall type line: plain <c>Land</c> (no basic-land subtypes, unlike the
/// Karlov Manor surveil-land cycle). Closest analogue is
/// <see cref="CastleVantressFactory"/> — the surveil is an ACTIVATED ability
/// ({2}{G}{U}, {T}) rather than an ETB trigger.
///
/// Identity + all abilities load from <c>paradox-gardens.json</c> via
/// <see cref="CardDefinitionFactory"/>. Enters-tapped (CR 614.1c) is owned by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> on the production load
/// path, not by this named factory (no <see cref="ReplacementBus"/> here) —
/// same posture as the rest of the surveil lands.
///
/// Covers the card's UNIQUE behaviour:
/// - Two single-colour mana abilities producing {G} and {U}.
/// - One {2}{G}{U}, {T}: Surveil 1 activated ability — cost gates + resolve.
/// </summary>
[Trait("Color", "M")]
public class ParadoxGardensFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Ability count / shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasThreeAbilities_TwoManaOneActivated()
    {
        var land = ParadoxGardensFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {G} or {U} is two single-colour mana abilities (CR 605.1a)");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{2}{G}{U}, {T}: Surveil 1 is one activated ability");
    }

    // -----------------------------------------------------------------------
    // Mana ability: {T}: Add {G} or {U} — two single-colour mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbilities_ProduceGreenAndBlue()
    {
        var land = ParadoxGardensFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().ContainSingle(m => m.ManaGenerated.Green == 1
                                      && m.ManaGenerated.Blue == 0);
        mana.Should().ContainSingle(m => m.ManaGenerated.Blue == 1
                                      && m.ManaGenerated.Green == 0);
    }

    // -----------------------------------------------------------------------
    // Activated ability: {2}{G}{U}, {T} — cost gates
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasExpectedCosts()
    {
        var land = ParadoxGardensFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2, "costs are {2}{G}{U} mana + tap");
        ability.Costs.Should().ContainItemsAssignableTo<ManaCostCost>(
            "one cost must be the {2}{G}{U} mana cost");
        ability.Costs.Should().Contain(c => c is AdditionalCost,
            "one cost must be the {T} tap cost");
    }

    // -----------------------------------------------------------------------
    // Activated ability: Surveil 1 (CR 701.43)
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_SurveilOne_PutsTopCardInGraveyard_WithNoAgent()
    {
        // No agent registered → the default surveil decision sends the peeked
        // card to the graveyard (same fallback as the surveil-land cycle).
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = ParadoxGardensFactory.Create(alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        // Execute the resolve body directly (cost gates verified above).
        ability.Effects.Single().Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Graveyard);
    }
}
