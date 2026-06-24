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
/// Unit tests for <see cref="SpectacleSummitFactory"/> (Tarkir: Dragonstorm,
/// the U/R "spire" surveil land).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "This land enters tapped.
///    {T}: Add {U} or {R}.
///    {2}{U}{R}, {T}: Surveil 1."
///
/// Distinct from the Karlov-Manor surveil dual cycle: that cycle surveils on an
/// ETB trigger, whereas Spectacle Summit's surveil is a paid <em>activated</em>
/// ability — <c>{2}{U}{R}, {T}: Surveil 1</c> (CR 701.42). Loaded from the
/// embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers the card's UNIQUE behaviour:
/// - Two single-colour mana abilities — {U} and {R} (CR 605.1a).
/// - One activated ability whose cost is {2}{U}{R} + {T} and whose effect is
///   surveil 1, with no targets.
/// - Surveil resolution: the no-agent default sends the peeked card to the
///   graveyard (CR 701.42).
///
/// Card identity (Land / colourless) and NamedCardFactory dispatch are asserted
/// for every implemented card by CardFactoryContractTests, so they are not
/// re-checked here. Enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// shape-only factory path — same posture as the Tranquil Cove / Temple cycles.
/// </summary>
[Trait("Color", "C")]
public class SpectacleSummitFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SpectacleSummit_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Spectacle Summit", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void SpectacleSummit_HasManaAbility_ForRed()
    {
        var land = (Land)NamedCardFactory.Create("Spectacle Summit", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void SpectacleSummit_HasActivatedSurveilAbility_WithManaAndTapCost()
    {
        var land = (Land)NamedCardFactory.Create("Spectacle Summit", _alice);

        // The non-mana activated ability is {2}{U}{R}, {T}: Surveil 1.
        var activated = land.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        activated.Should().ContainSingle("the only non-mana ability is {2}{U}{R}, {T}: Surveil 1");

        // {T} is part of the activation cost (CR 602.1 — the {T} symbol cost).
        activated[0].Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the activation cost includes {T}");

        // {2}{U}{R} mana cost: 2 generic + one blue + one red.
        activated[0].Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Cost.Blue == 1
                                      && c.Cost.Red == 1
                                      && c.Cost.Generic == 2,
                "the activation cost includes {2}{U}{R}");

        activated[0].TargetRequests.Should().BeEmpty(
            "surveil targets nothing — the controller's own library");
    }

    [Fact]
    public void SpectacleSummit_Surveil_DefaultsPeekedCardToGraveyard()
    {
        // CR 701.42 — surveil 1 looks at the top card; the no-agent default puts
        // it into the graveyard. The second card stays on top of the library.
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = (Land)NamedCardFactory.Create("Spectacle Summit", alice);
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }
}
