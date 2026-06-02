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
/// Unit tests for <see cref="AngelsMercyFactory"/> (Magic 2010, {2}{W}{W}).
///
/// Card: Angel's Mercy — Instant {2}{W}{W}.
/// Oracle: "You gain 7 life."
///
/// Covers:
/// - Identity (Instant, {2}{W}{W}, white, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve: caster gains exactly 7 life (CR 119.3).
/// - SpellDefinition shape: no target requests, no modes, no X.
/// </summary>
[Trait("Color", "W")]
public class AngelsMercyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void AngelsMercy_Identity()
    {
        var card = AngelsMercyFactory.Create(_alice);

        card.Name.Should().Be("Angel's Mercy");
        card.ManaCost.Should().Be("{2}{W}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AngelsMercy_IsWhite()
    {
        var card = AngelsMercyFactory.Create(_alice);

        var colors = CardColors.GetColors(card);

        colors.Should().Contain(ManaColor.White,
            "{W}{W} pips in {2}{W}{W} make this a white card");
        colors.Should().HaveCount(1,
            "Angel's Mercy is mono-white");
    }
    // ── Resolve — caster gains 7 life ────────────────────────────────────

    [Fact]
    public void Resolve_CasterGains7Life()
    {
        var startingLife = _alice.LifeTotal;

        var def = AngelsMercyFactory.BuildSpellDefinition(_alice);
        var picks = new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);

        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(startingLife + AngelsMercyFactory.LifeGainAmount,
            "Angel's Mercy grants exactly 7 life to the caster (CR 119.3)");
    }

    [Fact]
    public void Resolve_DoesNotAffectOpponentLifeTotal()
    {
        var bobStart = _bob.LifeTotal;

        var def = AngelsMercyFactory.BuildSpellDefinition(_alice);
        var picks = new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);

        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobStart,
            "Angel's Mercy only affects the caster");
    }

    // ── SpellDefinition shape ───────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasNoTargetRequests_NoModes_NoX()
    {
        var def = AngelsMercyFactory.BuildSpellDefinition(_alice);

        def.TargetRequests.Should().BeEmpty(
            "Angel's Mercy has no targets — it affects only the caster");
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }
}
