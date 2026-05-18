using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class EndOfTurnExpiryTests
{
    [Fact]
    public async Task GiantGrowthEquivalent_PumpExpiresAtEndOfTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var continuous = new ContinuousEffectsService();
        var combat = new CombatFlow(bus, sba);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        for (var i = 0; i < 5; i++)
        {
            var c = NamedCardFactory.Create("Mountain", alice);
            alice.Zones.Library.AddCard(c); c.Zone = ZoneType.Library;
        }

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        alice.Zones.Battlefield.AddCard(bear);
        bear.HasSummoningSickness = false;

        // Apply "Giant Growth": +3/+3 until end of turn
        continuous.Register(new PumpUntilEndOfTurnEffect(bear, 3, 3));
        bear.Power.Should().Be(5);

        var players = new List<Player> { alice, bob };
        var priorityMgr = new PriorityManager(players, stack, bus, triggers);
        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new DeterministicBotAgent(),
                [bob] = new DeterministicBotAgent(),
            },
            stack, zones, triggers, resolver, sba, priorityMgr, combat,
            continuous);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        // After end-of-turn cleanup, pump expires.
        bear.Power.Should().Be(2);
    }

    private sealed class PumpUntilEndOfTurnEffect : ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p, _t;
        public PumpUntilEndOfTurnEffect(Creature target, int p, int t)
        {
            _target = target; _p = p; _t = t;
        }
        public override Layer Layer => Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => true;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _t; }
    }
}
