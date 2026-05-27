using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Parametric tests for the Murders at Karlov Manor / Foundations "surveil
/// land" dual cycle — the seven members not already covered by their own
/// dedicated factories (Elegant Parlor, Thundering Falls, and Underground
/// Mortuary ship separately):
///
///   Commercial District (R/G), Hedge Maze (G/U), Lush Portico (G/W),
///   Meticulous Archive (W/U), Raucous Theater (B/R),
///   Shadowy Backstreet (W/B), Undercity Sewers (U/B).
///
/// Each shares the same shape (mirrors <see cref="ElegantParlorFactory"/>):
///   - Land (no subtypes / supertypes).
///   - Two mana abilities — one per colour in the pair.
///   - One ETB-triggered ability (battlefield-active) that surveils 1;
///     the default decision puts the top card into the graveyard.
///   - Enters-tapped is applied on the production load path by
///     <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by the
///     named-card factory (test convenience), same as the existing trio.
/// </summary>
public class SurveilLandCycleTests
{
    /// <summary>Per-card spec: printed name + the two mana colours.</summary>
    public sealed record SurveilLandSpec(string Name, string Color1, string Color2);

    public static IEnumerable<object[]> Cards() => new[]
    {
        new object[] { new SurveilLandSpec("Commercial District", "R", "G") },
        new object[] { new SurveilLandSpec("Hedge Maze",          "G", "U") },
        new object[] { new SurveilLandSpec("Lush Portico",        "G", "W") },
        new object[] { new SurveilLandSpec("Meticulous Archive",  "W", "U") },
        new object[] { new SurveilLandSpec("Raucous Theater",     "B", "R") },
        new object[] { new SurveilLandSpec("Shadowy Backstreet",  "W", "B") },
        new object[] { new SurveilLandSpec("Undercity Sewers",    "U", "B") },
    };

    private readonly Player _alice = new("Alice", 20);

    private Card Create(string name) => (Card)NamedCardFactory.Create(name, _alice);

    private static int ColorOf(ManaCost m, string c) => c switch
    {
        "W" => m.White,
        "U" => m.Blue,
        "B" => m.Black,
        "R" => m.Red,
        "G" => m.Green,
        _ => throw new ArgumentException($"Unknown colour {c}"),
    };

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cards))]
    public void SurveilLand_IsLand_WithCorrectName(SurveilLandSpec spec)
    {
        var land = Create(spec.Name);

        land.Name.Should().Be(spec.Name);
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void NamedCardFactory_Dispatches_SurveilLand(SurveilLandSpec spec)
    {
        var card = NamedCardFactory.Create(spec.Name, _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be(spec.Name);
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add c1 or c2 — two single-colour mana abilities
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cards))]
    public void SurveilLand_HasManaAbility_ForFirstColor(SurveilLandSpec spec)
    {
        var land = Create(spec.Name);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => ColorOf(m.ManaGenerated, spec.Color1) == 1
                                      && ColorOf(m.ManaGenerated, spec.Color2) == 0);
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void SurveilLand_HasManaAbility_ForSecondColor(SurveilLandSpec spec)
    {
        var land = Create(spec.Name);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => ColorOf(m.ManaGenerated, spec.Color2) == 1
                                      && ColorOf(m.ManaGenerated, spec.Color1) == 0);
    }

    // -----------------------------------------------------------------------
    // ETB surveil 1
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cards))]
    public void SurveilLand_EtbTrigger_IsBattlefieldActive(SurveilLandSpec spec)
    {
        var land = Create(spec.Name);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void SurveilLand_SurveilEffect_PutsTopCardInGraveyard(SurveilLandSpec spec)
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = (Card)NamedCardFactory.Create(spec.Name, alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Graveyard);
    }
}
