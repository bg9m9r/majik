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
/// Unit tests for <see cref="SavageMansionFactory"/>.
///
/// Savage Mansion (Murders at Karlov Manor) — Land. Oracle text (Scryfall):
///   "This land enters tapped.
///    {T}: Add {R} or {G}.
///    {4}, {T}: Surveil 1. (Look at the top card of your library. You may put
///    it into your graveyard.)"
///
/// Covers the card's UNIQUE behaviour: the two single-colour {R}/{G} mana
/// abilities and — distinct from the ETB surveil-land cycle — the repeatable
/// <c>{4}, {T}: Surveil 1</c> ACTIVATED ability (CR 701.42), including its
/// ManaCostCost({4}) + tap-self cost composition and the no-agent default
/// (peeked card → graveyard). Enters-tapped is applied on the production load
/// path by <see cref="EntersTappedBinder"/>, not the named factory (same split
/// as the surveil-land cycle / scry-land temples).
/// </summary>
[Trait("Color", "M")]
public class SavageMansionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land Create() => (Land)NamedCardFactory.Create("Savage Mansion", _alice);

    // -----------------------------------------------------------------------
    // Mana abilities — {T}: Add {R} or {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void SavageMansion_HasTwoManaAbilities_RedAndGreen()
    {
        var land = Create();

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(2, "{T}: Add {R} or {G} composes one mana ability per colour");
        mana.Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Green == 0);
        mana.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Red == 0);
    }

    // -----------------------------------------------------------------------
    // {4}, {T}: Surveil 1 — activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SavageMansion_HasSingleSurveilActivatedAbility()
    {
        var land = Create();

        // The mana abilities are also activated; exclude them to isolate the
        // {4}, {T}: Surveil 1 ability.
        var activated = land.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();

        activated.Should().ContainSingle("the only non-mana ability is {4}, {T}: Surveil 1");
        activated[0].TargetRequests.Should().BeEmpty(
            "surveil targets nothing — the controller's own library");
    }

    [Fact]
    public void SavageMansion_SurveilAbility_HasManaCostFour()
    {
        var land = Create();
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(4, "the {4} component");
        manaCost.Red.Should().Be(0, "no coloured component");
        manaCost.Green.Should().Be(0, "no coloured component");
    }

    [Fact]
    public void SavageMansion_SurveilAbility_HasTapSelfCost()
    {
        var land = Create();
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the {T} symbol composes a tap-self additional cost");
    }

    [Fact]
    public void SavageMansion_SurveilAbility_HasExactlyTwoCosts()
    {
        var land = Create();
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        ability.Costs.Should().HaveCount(2, "ManaCostCost({4}) + tap-self");
    }

    // -----------------------------------------------------------------------
    // Surveil resolution (CR 701.42)
    // -----------------------------------------------------------------------

    [Fact]
    public void SavageMansion_Surveil_DefaultsPeekedCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = (Land)NamedCardFactory.Create("Savage Mansion", alice);
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        foreach (var effect in ability.Effects) effect.Execute();

        // No agent registered → the no-agent default surveils the peeked card to
        // the graveyard (CR 701.42); the second card remains on the library.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }

    [Fact]
    public void SavageMansion_Surveil_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Savage Mansion", alice);
        var ability = land.Abilities.OfType<ActivatedAbility>()
            .First(a => a is not IManaAbility);

        Action act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
