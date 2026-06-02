using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BoltwaveFactory"/> ({R} Sorcery).
///
/// Covers:
/// - Identity ({R} Sorcery, mana value 1, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="BoltwaveFactory.BuildResolveEffect"/> deals 3 damage to
///   each opponent of the caster — one-opponent and two-opponent cases.
/// - Caster takes no damage even if listed among opponents (defensive guard).
/// </summary>
[Trait("Color", "R")]
public class BoltwaveFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Boltwave_Identity_SorceryAtR()
    {
        var card = BoltwaveFactory.Create(_alice);

        card.Name.Should().Be("Boltwave");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Boltwave_ManaValue_IsOne()
    {
        var card = BoltwaveFactory.Create(_alice);

        // {R} → one coloured pip = mana value 1 (CR 202.3).
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(1, "single {R} pip → CMC 1");
    }
    // -----------------------------------------------------------------------
    // Resolve — each-opponent damage
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildResolveEffect_DealsThreeDamage_ToSingleOpponent()
    {
        var effects = BoltwaveFactory.BuildResolveEffect(_alice, new[] { _bob });

        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(17, "Bob takes 3 damage from Boltwave");
        _alice.LifeTotal.Should().Be(20, "Alice (caster) is not affected");
    }

    [Fact]
    public void BuildResolveEffect_DealsThreeDamage_ToEachOfTwoOpponents()
    {
        var effects = BoltwaveFactory.BuildResolveEffect(_alice, new[] { _bob, _carol });

        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(17,   "Bob takes 3 damage from Boltwave");
        _carol.LifeTotal.Should().Be(17, "Carol takes 3 damage from Boltwave");
        _alice.LifeTotal.Should().Be(20, "Alice (caster) is not affected");
    }

    [Fact]
    public void BuildResolveEffect_DoesNotDamageCaster_EvenIfListedAsOpponent()
    {
        // Defensive — mirrors the CreepingChill controller-skip guard.
        var effects = BoltwaveFactory.BuildResolveEffect(_alice, new[] { _alice, _bob });

        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(20, "Alice is the caster and must not be damaged");
        _bob.LifeTotal.Should().Be(17,   "Bob takes 3 damage");
    }
}
