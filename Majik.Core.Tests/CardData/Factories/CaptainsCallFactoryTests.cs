using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Captain's Call ({3}{W}, Sorcery).
///
/// Coverage:
/// - Identity (name / type / printed mana cost / colour).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve effect creates exactly three 1/1 white Soldier creature tokens
///   on the caster's battlefield (CR 111 / 111.4 / CR 202.3).
/// </summary>
[Trait("Color", "W")]
public class CaptainsCallFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CaptainsCall_Identity()
    {
        var c = CaptainsCallFactory.Create(_alice);

        c.Name.Should().Be("Captain's Call");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CaptainsCall_ManaCost_ParsesCorrectly()
    {
        var parsed = ManaCost.Parse(CaptainsCallFactory.PrintedManaCost);

        parsed.TotalValue.Should().Be(4, "3 generic + 1 white = mana value 4 (CR 202.3)");
        parsed.White.Should().Be(1, "one white pip in {3}{W}");
        parsed.Generic.Should().Be(3, "three generic pips in {3}{W}");
    }
    // -----------------------------------------------------------------------
    // Resolve effect
    // -----------------------------------------------------------------------

    [Fact]
    public void CaptainsCall_Resolve_CreatesThreeWhiteSoldierTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var effects = CaptainsCallFactory.BuildResolveEffect(_alice, zones);
        effects.Should().ContainSingle("Captain's Call resolves with one effect (create three tokens)");

        foreach (var effect in effects) effect.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();
        tokens.Should().HaveCount(CaptainsCallFactory.TokensCreated,
            "Captain's Call creates exactly three Soldier tokens on resolution");

        tokens.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Soldier");
            t.BasePower.Should().Be(CaptainsCallFactory.TokenPower);
            t.BaseToughness.Should().Be(CaptainsCallFactory.TokenToughness);
            t.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
            t.IsToken.Should().BeTrue();
            t.TokenColorsOverride.Should().NotBeNull();
            t.TokenColorsOverride!.Should().Contain(ManaColor.White,
                "tokens are white per oracle text (CR 105 / 111.4)");
        });
    }
}
