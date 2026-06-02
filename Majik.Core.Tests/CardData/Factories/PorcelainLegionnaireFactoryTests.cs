using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PorcelainLegionnaireFactory"/>.
///
/// Card: Porcelain Legionnaire — {2}{W/P} Artifact Creature —
/// Phyrexian Soldier 3/1. Oracle text (verified against Scryfall):
///   "({W/P} can be paid with either {W} or 2 life.)
///    First strike"
///
/// The parenthetical is reminder text for the Phyrexian-mana pip in the
/// cost (CR 107.4f / 118.8); the only printed ability is First strike.
/// </summary>
[Trait("Color", "W")]
public class PorcelainLegionnaireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PorcelainLegionnaire_Identity()
    {
        var c = PorcelainLegionnaireFactory.Create(_alice);

        c.Name.Should().Be("Porcelain Legionnaire");
        c.ManaCost.Should().Be("{2}{W/P}");
        c.HasType(CardType.Creature).Should().BeTrue("Porcelain Legionnaire is a Creature");
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Porcelain Legionnaire is an Artifact Creature (CR 301.1 / 302.1)");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue("Phyrexian is a printed subtype");
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue("Soldier is a printed subtype");
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PorcelainLegionnaire_HasPhyrexianPipAndManaValueThree()
    {
        var c = PorcelainLegionnaireFactory.Create(_alice);

        // {2}{W/P} → generic 2 + one phyrexian (white) pip = mana value 3
        // (CR 202.3 / 107.4f). The {W/P} pip parses into PhyrexianPips, not
        // a coloured-pip bucket, so the parsed cost has a single white
        // phyrexian pip.
        var cost = ManaCost.Parse(c.ManaCost);
        cost.TotalValue.Should().Be(3);
        cost.PhyrexianPips.Should().ContainSingle()
            .Which.Should().Be(ManaColor.White);
    }

    [Fact]
    public void PorcelainLegionnaire_HasFirstStrikeKeywordMarker()
    {
        var c = PorcelainLegionnaireFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "First strike").Should().BeTrue(
                "Porcelain Legionnaire ships with First strike as a KeywordAbility marker (CR 702.7)");
    }

    [Fact]
    public void PorcelainLegionnaire_FirstStrikeIsTheOnlyAbility()
    {
        var c = PorcelainLegionnaireFactory.Create(_alice);

        // The parenthetical is reminder text only — First strike is the
        // sole printed ability.
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "First strike is the only printed keyword");
    }
}
