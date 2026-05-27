using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="LurkingRoperFactory"/> (Bloomburrow, {3}{B}).
///
/// Card: Lurking Roper — Creature — Snake Horror 4/3.
///   "Forage
///    When this creature enters, each opponent mills three cards."
///
/// Covers:
///   - Identity / dispatch.
///   - ETB trigger mills each opponent's top 3 library cards.
///   - Controller is NOT milled (one-sided "each opponent").
///   - No-opponent / no-resolver paths are clean no-ops.
/// </summary>
public class LurkingRoperTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LurkingRoper_Identity()
    {
        var c = LurkingRoperFactory.Create(_alice);

        c.Name.Should().Be("Lurking Roper");
        c.ManaCost.Should().Be("{3}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(3);
        c.Subtypes.Should().Contain(CardSubtype.Snake);
        c.Subtypes.Should().Contain(CardSubtype.Horror);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LurkingRoper()
    {
        var card = NamedCardFactory.Create("Lurking Roper", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Lurking Roper");
    }

    [Fact]
    public void HasForageKeyword()
    {
        var roper = LurkingRoperFactory.Create(_alice);
        var keywords = roper.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Forage");
    }

    [Fact]
    public void EtbTrigger_MillsEachOpponentThreeCards()
    {
        // Seed Bob's library with 5 placeholder cards.
        for (var i = 0; i < 5; i++)
        {
            var c = new Creature($"BobCard{i}", "{1}", 1, 1);
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
        }

        var roper = LurkingRoperFactory.Create(
            _alice,
            triggers: null,
            opponentResolver: () => new[] { _bob });

        // Execute the ETB effect directly (same pattern HardenedScalesTests
        // uses for Champion of the Parish / Sprite Dragon).
        var trigger = roper.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var fx in trigger.Effects) fx.Execute();

        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3,
            "Lurking Roper mills 3 from each opponent");
        _bob.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void EtbTrigger_DoesNotMillController()
    {
        for (var i = 0; i < 5; i++)
        {
            var c = new Creature($"AliceCard{i}", "{1}", 1, 1);
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
        }

        var roper = LurkingRoperFactory.Create(
            _alice,
            triggers: null,
            // Resolver mistakenly includes the controller — the factory
            // skips ReferenceEquals matches.
            opponentResolver: () => new[] { _alice });

        var trigger = roper.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var fx in trigger.Effects) fx.Execute();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "Lurking Roper's ETB mills 'each opponent', never the controller");
        _alice.Zones.Library.GetCards().Should().HaveCount(5);
    }

    [Fact]
    public void EtbTrigger_NullResolver_NoOps()
    {
        var roper = LurkingRoperFactory.Create(_alice);

        var trigger = roper.Abilities.OfType<TriggeredAbility>().Single();
        // Should not throw despite null resolver.
        Action act = () =>
        {
            foreach (var fx in trigger.Effects) fx.Execute();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void EtbTrigger_LibrarySmallerThanThree_MillsRemainder()
    {
        var c1 = new Creature("X", "{1}", 1, 1); c1.SetOwner(_bob);
        var c2 = new Creature("Y", "{1}", 1, 1); c2.SetOwner(_bob);
        _bob.Zones.Library.AddCard(c1);
        _bob.Zones.Library.AddCard(c2);

        var roper = LurkingRoperFactory.Create(
            _alice,
            triggers: null,
            opponentResolver: () => new[] { _bob });

        var trigger = roper.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var fx in trigger.Effects) fx.Execute();

        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2,
            "CR 701.13 — fewer than N cards left, mill all remaining");
        _bob.Zones.Library.GetCards().Should().BeEmpty();
    }
}
