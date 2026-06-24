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
/// Unit tests for <see cref="ForumOfAmityFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-24): Land,
///   "This land enters tapped.
///    {T}: Add {W} or {B}.
///    {2}{W}{B}, {T}: Surveil 1. (Look at the top card of your library. You
///    may put it into your graveyard.)"
///
/// Covers the card's UNIQUE behaviour: the two single-colour mana abilities
/// ({W} / {B}) and the {2}{W}{B}, {T} activated surveil ability — its mana +
/// tap cost shape (CR 602.1) and the surveil-1 resolution (CR 701.20; the
/// no-agent default puts the peeked card into the graveyard).
///
/// Enters-tapped (CR 614.1c) is applied on the production load path by
/// <c>EntersTappedBinder</c>, not the named factory, so it is not asserted
/// here (same split as the Temple / surveil-land cycles). Dispatch +
/// well-formedness are covered for every implemented card by
/// <c>CardFactoryContractTests</c>.
/// </summary>
[Trait("Color", "M")]
public class ForumOfAmityFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static int ColorOf(Majik.Core.ValueObjects.ManaCost m, string c) => c switch
    {
        "W" => m.White,
        "U" => m.Blue,
        "B" => m.Black,
        "R" => m.Red,
        "G" => m.Green,
        _ => throw new ArgumentException($"Unknown colour {c}"),
    };

    [Fact]
    public void ForumOfAmity_IsLand_WithTwoManaAbilities_WhiteAndBlack()
    {
        var land = (Land)NamedCardFactory.Create("Forum of Amity", _alice);

        land.Name.Should().Be("Forum of Amity");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2);
        manaAbilities.Should().ContainSingle(m => ColorOf(m.ManaGenerated, "W") == 1
                                               && ColorOf(m.ManaGenerated, "B") == 0,
            "{T}: Add {W}");
        manaAbilities.Should().ContainSingle(m => ColorOf(m.ManaGenerated, "B") == 1
                                               && ColorOf(m.ManaGenerated, "W") == 0,
            "{T}: Add {B}");
    }

    [Fact]
    public void ForumOfAmity_HasActivatedSurveilAbility_With2WBAndTapCost()
    {
        var land = (Land)NamedCardFactory.Create("Forum of Amity", _alice);

        // The only non-mana activated ability is {2}{W}{B}, {T}: Surveil 1.
        var activated = land.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        activated.Should().ContainSingle("the only non-mana ability is {2}{W}{B}, {T}: Surveil 1");

        var ability = activated[0];

        // {T} component (CR 602.1).
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap, "the {T} component");

        // {2}{W}{B} mana component.
        var manaCost = ability.Costs.OfType<ManaCostCost>().Should().ContainSingle().Subject.Cost;
        manaCost.Generic.Should().Be(2);
        manaCost.White.Should().Be(1);
        manaCost.Black.Should().Be(1);
        manaCost.Blue.Should().Be(0);
        manaCost.Red.Should().Be(0);
        manaCost.Green.Should().Be(0);

        ability.TargetRequests.Should().BeEmpty("surveil targets nothing — the controller's own library");
    }

    [Fact]
    public void ForumOfAmity_Surveil_DefaultsPeekedCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = (Land)NamedCardFactory.Create("Forum of Amity", alice);
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        foreach (var effect in ability.Effects) effect.Execute();

        // No agent registered → the no-agent default surveils the peeked card
        // to the graveyard (CR 701.20); the second card stays on the library.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }

    [Fact]
    public void ForumOfAmity_Surveil_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Forum of Amity", alice);
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        Action act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
