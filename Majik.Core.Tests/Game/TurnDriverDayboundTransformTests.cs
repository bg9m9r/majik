using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// Integration tests proving daybound/nightbound permanents transform live
/// when the day/night designation flips during a real driven turn sequence
/// (CR 702.145c / 702.145f wired into <see cref="TurnDriver"/>).
/// </summary>
public class TurnDriverDayboundTransformTests
{
    private static Creature NewWerewolf(Player owner)
    {
        var c = new Creature("Day Wolf", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Werewolf });
        c.SetOwner(owner);
        c.SetController(owner);
        c.MdfcState = new MdfcState("Day Wolf", "Night Wolf");
        c.AddAbility(new KeywordAbility(DayboundNightbound.DayboundKeyword, c, owner));
        c.AddAbility(new KeywordAbility(DayboundNightbound.NightboundKeyword, c, owner));
        return c;
    }

    private sealed class Harness
    {
        public EventBus Bus { get; }
        public TurnDriver Driver { get; }
        public Player Alice { get; }
        public Player Bob { get; }

        public Harness()
        {
            Bus = new EventBus();
            var stack = new Majik.Core.Stack.Stack(Bus);
            var zones = new ZoneService(Bus);
            var triggers = new TriggerManager(stack, Bus);
            var resolver = new StackResolver(Bus, zones);
            var sba = new StateBasedActions(Bus, zones, triggers);
            Alice = new Player("Alice", 20);
            Bob = new Player("Bob", 20);
            var players = new List<Player> { Alice, Bob };
            var priority = new PriorityManager(players, stack, Bus, triggers);

            foreach (var p in players)
            {
                for (var i = 0; i < 10; i++)
                {
                    var c = NamedCardFactory.Create("Mountain", p);
                    p.Zones.Library.AddCard(c);
                    c.SetZone(ZoneType.Library);
                }
            }

            Driver = new TurnDriver(
                players,
                new Dictionary<Player, IPlayerAgent>
                {
                    [Alice] = new DeterministicBotAgent(),
                    [Bob] = new DeterministicBotAgent(),
                },
                stack, zones, triggers, resolver, sba, priority,
                new CombatFlow(Bus, sba),
                eventBus: Bus);
        }
    }

    [Fact]
    public async Task BecomesNight_FrontFaceWerewolfTransformsToBack()
    {
        var h = new Harness();
        var wolf = NewWerewolf(h.Alice);
        h.Alice.Zones.Battlefield.AddCard(wolf);
        wolf.SetZone(ZoneType.Battlefield);

        h.Driver.DayNight.BecomeDay();
        wolf.MdfcState!.IsBackFace.Should().BeFalse();

        // Turn 1 (Alice) casts nothing → records 0 spells.
        await h.Driver.RunTurnAsync(h.Alice, turnNumber: 1);

        // Turn 2 (Bob) untap check: day + Alice cast 0 → night → wolf flips.
        await h.Driver.RunTurnAsync(h.Bob, turnNumber: 2);

        h.Driver.DayNight.IsNight.Should().BeTrue();
        wolf.MdfcState!.IsBackFace.Should().BeTrue("front-face daybound werewolf transforms when it becomes night");
    }
}
