using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Bot.Tests.Helpers;

/// <summary>
/// Builds a minimal GameContext + two Players for bot unit tests. No real
/// engine loop — tests construct exact board states via direct zone
/// manipulation.
/// </summary>
public sealed class BotTestScenario
{
    public Player Self { get; }
    public Player Opponent { get; }
    public GameContext Context { get; }
    public EventBus Bus { get; }
    public Majik.Core.Stack.Stack Stack { get; }

    public BotTestScenario(int selfLife = 20, int oppLife = 20)
    {
        Self = new Player("Bot", selfLife);
        Opponent = new Player("Human", oppLife);
        Bus = new EventBus();
        Stack = new Majik.Core.Stack.Stack(Bus);
        Context = new GameContext(
            self: Self,
            allPlayers: new[] { Self, Opponent },
            activePlayer: Self,
            turnNumber: 1,
            currentPhase: PhaseStateType.Main,
            stack: Stack);
    }

    public Creature AddCreatureToBattlefield(Player p, string name, int power, int toughness)
    {
        var c = new Creature(name, manaCost: string.Empty, power: power, toughness: toughness);
        c.ChangeOwner(p);
        p.Zones.Battlefield.AddCard(c);
        return c;
    }

    public Land AddLandToBattlefield(Player p, string name)
    {
        var l = new Land(name);
        l.ChangeOwner(p);
        p.Zones.Battlefield.AddCard(l);
        return l;
    }

    public void AddCardToHand(Player p, Card card)
    {
        card.ChangeOwner(p);
        p.Zones.Hand.AddCard(card);
    }
}
