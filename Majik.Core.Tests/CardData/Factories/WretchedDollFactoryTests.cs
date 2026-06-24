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
/// Unit tests for <see cref="WretchedDollFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-24): {1}{B} Artifact Creature — Toy 3/1,
///   "{B}, {T}: Surveil 1."
///
/// Covers card identity, the single {B}+tap surveil activated ability shape
/// (mana cost plus tap cost — CR 602.5), and resolution (no-agent default sends
/// the peeked card to the graveyard — CR 701.42).
/// </summary>
[Trait("Color", "B")]
public class WretchedDollFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WretchedDoll_HasExpectedIdentity()
    {
        var creature = (Creature)NamedCardFactory.Create("Wretched Doll", _alice);

        creature.Name.Should().Be("Wretched Doll");
        creature.HasType(CardType.Artifact).Should().BeTrue();
        creature.HasType(CardType.Creature).Should().BeTrue();
        creature.Subtypes.Should().Contain(CardSubtype.Toy);
        creature.Power.Should().Be(3);
        creature.Toughness.Should().Be(1);
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WretchedDoll_HasSingleManaTapSurveilActivatedAbility()
    {
        var creature = (Creature)NamedCardFactory.Create("Wretched Doll", _alice);

        var activated = creature.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        activated.Should().ContainSingle("the only ability is {B}, {T}: Surveil 1");

        // {T} cost is present (CR 602.5 — paid alongside the {B} mana cost).
        activated[0].Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the activation cost includes {T}");

        // A mana cost is also present.
        activated[0].Costs.OfType<ManaCostCost>()
            .Should().ContainSingle("the activation cost includes {B}");

        activated[0].TargetRequests.Should().BeEmpty(
            "surveil targets nothing — the controller's own library");
    }

    [Fact]
    public void WretchedDoll_Surveil_DefaultsPeekedCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var creature = (Creature)NamedCardFactory.Create("Wretched Doll", alice);
        var ability = creature.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        foreach (var effect in ability.Effects) effect.Execute();

        // No agent registered → the no-agent default surveils the peeked card to
        // the graveyard (CR 701.42); the second card remains on the library.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }

    [Fact]
    public void WretchedDoll_Surveil_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);
        var creature = (Creature)NamedCardFactory.Create("Wretched Doll", alice);
        var ability = creature.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        Action act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
