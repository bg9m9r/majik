using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests;

/// <summary>
/// Exercises HeuristicBotAgent's CR 118.9 alternative-cost election. The
/// agent receives an IAlternativeCostProbe that maps a small set of cards
/// (Lava Dart → Flashback, Skewer the Critics → Spectacle) to the cost
/// shapes used in real decks. The agent must:
///   - Elect flashback when Lava Dart is in the graveyard and the
///     flashback mana cost is affordable (only legal path from yard).
///   - Elect spectacle for {R} on Skewer the Critics when an opponent
///     has lost life this turn, in preference to the printed {2}{R} cost.
/// </summary>
public class HeuristicBotAgentAltCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public async Task ElectsFlashback_WhenLavaDartInGraveyard_AndMountainAvailable()
    {
        // Lava Dart's printed cost is {R}; its flashback cost is "sacrifice
        // two Mountains" — but the test only cares that the bot SURFACES
        // flashback as the elected alt cost when the card sits in the
        // yard. We model the cost as {R} for affordability purposes here
        // (real Lava Dart binding can override; the bot just needs to
        // see a probe-returned IAlternativeCost and elect it).
        var lavaDart = new Instant("Lava Dart", "R");
        lavaDart.ChangeOwner(_alice);
        _alice.Zones.Graveyard.AddCard(lavaDart);

        // Mountain on battlefield to pay {R}. NamedCardFactory wires a
        // tap-for-mana ability so HeuristicBotAgent.TryPickManaSources
        // recognizes it as a {R} source.
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var probe = new FixedAltCostProbe(card => card.Name == "Lava Dart"
            ? new[] { (IAlternativeCost)new FlashbackAlternativeCost(ManaCost.Parse("R")) }
            : System.Array.Empty<IAlternativeCost>());

        var bot = new HeuristicBotAgent(probe);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.Main, new Majik.Core.Stack.Stack());

        var action = await bot.ChoosePriorityActionAsync(ctx);

        var cast = action.Should().BeOfType<PriorityAction.CastSpell>().Subject;
        cast.Card.Should().BeSameAs(lavaDart);
        cast.AlternativeCost.Should().BeOfType<FlashbackAlternativeCost>();
        cast.AlternativeCost!.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));
    }

    [Fact]
    public async Task ElectsSpectacle_ForOneRedMana_WhenOpponentLostLife()
    {
        // Skewer the Critics — printed {2}{R}, spectacle {R}. Opponent
        // at 18 (lost 2 this turn) → spectacle is legal. Bot must prefer
        // the cheaper alt cost over the printed cost.
        var skewer = new Sorcery("Skewer the Critics", "2R");
        skewer.ChangeOwner(_alice);
        _alice.Zones.Hand.AddCard(skewer);

        // Only one Mountain — enough for spectacle's {R}, not enough
        // for printed {2}{R}. That guarantees the bot can't fall into
        // the printed-cost branch as a fallback. NamedCardFactory wires
        // the {R} mana ability.
        var mountain = (Land)NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        // Bob at 18 — lost 2 this turn (starting life 20).
        _bob.LoseLife(2);

        var probe = new FixedAltCostProbe(card => card.Name == "Skewer the Critics"
            ? new[] { (IAlternativeCost)new SpectacleAlternativeCost(
                ManaCost.Parse("R"),
                new[] { _bob }) }
            : System.Array.Empty<IAlternativeCost>());

        var bot = new HeuristicBotAgent(probe);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.Main, new Majik.Core.Stack.Stack());

        var action = await bot.ChoosePriorityActionAsync(ctx);

        var cast = action.Should().BeOfType<PriorityAction.CastSpell>().Subject;
        cast.Card.Should().BeSameAs(skewer);
        cast.AlternativeCost.Should().BeOfType<SpectacleAlternativeCost>();
        cast.AlternativeCost!.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));
    }

    /// <summary>Trivial probe: per-card lookup table baked into a delegate.
    /// Keeps the test free of binder/oracle-parsing wiring while still
    /// exercising the real <see cref="IAlternativeCostProbe"/> seam.</summary>
    private sealed class FixedAltCostProbe : IAlternativeCostProbe
    {
        private readonly Func<ICard, IEnumerable<IAlternativeCost>> _lookup;
        public FixedAltCostProbe(Func<ICard, IEnumerable<IAlternativeCost>> lookup)
            => _lookup = lookup;
        public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
            => _lookup(card);
    }
}
