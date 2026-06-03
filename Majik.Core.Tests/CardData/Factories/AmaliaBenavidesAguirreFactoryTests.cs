using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Amalia Benavides Aguirre (The Lost Caverns of Ixalan, {W}{B}).
/// Legendary Creature — Vampire Scout 2/2.
///
/// Oracle text (verified against Scryfall):
///   "Ward—Pay 3 life.
///    Whenever you gain life, Amalia Benavides Aguirre explores. Then destroy
///    all other creatures if its power is exactly 20."
///
/// Coverage:
///   * Identity ({W}{B}, 2/2, Legendary, Vampire Scout, Ward marker).
///   * Lifegain trigger fires Amalia's own explore (CR 701.40 — non-land top
///     puts a +1/+1 counter on Amalia herself).
///   * "Then destroy all other creatures if its power is exactly 20" — the
///     board wipe is gated on Amalia's CURRENT power being exactly 20 (after
///     the explore), spares Amalia, and sweeps every OTHER creature.
/// </summary>
[Trait("Color", "WB")]
public class AmaliaBenavidesAguirreFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    private void ExecuteTrigger(Creature card, IPlayerAgent? agent = null)
    {
        var trigger = card.Abilities.OfType<TriggeredAbility>().First();
        var controller = card.Controller ?? _alice;
        var game = new Majik.Core.Game.GameContext(
            self: controller,
            allPlayers: new[] { _alice, _bob },
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));
        var ctx = Majik.Core.Abilities.ResolutionContext.For(
            controller, agent, game, chosenTargets: null);
        foreach (var effect in trigger.Effects)
        {
            effect.ExecuteAsync(ctx).AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void Identity_Wb_LegendaryVampireScout_Ward()
    {
        var c = AmaliaBenavidesAguirreFactory.Create(_alice);
        c.Name.Should().Be("Amalia Benavides Aguirre");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.ManaCost.Should().Be("{W}{B}");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("CR 205.4 — Legendary creature");
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.Abilities.OfType<KeywordAbility>().Any(k => k.Keyword == "Ward")
            .Should().BeTrue("CR 702.21 — Ward—Pay 3 life");
        c.Abilities.OfType<TriggeredAbility>().Should()
            .ContainSingle("the lifegain explore + conditional wipe trigger");
    }

    [Fact]
    public void Lifegain_Explores_NonLandTop_CounterOnSelf()
    {
        var nonLand = new Creature("Big", "{G}", 3, 3);
        _alice.Zones.Library.AddCard(nonLand);
        var agent = new ScriptedAgent();
        agent.QueueExploreKeepOnTop(true);
        AgentRegistry.Set(_alice, agent);

        var amalia = AmaliaBenavidesAguirreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(amalia);
        amalia.SetZone(ZoneType.Battlefield);

        ExecuteTrigger(amalia, agent);

        amalia.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.40c — the +1/+1 counter lands on Amalia (the exploring creature)");
    }

    [Fact]
    public void Lifegain_PowerNotTwenty_NoBoardWipe()
    {
        var land = new Land("Plains");
        _alice.Zones.Library.AddCard(land); // land top → no counter, power stays 2

        var amalia = AmaliaBenavidesAguirreFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(amalia);
        amalia.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        ExecuteTrigger(amalia);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "Amalia's power is 2 (not 20) — no board wipe");
    }

    [Fact]
    public void Lifegain_PowerExactlyTwenty_DestroysAllOtherCreatures_SparesAmalia()
    {
        var land = new Land("Plains");
        _alice.Zones.Library.AddCard(land); // land top → explore leaves power unchanged

        var amalia = AmaliaBenavidesAguirreFactory.Create(_alice);
        // Wire the layer system so +1/+1 counters feed Amalia's effective power
        // (CR 711 reads power through the continuous-effects service).
        amalia.ActiveEffects = new Majik.Core.Effects.ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(amalia);
        amalia.SetZone(ZoneType.Battlefield);
        // Pump Amalia to exactly 20 power via 18 +1/+1 counters (base 2).
        amalia.Counters.Add(CounterType.PlusOnePlusOne, 18);
        amalia.Power.Should().Be(20, "base 2 + 18 counters = 20");

        var ally = new Creature("Ally", "{G}", 1, 1) { Owner = _alice };
        _alice.Zones.Battlefield.AddCard(ally);
        ally.SetZone(ZoneType.Battlefield);
        var enemy = new Creature("Enemy", "{1}{R}", 3, 3) { Owner = _bob };
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.SetZone(ZoneType.Battlefield);

        ExecuteTrigger(amalia);

        amalia.Zone.Should().Be(ZoneType.Battlefield,
            "'all OTHER creatures' spares Amalia");
        ally.Zone.Should().Be(ZoneType.Graveyard, "your own creatures are destroyed too");
        enemy.Zone.Should().Be(ZoneType.Graveyard, "opponents' creatures are destroyed");
    }
}
