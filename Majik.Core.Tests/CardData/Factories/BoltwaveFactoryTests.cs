using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BoltwaveFactory"/> ({R} Sorcery).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Boltwave deals 3 damage to each opponent."
///
/// Covers:
/// - Identity ({R} Sorcery, red) — the {R} analogue of Sizzle ({2}{R}).
/// - Resolve effect deals 3 damage to each opponent (CR 800.4 — "each
///   opponent" enumerates every player other than the caster at resolution).
/// - Caster is unaffected (CR 800.4).
/// - Two-opponent case: both take exactly 3 damage (CR 119.2 — each damage
///   event is independent).
///
/// Dispatch + well-formedness are asserted automatically for every
/// implemented card by CardFactoryContractTests — not re-tested here.
/// </summary>
[Trait("Color", "R")]
public class BoltwaveFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);
    private readonly Player _carol = new("Carol", 20);

    // -------------------------------------------------------------------------
    // Identity — {R} Sorcery
    // -------------------------------------------------------------------------

    [Fact]
    public void Boltwave_Identity_SorceryAtR()
    {
        var boltwave = BoltwaveFactory.Create(_alice);

        boltwave.Name.Should().Be("Boltwave");
        boltwave.HasType(CardType.Sorcery).Should().BeTrue();
        boltwave.ManaCost.ToString().Should().Be("{R}");
        boltwave.Owner.Should().BeSameAs(_alice);
        boltwave.Controller.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------------
    // Resolve: one opponent
    // -------------------------------------------------------------------------

    [Fact]
    public void Boltwave_Resolve_DealsThreeDamageToSingleOpponent()
    {
        // Arrange: Alice casts, Bob is the sole opponent.
        var effects = BoltwaveFactory.BuildResolveEffect(caster: _alice, allPlayers: new[] { _alice, _bob });

        // Act
        foreach (var e in effects) e.Execute();

        // Assert: CR 119.3 — damage to a player reduces their life total.
        _bob.LifeTotal.Should().Be(17, "Boltwave deals 3 damage to Bob (the only opponent)");
        _alice.LifeTotal.Should().Be(20, "Boltwave does not damage the caster (CR 800.4)");
    }

    // -------------------------------------------------------------------------
    // Resolve: caster unaffected
    // -------------------------------------------------------------------------

    [Fact]
    public void Boltwave_Resolve_CasterIsUnaffected()
    {
        // Even when caster is in allPlayers, they take no damage.
        var effects = BoltwaveFactory.BuildResolveEffect(caster: _alice, allPlayers: new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(20, "caster must not be damaged by Boltwave (CR 800.4)");
    }

    // -------------------------------------------------------------------------
    // Resolve: two opponents both take 3
    // -------------------------------------------------------------------------

    [Fact]
    public void Boltwave_Resolve_BothOpponentsTakeThreeDamage_TwoOpponentGame()
    {
        // Arrange: Alice casts, Bob and Carol are both opponents.
        var effects = BoltwaveFactory.BuildResolveEffect(
            caster: _alice,
            allPlayers: new[] { _alice, _bob, _carol });

        // Act
        foreach (var e in effects) e.Execute();

        // Assert: CR 119.2 — each damage event is independent; both
        // opponents lose 3 life from 20.
        _bob.LifeTotal.Should().Be(17,   "Bob (opponent) takes 3 damage from Boltwave");
        _carol.LifeTotal.Should().Be(17, "Carol (opponent) takes 3 damage from Boltwave");
        _alice.LifeTotal.Should().Be(20, "Alice (caster) is unaffected");
    }
}
