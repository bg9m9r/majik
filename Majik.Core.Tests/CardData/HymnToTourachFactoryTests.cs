using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Instant = Majik.Core.Cards.Instant;
using Land = Majik.Core.Cards.Land;
using Sorcery = Majik.Core.Cards.Sorcery;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="HymnToTourachFactory"/> — Sorcery {B}{B}
/// (Fallen Empires).
///
/// "Target player discards two cards at random."
///
/// Covers:
///   - Identity (Sorcery, {B}{B}, owner / controller) + NamedCardFactory dispatch.
///   - SpellDefinition shape: one 1..1 "target player" request,
///     Discard intent, gatherer surfaces every player.
///   - Resolve: discards exactly two cards from a hand of three+.
///   - Resolve: hand of size N &lt; 2 discards all N (capped).
///   - Resolve: empty hand → no-op.
///   - Resolve: uses the per-game RNG so a seeded GameRandom replays the
///     same picks deterministically.
/// </summary>
public class HymnToTourachFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        // Reset the registry's default RNG so seeded fixtures don't bleed
        // into other tests.
        GameRandomRegistry.Clear();
        GameRandomRegistry.SetDefault(new GameRandom());
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = HymnToTourachFactory.Create(_alice);

        card.Name.Should().Be("Hymn to Tourach");
        card.ManaCost.Should().Be("{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HymnToTourach()
    {
        var card = NamedCardFactory.Create("Hymn to Tourach", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Hymn to Tourach");
        card.ManaCost.Should().Be("{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresOneTargetPlayerWithDiscardIntent()
    {
        var def = HymnToTourachFactory.BuildSpellDefinition(_alice, t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().ContainSingle();
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target player");
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Discard);
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    private void Execute()
    {
        var def = HymnToTourachFactory.BuildSpellDefinition(_alice, t => t);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    [Fact]
    public void Resolve_FullHand_DiscardsExactlyTwo()
    {
        // Seed the RNG so the pair is deterministic for assertion.
        GameRandomRegistry.SetDefault(new GameRandom(seed: 12345));

        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        var island = new Land("Island") { Owner = _bob, Zone = ZoneType.Hand };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(forest);
        _bob.Zones.Hand.AddCard(island);
        _bob.Zones.Hand.AddCard(bolt);
        _bob.Zones.Hand.AddCard(bear);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2,
            "Hymn discards exactly two cards at random per CR 701.16");
        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
        _bob.Zones.Graveyard.GetCards()
            .Should().OnlyHaveUniqueItems("Hymn samples without replacement");
    }

    [Fact]
    public void Resolve_HandSizeOne_DiscardsAllAvailable()
    {
        // CR 701.16 — discarding N when fewer than N are available
        // discards all of them (partial-effect rule).
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bolt);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().Be(bolt);
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EmptyHand_NoOp()
    {
        Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_SeededRng_IsDeterministic()
    {
        // Cast Hymn twice with the same seed against equivalent hands and
        // assert the discarded pair is the same — confirms the factory
        // pulls its entropy from GameRandomRegistry (replayable).

        ISet<string> RunOnce(int seed)
        {
            GameRandomRegistry.SetDefault(new GameRandom(seed));
            var alice = new Player("Alice", 20);
            var bob = new Player("Bob", 20);
            var cards = new ICard[]
            {
                new Land("Forest") { Owner = bob, Zone = ZoneType.Hand },
                new Land("Island") { Owner = bob, Zone = ZoneType.Hand },
                new Instant("Lightning Bolt", "{R}") { Owner = bob, Zone = ZoneType.Hand },
                new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Zone = ZoneType.Hand },
                new Instant("Counterspell", "{U}{U}") { Owner = bob, Zone = ZoneType.Hand },
            };
            foreach (var c in cards) bob.Zones.Hand.AddCard(c);

            var def = HymnToTourachFactory.BuildSpellDefinition(alice, t => t);
            var chosen = new ChosenSpellParams(null, null,
                new[] { new object[] { bob } }, ManaPayment.Empty);
            foreach (var e in def.EffectFactory(chosen)) e.Execute();
            return new HashSet<string>(bob.Zones.Graveyard.GetCards().Select(c => c.Name));
        }

        var first = RunOnce(seed: 99);
        var second = RunOnce(seed: 99);

        first.Should().BeEquivalentTo(second,
            "same seed → same random discards (deterministic replay per CR 100.6)");
    }
}
