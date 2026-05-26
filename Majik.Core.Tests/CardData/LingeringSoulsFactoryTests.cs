using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Lingering Souls (Innistrad / Dark Ascension, {2}{W}, Sorcery).
///
/// Coverage:
/// - Identity (name / type / mana cost).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve effect creates two 1/1 white Spirit creature tokens with
///   Flying under the caster.
/// - Flashback {1}{B} alt-cost wiring through
///   <see cref="FlashbackOracleParser"/>.
/// </summary>
public class LingeringSoulsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LingeringSouls_Identity()
    {
        var c = LingeringSoulsFactory.Create(_alice);

        c.Name.Should().Be("Lingering Souls");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LingeringSouls_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Lingering Souls", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Lingering Souls");
    }

    // -----------------------------------------------------------------------
    // Resolve effect
    // -----------------------------------------------------------------------

    [Fact]
    public void LingeringSouls_Resolve_CreatesTwoWhiteSpiritTokensWithFlying()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var effects = LingeringSoulsFactory.BuildResolveEffect(_alice, zones);
        effects.Should().ContainSingle("Lingering Souls resolves with one effect (create two tokens)");

        foreach (var effect in effects) effect.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();
        tokens.Should().HaveCount(LingeringSoulsFactory.TokensCreated,
            "Lingering Souls creates exactly two Spirit tokens on resolution");

        tokens.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Spirit");
            t.BasePower.Should().Be(LingeringSoulsFactory.TokenPower);
            t.BaseToughness.Should().Be(LingeringSoulsFactory.TokenToughness);
            t.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
            t.IsToken.Should().BeTrue();
            t.Abilities.OfType<KeywordAbility>()
                .Should().Contain(k => k.Keyword == "Flying",
                    "each Spirit token has flying");
            t.TokenColorsOverride.Should().NotBeNull();
            t.TokenColorsOverride!.Should().Contain(ManaColor.White,
                "tokens are white per the printed clause (CR 105 / 111.4)");
        });
    }

    // -----------------------------------------------------------------------
    // Flashback {1}{B}
    // -----------------------------------------------------------------------

    [Fact]
    public void LingeringSouls_BuildFlashbackCost_ParsesAsOneBlack()
    {
        var cost = LingeringSoulsFactory.BuildFlashbackCost();

        cost.Should().BeOfType<FlashbackAlternativeCost>();
        var parsed = cost.AlternativeManaCost;
        parsed.Generic.Should().Be(1);
        parsed.Black.Should().Be(1);
        parsed.White.Should().Be(0);
        parsed.TotalValue.Should().Be(2,
            "Flashback {1}{B} has mana value 2 (CR 702.34 / 202.3)");
    }
}
