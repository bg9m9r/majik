using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Cards;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// Integration tests for the day/night untap-step check wired into
/// <see cref="TurnDriver"/> (CR 502.2 / 730.2). Drives real turns and asserts
/// the <see cref="TurnDriver.DayNight"/> designation transitions based on the
/// PREVIOUS turn's active player's spell count, and that a
/// <see cref="DayNightChangedEvent"/> is published on each change.
/// </summary>
public class TurnDriverDayNightTests
{
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

        /// <summary>
        /// Register a one-shot publisher that, on <paramref name="caster"/>'s
        /// upkeep step, publishes <paramref name="count"/> SpellCastEvents so
        /// the TurnState records that many spells cast for that player THIS
        /// turn (mirrors the production OnSpellCast funnel).
        /// </summary>
        public void CastSpellsOnUpkeep(Player caster, int count)
        {
            void Handler(StepStartedEvent e)
            {
                if (e.StepType != Majik.Core.StateMachine.PhaseStateType.Upkeep) return;
                if (!ReferenceEquals(e.Player, caster)) return;
                Bus.Unsubscribe<StepStartedEvent>(Handler);
                for (var i = 0; i < count; i++)
                {
                    var creature = new Creature($"Spell{i}", "{R}", 1, 1) { Owner = caster };
                    Bus.Publish(new SpellCastEvent(new Majik.Core.Spells.Spell(creature, caster)));
                }
            }
            Bus.Subscribe<StepStartedEvent>(Handler);
        }
    }

    [Fact]
    public async Task Day_PreviousActivePlayerCastNoSpells_BecomesNightAtUntap()
    {
        var h = new Harness();
        h.Driver.DayNight.BecomeDay();

        DayNightDesignation? lastChange = null;
        h.Bus.Subscribe<DayNightChangedEvent>(e => lastChange = e.NewDesignation);

        // Turn 1 (Alice) — she casts no spells. Records her as the previous
        // active player with a 0 spell count.
        await h.Driver.RunTurnAsync(h.Alice, turnNumber: 1);
        h.Driver.DayNight.IsDay.Should().BeTrue("untap check on turn 1 has no previous player");

        // Turn 2 (Bob) — untap check reads Alice's turn-1 count (0): day → night.
        await h.Driver.RunTurnAsync(h.Bob, turnNumber: 2);

        h.Driver.DayNight.IsNight.Should().BeTrue();
        lastChange.Should().Be(DayNightDesignation.Night);
    }

    [Fact]
    public async Task Night_PreviousActivePlayerCastTwoSpells_BecomesDayAtUntap()
    {
        var h = new Harness();
        h.Driver.DayNight.BecomeNight();
        h.CastSpellsOnUpkeep(h.Alice, 2);

        DayNightDesignation? lastChange = null;
        h.Bus.Subscribe<DayNightChangedEvent>(e => lastChange = e.NewDesignation);

        // Turn 1 (Alice) — she casts two spells this turn.
        await h.Driver.RunTurnAsync(h.Alice, turnNumber: 1);

        // Turn 2 (Bob) — untap check reads Alice's turn-1 count (2): night → day.
        await h.Driver.RunTurnAsync(h.Bob, turnNumber: 2);

        h.Driver.DayNight.IsDay.Should().BeTrue();
        lastChange.Should().Be(DayNightDesignation.Day);
    }

    [Fact]
    public async Task Day_PreviousActivePlayerCastOneSpell_StaysDay()
    {
        var h = new Harness();
        h.Driver.DayNight.BecomeDay();
        h.CastSpellsOnUpkeep(h.Alice, 1);

        await h.Driver.RunTurnAsync(h.Alice, turnNumber: 1);
        await h.Driver.RunTurnAsync(h.Bob, turnNumber: 2);

        h.Driver.DayNight.IsDay.Should().BeTrue();
    }

    [Fact]
    public async Task Neither_StaysNeitherAcrossTurns()
    {
        var h = new Harness();
        // No BecomeDay/BecomeNight — game starts "neither" (CR 730.1 / 730.2c).

        await h.Driver.RunTurnAsync(h.Alice, turnNumber: 1);
        await h.Driver.RunTurnAsync(h.Bob, turnNumber: 2);

        h.Driver.DayNight.IsNeither.Should().BeTrue();
    }
}
