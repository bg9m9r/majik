using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Spectral Procession (Shadowmoor, {(2/W)}{(2/W)}{(2/W)}, Sorcery).
///
/// Coverage:
/// - Identity (name / type / printed twobrid mana cost).
/// - Mana cost parses as three twobrid hybrid pips with mana value 6
///   (CR 107.4e / CR 202.3f).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve effect creates three 1/1 white Spirit creature tokens with
///   Flying under the caster.
/// </summary>
public class SpectralProcessionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SpectralProcession_Identity()
    {
        var c = SpectralProcessionFactory.Create(_alice);

        c.Name.Should().Be("Spectral Procession");
        c.ManaCost.Should().Be("{2/W}{2/W}{2/W}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpectralProcession_ManaCost_ParsesAsThreeTwobridPips()
    {
        var parsed = ManaCost.Parse(SpectralProcessionFactory.PrintedManaCost);

        parsed.HybridPips.Should().HaveCount(3,
            "three {2/W} twobrid pips per the printed cost");
        parsed.HybridPips.Should().AllSatisfy(p =>
        {
            p.GenericAlternative.Should().Be(2,
                "each pip is {2/W} — 2 generic or 1 white (CR 202.3f)");
        });

        parsed.TotalValue.Should().Be(6,
            "TotalValue uses the higher generic alternative per twobrid pip → 3 × 2 = 6");
    }

    [Fact]
    public void SpectralProcession_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Spectral Procession", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Spectral Procession");
    }

    // -----------------------------------------------------------------------
    // Resolve effect
    // -----------------------------------------------------------------------

    [Fact]
    public void SpectralProcession_Resolve_CreatesThreeWhiteSpiritTokensWithFlying()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var effects = SpectralProcessionFactory.BuildResolveEffect(_alice, zones);
        effects.Should().ContainSingle("Spectral Procession resolves with one effect (create three tokens)");

        foreach (var effect in effects) effect.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();
        tokens.Should().HaveCount(SpectralProcessionFactory.TokensCreated,
            "Spectral Procession creates three Spirit tokens on resolution");

        tokens.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Spirit");
            t.BasePower.Should().Be(SpectralProcessionFactory.TokenPower);
            t.BaseToughness.Should().Be(SpectralProcessionFactory.TokenToughness);
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
}
