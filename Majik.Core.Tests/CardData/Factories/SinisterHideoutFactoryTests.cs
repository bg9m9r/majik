using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SinisterHideoutFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-24): Land,
///   "This land enters tapped.
///    {T}: Add {U} or {B}.
///    {4}, {T}: Surveil 1."
///
/// Covers the card's unique behaviour: the two {U}/{B} mana abilities, the
/// {4},{T}: Surveil 1 activated ability shape, the surveil resolution
/// (no-agent default sends the peeked card to the graveyard), and the
/// unconditional ETB-tapped replacement (CR 614.1c). Dispatch + well-formedness
/// are covered for every implemented card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "M")] // U/B dual land — multicolour colour identity.
public class SinisterHideoutFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static int ColorOf(ManaCost m, string c) => c switch
    {
        "W" => m.White,
        "U" => m.Blue,
        "B" => m.Black,
        "R" => m.Red,
        "G" => m.Green,
        _ => throw new ArgumentException($"Unknown colour {c}"),
    };

    [Fact]
    public void SinisterHideout_IsPlainLand()
    {
        var land = (Land)NamedCardFactory.Create("Sinister Hideout", _alice);

        land.Name.Should().Be("Sinister Hideout");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Subtypes.Should().BeEmpty("Sinister Hideout has no basic-land subtype");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {U} or {B} — two single-colour mana abilities (CR 605.1a).
    // -----------------------------------------------------------------------

    [Fact]
    public void SinisterHideout_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Sinister Hideout", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => ColorOf(m.ManaGenerated, "U") == 1
                                      && ColorOf(m.ManaGenerated, "B") == 0);
    }

    [Fact]
    public void SinisterHideout_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Sinister Hideout", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => ColorOf(m.ManaGenerated, "B") == 1
                                      && ColorOf(m.ManaGenerated, "U") == 0);
    }

    // -----------------------------------------------------------------------
    // {4}, {T}: Surveil 1 — non-mana activated ability.
    // -----------------------------------------------------------------------

    [Fact]
    public void SinisterHideout_HasFourTapSurveilActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Sinister Hideout", _alice);

        var activated = land.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        activated.Should().ContainSingle("the only non-mana ability is {4},{T}: Surveil 1");

        activated[0].Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the activation cost includes {T}");
        activated[0].Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Cost.Generic == 4,
                "the activation cost includes the generic {4}");
        activated[0].TargetRequests.Should().BeEmpty(
            "surveil targets nothing — the controller's own library");
    }

    [Fact]
    public void SinisterHideout_Surveil_DefaultsPeekedCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = (Land)NamedCardFactory.Create("Sinister Hideout", alice);
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        foreach (var effect in ability.Effects) effect.Execute();

        // No agent registered → the no-agent default surveils the peeked card to
        // the graveyard (CR 701.42); the second card remains on the library.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }

    // -----------------------------------------------------------------------
    // This land enters tapped (CR 614.1c) — unconditional, wired via the
    // ReplacementBus overload.
    // -----------------------------------------------------------------------

    [Fact]
    public void SinisterHideout_WithReplacementBus_EntersTapped()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var alice = new Player("Alice", 20);
        var land = SinisterHideoutFactory.Create(alice, rep);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        land.IsTapped.Should().BeTrue("Sinister Hideout always enters tapped");
        land.Zone.Should().Be(ZoneType.Battlefield);
    }
}
