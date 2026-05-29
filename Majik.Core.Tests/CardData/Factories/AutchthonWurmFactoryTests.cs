using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AutchthonWurmFactory"/>
/// (Ravnica: City of Guilds, {10}{G}{G}{G}{W}{W}).
///
/// Creature — Wurm 9/14. Oracle text:
///   "Convoke (CR 702.51)
///    Trample (CR 702.19)"
///
/// Covers:
///   - Identity (name / cost / power / toughness / subtype / mana value).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Convoke keyword marker present.
///   - Trample keyword marker present; <see cref="CombatAbilities.HasTrample"/> reads it.
///   - Convoke cost reduction: 8 creatures tapped → 8 less generic (CR 702.51b).
///   - Exactly two keyword abilities (no extra abilities).
/// </summary>
public class AutchthonWurmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeCostPowerToughness()
    {
        var card = AutchthonWurmFactory.Create(_alice);

        card.Name.Should().Be("Autochthon Wurm");
        card.ManaCost.Should().Be("{10}{G}{G}{G}{W}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.Power.Should().Be(9);
        creature.Toughness.Should().Be(14);
    }

    [Fact]
    public void Identity_ManaValue_IsFifteen()
    {
        var card = AutchthonWurmFactory.Create(_alice);

        // {10}{G}{G}{G}{W}{W} = 10 + 3 + 2 = 15 (CR 202.3)
        card.ManaCostValue.TotalValue.Should().Be(15);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AutchthonWurm()
    {
        var card = NamedCardFactory.Create("Autochthon Wurm", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Autochthon Wurm");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
    }

    // ── Convoke ───────────────────────────────────────────────────────────────

    [Fact]
    public void Card_HasConvokeKeyword()
    {
        var card = AutchthonWurmFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Convoke",
                "Autochthon Wurm has printed Convoke (CR 702.51)");
    }

    [Fact]
    public void Convoke_EightCreaturesTapped_ReducesEightGeneric()
    {
        // {10}{G}{G}{G}{W}{W} has 10 generic + 3 green + 2 white.
        // Tapping 8 generic-coloured creatures removes 8 from the generic portion
        // (CR 702.51b — each tap pays {1} or one mana of the creature's colour).
        // With 8 colourless/neutral taps the generic drops from 10 → 2;
        // the coloured pips remain.
        var printedCost = ManaCost.Parse(AutchthonWurmFactory.PrintedManaCost);
        var tappedCreatures = Enumerable.Range(0, 8)
            .Select(_ => new Creature("Token", "{1}", 1, 1))
            .ToArray();

        var reduced = ConvokeAlternativeCost.ReduceCost(printedCost, tappedCreatures);

        reduced.Generic.Should().Be(2,
            "8 taps on {10}{G}{G}{G}{W}{W} reduce the 10 generic pips to 2");
        reduced.Green.Should().Be(3, "green pips are untouched by colourless taps");
        reduced.White.Should().Be(2, "white pips are untouched by colourless taps");
    }

    // ── Trample ───────────────────────────────────────────────────────────────

    [Fact]
    public void Card_HasTrampleKeyword()
    {
        var card = AutchthonWurmFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "Autochthon Wurm has printed Trample (CR 702.19)");
    }

    [Fact]
    public void CombatAbilities_HasTrample_ReturnsTrue()
    {
        var card = AutchthonWurmFactory.Create(_alice);

        CombatAbilities.HasTrample(card).Should().BeTrue(
            "Trample keyword marker must be recognized by CombatAbilities");
    }

    // ── Ability count ─────────────────────────────────────────────────────────

    [Fact]
    public void Card_HasExactlyTwoKeywordAbilities()
    {
        var card = AutchthonWurmFactory.Create(_alice);

        // Exactly two abilities: Convoke + Trample.
        card.Abilities.Should().HaveCount(2,
            "Autochthon Wurm has exactly two printed keywords: Convoke and Trample");
        card.Abilities.Should().AllBeOfType<KeywordAbility>();
    }
}
