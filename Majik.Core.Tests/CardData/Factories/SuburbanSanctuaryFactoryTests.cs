using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SuburbanSanctuaryFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-24): Land,
///   "This land enters tapped.
///    {T}: Add {G} or {W}.
///    {4}, {T}: Surveil 1."
///
/// Covers the card body's unique shape — the dual {G}/{W} mana abilities plus
/// the {4}, {T}: Surveil 1 activated ability (CR 701.42), which unlike the
/// plain {T}: Surveil 1 analogue (Sinister Starfish) carries an additional
/// generic {4} mana cost — and the surveil resolution (no-agent default sends
/// the peeked card to the graveyard). Enters-tapped (CR 614.1c) is owned by the
/// binder layer on the production load path, not this shape-only factory.
/// </summary>
[Trait("Color", "M")]
public class SuburbanSanctuaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land Create() => (Land)NamedCardFactory.Create("Suburban Sanctuary", _alice);

    [Fact]
    public void SuburbanSanctuary_Identity_IsPlainLand()
    {
        var land = Create();

        land.Name.Should().Be("Suburban Sanctuary");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        // Type line is bare "Land" — no basic-land subtypes.
        land.Subtypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SuburbanSanctuary_HasGreenAndWhiteManaAbilities()
    {
        var land = Create();

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(2, "{T}: Add {G} or {W} splits into one ability per colour");
        mana.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.White == 0);
        mana.Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void SuburbanSanctuary_HasSurveilAbility_WithFourManaPlusTapCost()
    {
        var land = Create();

        var activated = land.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        activated.Should().ContainSingle("the only non-mana ability is {4}, {T}: Surveil 1");

        var surveil = activated[0];
        // {T} portion of the cost.
        surveil.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the activation cost includes {T}");
        // {4} generic-mana portion of the cost.
        surveil.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Cost.Generic == 4,
                "the activation cost includes {4}");
        surveil.TargetRequests.Should().BeEmpty(
            "surveil targets nothing — the controller's own library");
    }

    [Fact]
    public void SuburbanSanctuary_Surveil_DefaultsPeekedCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = (Land)NamedCardFactory.Create("Suburban Sanctuary", alice);
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        foreach (var effect in ability.Effects) effect.Execute();

        // No agent registered → the no-agent default surveils the peeked card to
        // the graveyard (CR 701.42); the second card remains on the library.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }
}
