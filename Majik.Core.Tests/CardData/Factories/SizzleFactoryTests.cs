using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SizzleFactory"/> (Onslaught, {2}{R}).
///
/// Sorcery. Oracle text:
///   "Sizzle deals 3 damage to each opponent."
///
/// Covers:
/// - Identity ({2}{R} Sorcery, red).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve effect deals 3 damage to each opponent (CR 109.5 — "each
///   opponent" enumerates every player other than the caster at
///   resolution time).
/// - Caster is unaffected (CR 800.4 — "opponent" excludes the active
///   player themselves).
/// - Two-opponent case: both take exactly 3 damage (CR 119.2 — each
///   damage event is independent).
/// </summary>
[Trait("Color", "R")]
public class SizzleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);
    private readonly Player _carol = new("Carol", 20);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Sizzle_Identity_SorceryAt2R()
    {
        var sizzle = SizzleFactory.Create(_alice);

        sizzle.Name.Should().Be("Sizzle");
        sizzle.HasType(CardType.Sorcery).Should().BeTrue();
        sizzle.ManaCost.ToString().Should().Be("{2}{R}");
        sizzle.Owner.Should().BeSameAs(_alice);
        sizzle.Controller.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------------
    // Dispatch
    // -------------------------------------------------------------------------
    // -------------------------------------------------------------------------
    // Resolve: one opponent
    // -------------------------------------------------------------------------

    [Fact]
    public void Sizzle_Resolve_DealsThreeDamageToSingleOpponent()
    {
        // Arrange: Alice casts, Bob is the sole opponent.
        var effects = SizzleFactory.BuildResolveEffect(caster: _alice, allPlayers: new[] { _alice, _bob });

        // Act
        foreach (var e in effects) e.Execute();

        // Assert: CR 119.3 — damage to a player reduces their life total.
        _bob.LifeTotal.Should().Be(17, "Sizzle deals 3 damage to Bob (the only opponent)");
        _alice.LifeTotal.Should().Be(20, "Sizzle does not damage the caster (CR 800.4)");
    }

    // -------------------------------------------------------------------------
    // Resolve: caster unaffected
    // -------------------------------------------------------------------------

    [Fact]
    public void Sizzle_Resolve_CasterIsUnaffected()
    {
        // Even when caster is in allPlayers, they take no damage.
        var effects = SizzleFactory.BuildResolveEffect(caster: _alice, allPlayers: new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(20, "caster must not be damaged by Sizzle (CR 800.4)");
    }

    // -------------------------------------------------------------------------
    // Resolve: two opponents both take 3
    // -------------------------------------------------------------------------

    [Fact]
    public void Sizzle_Resolve_BothOpponentsTakeThreeDamage_TwoOpponentGame()
    {
        // Arrange: Alice casts, Bob and Carol are both opponents.
        var effects = SizzleFactory.BuildResolveEffect(
            caster: _alice,
            allPlayers: new[] { _alice, _bob, _carol });

        // Act
        foreach (var e in effects) e.Execute();

        // Assert: CR 119.2 — each damage event is independent; both
        // opponents lose 3 life from 20.
        _bob.LifeTotal.Should().Be(17,   "Bob (opponent) takes 3 damage from Sizzle");
        _carol.LifeTotal.Should().Be(17, "Carol (opponent) takes 3 damage from Sizzle");
        _alice.LifeTotal.Should().Be(20, "Alice (caster) is unaffected");
    }
}
