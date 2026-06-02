using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Marauding Blight-Priest (Zendikar Rising, {2}{B}).
///
/// Creature — Vampire Cleric 3/2. Oracle text:
///   "Whenever you gain life, each opponent loses 1 life."
///
/// ## Shape source
/// Card identity (name, {2}{B}, 3/2, Creature — Vampire Cleric) is loaded from
/// <c>Majik.Core/CardData/Cards/marauding-blight-priest.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The lifegain triggered ability is
/// attached in code below — same shape as the suggested analogue
/// <see cref="CliffhavenVampireFactory"/> (and a fixed-amount sibling of
/// <see cref="VitoThornOfTheDuskRoseFactory"/>, which drains "that much"
/// instead of a flat 1).
///
/// ## Implemented (v1)
/// - 3/2 Creature — Vampire Cleric (CR 205.3m) at {2}{B}, owner / controller
///   wired.
/// - <b>Lifegain triggered ability (CR 603.6a / CR 119.3)</b>:
///   "Whenever you gain life, each opponent loses 1 life." Wired via
///   <see cref="Triggers.OnLifeGainedByPlayer"/> consuming
///   <see cref="LifeChangedEvent"/> filtered to the controller AND
///   strictly-positive deltas (NewLife &gt; PreviousLife — life *gain*, not
///   life loss). The "each opponent" clause (CR 109.5 — no targets, global)
///   reads from the optional <c>opponentResolver</c> closure (Sheoldred /
///   Cliffhaven shape: the factory doesn't reach into a global player list at
///   construction; the engine / tests feed in <c>Game.Players</c> at wire-up).
///   Each opponent returned by the resolver (filtered to non-controller)
///   loses 1 life via <see cref="Player.LoseLife"/>.
///
/// ## Lifecycle
/// - Single-arg <see cref="Create(Player)"/> attaches the trigger for shape
///   inspection but registers nothing and leaves the opponent resolver null —
///   the drain clause silently no-ops.
/// - Full overload accepts an <see cref="IEventBus"/> + <see cref="TriggerManager"/>
///   so bus-fired <see cref="LifeChangedEvent"/>s auto-queue the trigger, plus
///   an opponent-list closure resolved at trigger-resolution time (mirrors
///   <see cref="CliffhavenVampireFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Live opponent enumeration without a resolver</b>: same gap as
///   <see cref="CliffhavenVampireFactory"/> / <see cref="SheoldredTheApocalypseFactory"/>
///   — <c>Player</c> doesn't expose an opponent list, so the factory leans on
///   a caller-supplied resolver. Tests + the engine wire-up site feed it in.
/// </summary>
[CardName("Marauding Blight-Priest")]
public static class MaraudingBlightPriestFactory
{
    public const string CardName = "Marauding Blight-Priest";
    public const int LifeLossPerOpponent = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("marauding-blight-priest");

    /// <summary>
    /// Construct Marauding Blight-Priest with no live runtime services. The
    /// lifegain trigger is attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>) and the drain clause is a no-op (no
    /// opponent resolver). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Marauding Blight-Priest. When <paramref name="opponentResolver"/>
    /// is supplied, the lifegain trigger drains 1 life from every player it
    /// returns (filtered to non-controller). When <paramref name="triggers"/> is
    /// supplied, the trigger is registered so a controller-scoped
    /// <see cref="LifeChangedEvent"/> places it on the stack automatically
    /// (CR 603.3).
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Lifegain trigger — CR 603.6a / 119.3.
        //   "Whenever you gain life, each opponent loses 1 life."
        // Triggers.OnLifeGainedByPlayer filters LifeChangedEvent to the
        // controller AND NewLife > PreviousLife (strictly-positive delta).
        // Resolution: each opponent returned by the resolver loses 1 life.
        // No targets — "each opponent" is global (CR 109.5). Same shape as
        // Cliffhaven Vampire's drain.
        // ----------------------------------------------------------------
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses {LifeLossPerOpponent} life",
            () =>
            {
                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;
                foreach (var opp in opponents)
                {
                    if (opp == null) continue;
                    if (ReferenceEquals(opp, owner)) continue;
                    opp.LoseLife(LifeLossPerOpponent);
                }
            });

        var lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLifeGainedByPlayer(owner),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lifegainTrigger);
        triggers?.RegisterTriggeredAbility(lifegainTrigger);

        // The event bus drives bus-fired LifeChangedEvents into the registered
        // TriggerManager; no Vito-style "that much" amount snapshot is needed
        // because the drain amount is a flat 1 (CR 603.7 is moot here).
        _ = eventBus;

        return card;
    }
}
