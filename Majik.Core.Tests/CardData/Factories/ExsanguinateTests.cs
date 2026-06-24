using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ExsanguinateFactory"/>.
///
/// Card: Exsanguinate — Sorcery {X}{B}{B} (Worldwake / reprints).
///   "Each opponent loses X life. You gain life equal to the life lost this
///    way."
///
/// Covers the card's UNIQUE behaviour (the contract test already asserts
/// dispatch + well-formedness):
///   - Identity (name, type, X-cost {X}{B}{B}, owner/controller).
///   - Resolve drains X life from every opponent (not the caster).
///   - The caster gains life equal to the TOTAL life lost (X × opponents).
///   - X scales the swing; X = 0 is a clean no-op.
///   - A player who has already lost is skipped (CR 800.4a).
///   - No-roster (legacy) callers no-op instead of throwing.
/// </summary>
[Trait("Color", "B")]
public class ExsanguinateTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Exsanguinate_Identity()
    {
        var c = ExsanguinateFactory.Create(_alice);

        c.Name.Should().Be("Exsanguinate");
        c.ManaCost.Should().Be("{X}{B}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — drain each opponent + gain the total
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_EachOpponentLosesX_CasterIsSpared()
    {
        var effects = ExsanguinateFactory.BuildResolveEffect(
            _alice, x: 5, allPlayers: new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(15, "the single opponent loses X");
        _alice.LifeTotal.Should().Be(25, "the caster gains the 5 life lost and never loses any");
    }

    [Fact]
    public void Resolve_CasterGainsTotalLostAcrossAllOpponents()
    {
        var effects = ExsanguinateFactory.BuildResolveEffect(
            _alice, x: 3, allPlayers: new[] { _alice, _bob, _carol });
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(17, "each opponent loses X = 3");
        _carol.LifeTotal.Should().Be(17, "each opponent loses X = 3");
        // CR 119.3 — you gain life equal to the TOTAL life lost: 3 + 3 = 6.
        _alice.LifeTotal.Should().Be(26, "caster gains the total drained (X × opponentCount)");
    }

    [Fact]
    public void Resolve_XScalesTheSwing()
    {
        var effects = ExsanguinateFactory.BuildResolveEffect(
            _alice, x: 8, allPlayers: new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(12);
        _alice.LifeTotal.Should().Be(28);
    }

    [Fact]
    public void Resolve_XZero_IsCleanNoOp()
    {
        var effects = ExsanguinateFactory.BuildResolveEffect(
            _alice, x: 0, allPlayers: new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(20, "X = 0 drains nothing — losing 0 life is not losing life");
        _alice.LifeTotal.Should().Be(20, "no life lost this way, so no lifegain");
    }

    [Fact]
    public void Resolve_SkipsPlayersWhoHaveAlreadyLost()
    {
        // CR 800.4a — a player who has left the game can't lose life.
        _carol.MarkLost();

        var effects = ExsanguinateFactory.BuildResolveEffect(
            _alice, x: 4, allPlayers: new[] { _alice, _bob, _carol });
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(16, "the live opponent loses X");
        // Carol contributes nothing to the life lost this way, so the caster
        // gains only Bob's 4.
        _alice.LifeTotal.Should().Be(24, "caster gains only the life actually lost by live opponents");
    }

    [Fact]
    public void Resolve_NoRoster_IsCleanNoOp()
    {
        var effects = ExsanguinateFactory.BuildResolveEffect(
            _alice, x: 5, allPlayers: null);
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.LifeTotal.Should().Be(20, "no player roster — the drain no-ops rather than throwing");
    }

    // -----------------------------------------------------------------------
    // Production binding — the seed oracle text resolves through the live
    // OracleSpellBinder (NOT just the factory helper). This is the path the
    // real cast flow takes: cards are resolved AT CAST TIME BY NAME via the
    // binder registry, so a working factory helper is meaningless unless a
    // template binds the printed text to it.
    // -----------------------------------------------------------------------

    [Fact]
    public void ProductionBinder_BindsSeedOracleText_AndDrains()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Exsanguinate",
                ManaCost = "{X}{B}{B}",
                OracleText = "Each opponent loses X life. You gain life equal to the life lost this way.",
            },
            _alice, raw => raw, null);

        def.Should().NotBeNull("the binder must recognise Exsanguinate's printed text");
        def!.HasVariableX.Should().BeTrue("{X}{B}{B} is an X-spell — the cast flow must prompt for X");
        def.TargetRequests.Should().BeEmpty("\"each opponent\" is global, not a chosen target (CR 109.5)");

        // Resolve with the cast-time ChosenSpellParams the live flow stamps:
        // X = 4, the full player roster threaded through AllPlayers.
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: 4,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(16, "each opponent loses X = 4 on resolution");
        _alice.LifeTotal.Should().Be(24, "caster gains the 4 life lost this way");
    }
}
