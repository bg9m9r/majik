using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soaring Thought-Thief (Zendikar Rising,
/// {1}{U}). Creature — Faerie Rogue 1/2.
///
/// Oracle text:
///   "Flying
///    Whenever you attack with one or more Rogues, target opponent puts
///    the top two cards of their library into their graveyard.
///    Other Rogues you control have flying."
///
/// ## Implemented (v1)
/// - 1/2 Creature — Faerie Rogue, mana cost {1}{U}, owner/controller wired.
/// - <b>Flying</b> keyword marker (CR 702.9) via <see cref="KeywordAbility"/>.
/// - <b>Attack-with-Rogues trigger (CR 603.1 / CR 508.1f)</b> wired via
///   <see cref="EventTriggerCondition{TEvent}"/> against
///   <see cref="AttackersDeclaredEvent"/>. The trigger fires once per
///   declare-attackers step when the controller is the attacking player
///   AND at least one declared attacker is a Rogue (CR 700.2 — "one or
///   more" satisfies on a count of one or more). On resolution it picks
///   a target opponent (1..1 TargetRequest) and mills 2 (CR 701.13b)
///   via <see cref="MillAction.Apply"/>.
/// - <b>Lord rider</b> "Other Rogues you control have flying" wired
///   via <see cref="LordStaticEffect"/> with
///   <c>matchingSubtype: Rogue</c>, <c>power: 0, toughness: 0</c>,
///   <c>grantedKeywords: ["Flying"]</c>, <c>includeSelf: false</c>
///   (CR 613.1f — keyword-only grant in layer 6 / layer 7c via the
///   same effect shape as Goblin Chieftain's Haste rider).
///
/// ## Source closure injection
/// Same shape as <see cref="GoblinRabblemasterFactory"/> /
/// <see cref="AshiokDreamRenderFactory"/> — the engine doesn't yet expose
/// a global "currently declared attackers" view from inside a trigger
/// effect closure, so the factory accepts a
/// <c>Func&lt;Combat?&gt;</c> closure on the trigger predicate. The
/// trigger predicate reads the live <see cref="AttackersDeclaredEvent.Combat"/>
/// directly to count Rogue attackers, but a separate
/// <paramref name="allPlayersResolver"/> closure feeds the mill body
/// (target opponent resolution).
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger-on-stack timing</b>: in real MTG the trigger goes on the
///   stack and is targeted on resolution. v1 collapses this to
///   trigger-resolves-now shape; target opponent is the first opponent
///   in <paramref name="allPlayersResolver"/> when no explicit target
///   was supplied. Observationally equivalent for the mill payload.
/// - <b>LTB unregister for the lord static</b>: the registered
///   <see cref="LordStaticEffect"/> stays on the
///   <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Thought-Thief isn't on the battlefield so the granted Flying lifts
///   correctly. Same posture as Goblin Chieftain / Supreme Phantom.
/// </summary>
[CardName("Soaring Thought-Thief")]
public static class SoaringThoughtThiefFactory
{
    public const string CardName = "Soaring Thought-Thief";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>Number of cards milled by the attack trigger.</summary>
    public const int MillCount = 2;

    /// <summary>
    /// Construct Soaring Thought-Thief with no live runtime services.
    /// Suitable for card-shape / dispatcher tests — the lord static effect
    /// is NOT registered (no layers service) and the attack-trigger mill
    /// body is a no-op (no players resolver). The trigger ability shape
    /// is still attached to the card so <see cref="ICard.Abilities"/>
    /// includes it.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(
            owner,
            continuousEffects: null,
            triggers: null,
            allPlayersResolver: null);

    /// <summary>
    /// Construct a fully-wired Soaring Thought-Thief.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// "Other Rogues you control have flying" <see cref="LordStaticEffect"/>
    /// against. May be null — no live grant.</param>
    /// <param name="triggers">TriggerManager to register the attack-with-
    /// Rogues trigger against. May be null — the trigger is still attached
    /// to the card shape.</param>
    /// <param name="allPlayersResolver">Closure returning the full player
    /// list. Called at trigger resolution to pick the target opponent for
    /// the mill body. May be null — mill body is a no-op.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying keyword marker on the card itself.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 613.1f — "Other Rogues you control have flying." Keyword-only
        // grant in the LordStaticEffect shape — power/toughness = 0,
        // includeSelf: false. Same shape as Goblin Rabblemaster's "Other
        // Goblins you control have haste" rider.
        if (continuousEffects != null)
        {
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Rogue,
                power: 0,
                toughness: 0,
                grantedKeywords: new[] { "Flying" },
                includeSelf: false,
                opponentsOnly: false));
        }

        // CR 603.1 / CR 508.1f — "Whenever you attack with one or more
        // Rogues, target opponent puts the top two cards of their library
        // into their graveyard."
        //
        // Fires on AttackersDeclaredEvent (published once by CombatManager
        // when attackers are locked in) where the attacking player is this
        // card's controller AND at least one declared attacker is a Rogue
        // creature controlled by the controller. Mirrors the per-combat
        // semantics of Edric, Spymaster of Trest / Coastal Piracy.
        TriggeredAbility? attackTrigger = null;
        var attackEffect = new Effect(
            $"{CardName}: target opponent mills 2",
            () =>
            {
                if (allPlayersResolver == null) return;
                var players = allPlayersResolver();
                if (players == null) return;

                var controller = card.Controller ?? owner;
                Player? chosen = null;

                // CR 115 — honour an explicit target if the trigger was
                // dispatched with one. ChosenTargets[0][0] is the agent /
                // resolver-picked opponent.
                if (attackTrigger != null
                    && attackTrigger.ChosenTargets.Count > 0
                    && attackTrigger.ChosenTargets[0].Count > 0
                    && attackTrigger.ChosenTargets[0][0] is Player chosenPlayer
                    && !ReferenceEquals(chosenPlayer, controller))
                {
                    chosen = chosenPlayer;
                }

                // v1 fallback — first opponent in the player list (same
                // posture as Ashiok, Dream Render's -1 mill rider).
                if (chosen == null)
                {
                    foreach (var p in players)
                    {
                        if (ReferenceEquals(p, controller)) continue;
                        chosen = p;
                        break;
                    }
                }

                if (chosen == null) return;

                // CR 701.13b — mill N. MillAction.Apply gracefully handles
                // libraries with fewer than N cards (mills the remainder).
                MillAction.Apply(chosen, MillCount);
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
            {
                var controller = card.Controller ?? owner;
                if (!ReferenceEquals(e.Combat.AttackingPlayer, controller)) return false;
                // CR 700.2 — "one or more Rogues" satisfies on count ≥ 1.
                // The attacker creature already has the controller filter
                // baked in (declared attackers belong to AttackingPlayer).
                foreach (var atk in e.Combat.Attackers)
                {
                    if (atk?.Creature == null) continue;
                    if (atk.Creature.HasSubtype(CardSubtype.Rogue)) return true;
                }
                return false;
            }),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
