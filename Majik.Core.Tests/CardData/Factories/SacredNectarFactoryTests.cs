using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SacredNectarFactory"/> (Exodus, {1}{W}).
///
/// Card: Sacred Nectar — Sorcery {1}{W}.
/// Oracle: "You gain 4 life."
///
/// Covers:
/// - Identity (Sorcery, {1}{W}, white, mana value 2, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve: caster gains exactly 4 life (CR 119.3).
/// - SpellDefinition shape: no target requests, no modes, no X.
/// </summary>
[Trait("Color", "W")]
public class SacredNectarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void SacredNectar_Identity()
    {
        var card = SacredNectarFactory.Create(_alice);

        card.Name.Should().Be("Sacred Nectar");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SacredNectar_ManaValue_Is2()
    {
        var card = SacredNectarFactory.Create(_alice);

        card.ManaCost.Should().Be("{1}{W}",
            "Sacred Nectar costs {1}{W}, mana value 2");
    }

    [Fact]
    public void SacredNectar_IsWhite()
    {
        var card = SacredNectarFactory.Create(_alice);

        var colors = CardColors.GetColors(card);

        colors.Should().Contain(ManaColor.White,
            "{W} pip in {1}{W} makes this a white card");
        colors.Should().HaveCount(1,
            "Sacred Nectar is mono-white");
    }
    // ── Resolve — caster gains 4 life ────────────────────────────────────

    [Fact]
    public void Resolve_CasterGains4Life()
    {
        var startingLife = _alice.LifeTotal;

        var def = SacredNectarFactory.BuildSpellDefinition(_alice);
        var picks = new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);

        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(startingLife + SacredNectarFactory.LifeGainAmount,
            "Sacred Nectar grants exactly 4 life to the caster (CR 119.3)");
    }

    [Fact]
    public void Resolve_DoesNotAffectOpponentLifeTotal()
    {
        var bobStart = _bob.LifeTotal;

        var def = SacredNectarFactory.BuildSpellDefinition(_alice);
        var picks = new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);

        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobStart,
            "Sacred Nectar only affects the caster");
    }

    // ── SpellDefinition shape ───────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasNoTargetRequests_NoModes_NoX()
    {
        var def = SacredNectarFactory.BuildSpellDefinition(_alice);

        def.TargetRequests.Should().BeEmpty(
            "Sacred Nectar has no targets — it affects only the caster");
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }
}
