using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Regression for the GoryoVengeance-vs-Yawg (seed 0) runaway loop: Young Wolf
/// (1/1, undying) with a +1/+1 counter gains Walking Ballista's "Remove a +1/+1
/// counter from this creature: it deals 1 damage to any target" through
/// Agatha's Soul Cauldron (CR 702.49 imprint grant, Layer 6). It is a 2/2;
/// paying the cost leaves a 1/1 that survives. Root cause of the loop: the
/// 1 damage the Wolf had when it first died stayed marked on it after undying
/// returned it (marked damage was not cleared on a zone change, CR 400.7), so
/// every counter removal left a 1/1 with lethal damage (CR 704.5g): it died,
/// came back with a counter, pinged, died again, forever.
/// </summary>
public class YoungWolfCauldronLoopTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    [Fact]
    public void AYoungWolfWithACounter_GrantedBallistasAbility_SurvivesPayingItsCost()
    {
        var alice = new Majik.Core.Players.Player("Alice", 20);
        var bus = new Majik.Core.Events.EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);
        var zones = new Majik.Core.Services.ZoneService(bus);

        var ballista = WalkingBallistaFactory.Create(alice);
        alice.Zones.Graveyard.AddCard(ballista);
        ballista.SetZone(ZoneType.Graveyard);

        var wolf = YoungWolfFactory.Create(alice);
        wolf.ChangeController(alice);
        alice.Zones.Library.AddCard(wolf);
        zones.MoveCard(wolf, ZoneType.Library, ZoneType.Battlefield, alice);
        wolf.ActiveEffects = effects;
        // As in the game: it was dealt 1 damage and died, and undying returned
        // it with a +1/+1 counter. CR 400.7 — the returned Wolf is a new object
        // with no damage marked on it.
        wolf.TakeDamage(1);
        zones.MoveCard(wolf, ZoneType.Battlefield, ZoneType.Graveyard, alice);
        zones.MoveCard(wolf, ZoneType.Graveyard, ZoneType.Battlefield, alice);
        wolf.Damage.Should().Be(0, "damage does not survive a zone change (CR 400.7)");
        wolf.Counters.Add(CounterType.PlusOnePlusOne, 1);
        wolf.ClearSummoningSickness();

        var cauldron = AgathasSoulCauldronFactory.Create(alice, effects);
        alice.Zones.Library.AddCard(cauldron);
        zones.MoveCard(cauldron, ZoneType.Library, ZoneType.Battlefield, alice);
        var tap = cauldron.Abilities.OfType<ActivatedAbility>().Single();
        tap.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ballista }, System.Array.Empty<object>() });
        foreach (var e in tap.Effects) e.Execute();

        (wolf.Power, wolf.Toughness).Should().Be((2, 2), "Young Wolf is a 1/1 with a +1/+1 counter");
        var ping = wolf.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.Any(c => c.Description.Contains("counter", System.StringComparison.OrdinalIgnoreCase)));

        foreach (var cost in ping.Costs) cost.Pay(alice);

        wolf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        (wolf.Power, wolf.Toughness).Should().Be((1, 1));
        wolf.IsDead().Should().BeFalse("a 1/1 Young Wolf with no damage survives (CR 704.5f, 704.5g)");
    }
}
