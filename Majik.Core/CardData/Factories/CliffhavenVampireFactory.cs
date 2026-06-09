using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cliffhaven Vampire (Battle for Zendikar,
/// {1}{W}{B}).
///
/// Creature — Vampire Warrior Ally 2/4. Oracle text:
///   "Flying
///    Whenever you gain life, each opponent loses 1 life."
///
/// ## Implemented (v1)
/// - 2/4 Creature — Vampire Warrior Ally, mana cost {1}{W}{B}, owner / controller
///   wired.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> reads it for
///   block-legality (CR 509.1b) and the combat-evasion pipeline.
/// - <b>Lifegain triggered ability (CR 603.6a / CR 119.3)</b>:
///   "Whenever you gain life, each opponent loses 1 life." Wired via
///   <see cref="Triggers.OnLifeGainedByPlayer"/> consuming
///   <see cref="LifeChangedEvent"/> filtered to the controller AND
///   strictly-positive deltas (NewLife &gt; PreviousLife — life *gain*,
///   not life loss). The "each opponent" clause reads from the optional
///   <c>opponentResolver</c> closure (Sheoldred-shape: factory doesn't
///   reach into a global player list at construction; the engine /
///   tests feed in <c>Game.Players</c> at wire-up). Each opponent
///   returned by the resolver (filtered to non-controller) loses 1
///   life via <see cref="Player.LoseLife"/>.
///
/// ## Lifecycle
/// - Single-arg <see cref="Create(Player)"/> attaches the trigger for
///   shape inspection but registers nothing and leaves the opponent
///   resolver null — the drain clause silently no-ops.
/// - Full overload accepts an <see cref="IEventBus"/> + <see cref="TriggerManager"/>
///   so bus-fired <see cref="LifeChangedEvent"/>s auto-queue the trigger,
///   plus an opponent-list closure resolved at trigger-resolution time
///   (mirrors <see cref="SheoldredTheApocalypseFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Live opponent enumeration without a resolver</b>: same gap as
///   <see cref="SheoldredTheApocalypseFactory"/> — <c>Player</c> doesn't
///   expose an opponent list, so the factory leans on a caller-supplied
///   resolver. Tests + the engine wire-up site feed it in.
/// </summary>
[CardName("Cliffhaven Vampire")]
public static class CliffhavenVampireFactory
{
    public const string CardName = "Cliffhaven Vampire";
    public const string PrintedManaCost = "{2}{W}{B}";
    public const int Power = 2;
    public const int Toughness = 4;
    public const int LifeLossPerOpponent = 1;

    /// <summary>
    /// Construct Cliffhaven Vampire with no live runtime services. The
    /// lifegain trigger is attached for shape inspection (not registered
    /// with a <see cref="TriggerManager"/>) and the drain clause is a
    /// no-op (no opponent resolver). Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Cliffhaven Vampire. When <paramref name="triggers"/> is
    /// supplied, the trigger is registered so a controller-scoped
    /// <see cref="LifeChangedEvent"/> places it on the stack automatically.
    /// "Each opponent" is read from the live resolution context at resolution
    /// (<see cref="ContextOpponents"/>), so the drain is correct on the
    /// production routed build.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Warrior, CardSubtype.Ally });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flying — CR 702.9. CombatAbilities.HasFlying consumes this
        // marker for the block-legality / evasion pipeline (CR 509.1b).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Lifegain trigger — CR 603.6a / 119.3.
        //   "Whenever you gain life, each opponent loses 1 life."
        // Triggers.OnLifeGainedByPlayer filters LifeChangedEvent to the
        // controller AND NewLife > PreviousLife (strictly-positive delta).
        // Resolution: each opponent returned by the resolver loses 1 life.
        // No targets — "each opponent" is global (CR 109.5). Same shape
        // as Sheoldred's draw drain.
        // ----------------------------------------------------------------
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses {LifeLossPerOpponent} life",
            ctx =>
            {
                // "Each opponent" is read from the LIVE resolution context —
                // NOT a captured resolver, which was null on the routed prod
                // build and made the drain INERT in real games (resolver-null
                // bug class; mirrors Stormbreath #2540 / Grist #2549).
                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    opp.LoseLife(LifeLossPerOpponent);
                }
                return ValueTask.CompletedTask;
            });

        var lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLifeGainedByPlayer(owner),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lifegainTrigger);
        triggers?.RegisterTriggeredAbility(lifegainTrigger);

        return card;
    }
}
