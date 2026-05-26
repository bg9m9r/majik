using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phelia, Exuberant Shepherd (Modern Horizons 3,
/// {1}{W}).
///
/// Legendary Creature — Dog Wizard 2/1. Oracle text:
///   "Lifelink.
///    Whenever Phelia, Exuberant Shepherd attacks, exile another target
///    nonland permanent. At the beginning of the next end step, return
///    that card to the battlefield under its owner's control."
///
/// ## Implemented (v1)
/// - 2/1 Legendary Creature — Dog Wizard, mana cost {1}{W}.
/// - <b>Lifelink</b> (CR 702.15) — wired as a <see cref="KeywordAbility"/>;
///   damage-time life gain is enforced by the combat damage step
///   (CR 510.1c).
/// - <b>Attack triggered ability</b> (CR 508.1f / CR 701.21) over
///   <see cref="CreatureAttacksEvent"/> matching <c>e.Attacker == card</c>
///   (same shape as <see cref="Triggers.OnAttackSelf"/> /
///   <see cref="EsikasChariotFactory"/>). The ability declares a single
///   1..1 <see cref="TargetRequest"/> for "another target nonland
///   permanent". The candidate gatherer enumerates every battlefield
///   permanent across all players (CR 109.5 / CR 305 — Lands are a card
///   type) that is NOT Phelia herself and is NOT a Land. On resolve:
///   <list type="bullet">
///     <item>CR 608.2b — resolution-time legality re-check: the target is
///       still a battlefield permanent, still not Phelia, still not a
///       Land.</item>
///     <item>CR 701.21 — exile via the target's owner-routed zone moves
///       (mirrors <see cref="SwordOfHearthAndHomeFactory.ExileThenReturn"/>
///       so the exiled card winds up in its OWNER's exile pile — relevant
///       when Phelia exiles an opponent's permanent).</item>
///     <item>CR 603.7 — register a one-shot
///       <see cref="DelayedTriggeredAbility"/> on the supplied
///       <see cref="TriggerManager"/>. The delayed trigger fires on the
///       first <see cref="StepStartedEvent"/> with
///       <c>StepType == End</c> AND <c>Timestamp &gt; resolvedAt</c>
///       (activation-time fence — same shape as
///       <see cref="TouchTheSpiritRealmFactory"/>'s Channel return and
///       <see cref="WrennsResolveFactory"/>'s upkeep grant). On resolve
///       the delayed effect moves the still-exiled card back to the
///       battlefield under its OWNER's control (CR 614 — "under its
///       owner's control" overrides any "you control" pronoun the
///       attack trigger might have implied).</item>
///   </list>
///
/// ## Bouncing into Phelia
/// Phelia is a Modern Horizons 3 "blink piece" — the canonical line is
/// attack with Phelia, exile your own ETB-heavy permanent (Solitude,
/// Ephemerate-target, etc.), and return it at end step for a free
/// re-trigger. v1 supports this because the candidate gatherer permits
/// controller-side permanents (the printed "another target" only excludes
/// Phelia herself, not all controller-side permanents). Heuristic bots
/// score with <see cref="BotIntent.Reanimate"/> + <see cref="BotIntent.Removal"/>:
/// the printed effect is both removal (against opponent perms) and a
/// re-trigger enabler (against own perms). v1 settles for Removal as the
/// primary intent — adequate for the bot's attack ranker.
///
/// ## Deferred (v1 gaps)
/// - <b>Tokens</b>: CR 111.8 — tokens that leave the battlefield cease to
///   exist. The delayed-return trigger defensively skips when
///   <c>target.Zone != ZoneType.Exile</c>, so a token exiled by Phelia
///   then SBA-removed (CR 704.5d) cleanly no-ops the return half. Same
///   posture as <see cref="TouchTheSpiritRealmFactory"/>.
/// - <b>Replacement effects on return</b>: the delayed return uses raw
///   zone moves (Exile.RemoveCard + Battlefield.AddCard), bypassing
///   <see cref="ZoneService"/>. ETB triggers on the returned card don't
///   fire on the shape path. The fully-wired path mirrors Sword of Hearth
///   and Home's <c>ExileThenReturn</c> using
///   <see cref="ZoneServiceRegistry"/> when registered.
/// - <b>"New object" rules</b>: CR 400.7 — the returned card is treated
///   as a new object that's been in play since the return resolves. v1
///   reuses the same <see cref="Card"/> instance; counter / aura
///   identity-sensitive interactions diverge from paper here (same
///   posture as every other exile-then-return effect in the codebase).
/// </summary>
[CardName("Phelia, Exuberant Shepherd")]
public static class PheliaExuberantShepherdFactory
{
    public const string CardName = "Phelia, Exuberant Shepherd";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Phelia with no live TriggerManager wiring. The attack
    /// trigger is attached for shape but is not registered with a bus;
    /// the delayed-return half is skipped silently when no
    /// <see cref="TriggerManager"/> is supplied (matches Touch the Spirit
    /// Realm / Wrenn's Resolve shape-only posture). Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Phelia with optional TriggerManager wiring. When
    /// <paramref name="triggers"/> is supplied the attack trigger is
    /// registered against the bus and the delayed end-step return is
    /// registered after each successful exile.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Dog, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.15 — Lifelink. Combat damage step gates the life gain.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // ----------------------------------------------------------------
        // Attack triggered ability — CR 508.1f / CR 701.21 / CR 603.7.
        //   "Whenever Phelia, Exuberant Shepherd attacks, exile another
        //    target nonland permanent. At the beginning of the next end
        //    step, return that card to the battlefield under its owner's
        //    control."
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;
        var attackCondition = Triggers.OnAttackSelf(card);

        var attackEffect = new Effect(
            $"{CardName}: exile another target nonland permanent; return at next end step",
            () =>
            {
                if (attackTrigger == null) return;
                var chosen = attackTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — resolution-time legality re-check.
                if (target.Zone != ZoneType.Battlefield) return;
                if (ReferenceEquals(target, card)) return;       // "another"
                if (target.HasType(CardType.Land)) return;        // "nonland"

                // CR 701.21 — exile via the target's OWNER's zone (the
                // owner might differ from the attack-trigger controller
                // when the exiled permanent belongs to an opponent).
                var targetOwner = target.Owner;
                var targetController = target.Controller ?? targetOwner;
                if (targetOwner == null) return;

                // Remove from whichever battlefield zone currently holds
                // it. Some test setups stash the permanent directly under
                // the owner's battlefield even when controlled by someone
                // else; controller-side removal is the canonical path.
                if (targetController != null
                    && targetController.Zones.Battlefield.GetCards().Contains(target))
                {
                    targetController.Zones.Battlefield.RemoveCard(target);
                }
                else
                {
                    targetOwner.Zones.Battlefield.RemoveCard(target);
                }
                targetOwner.Zones.Exile.AddCard(target);
                target.SetZone(ZoneType.Exile);

                // CR 603.7 — register a delayed end-step return rider.
                // Skipped when no TriggerManager is wired (same posture
                // as TouchTheSpiritRealmFactory / WrennsResolveFactory).
                if (triggers == null) return;

                var resolvedAt = DateTime.UtcNow;
                var returnEffect = new Effect(
                    $"{CardName}: return exiled card at next end step (CR 603.7)",
                    () =>
                    {
                        // CR 111.8 — tokens that left the battlefield
                        // cease to exist; defensively skip if SBA / a
                        // second exile move has pulled the card out.
                        if (target.Zone != ZoneType.Exile) return;

                        // CR 614 — "under its owner's control" — return
                        // routes through the OWNER's zones, not Phelia's
                        // controller (same posture as
                        // SwordOfHearthAndHomeFactory.ExileThenReturn).
                        var returnOwner = target.Owner;
                        if (returnOwner == null) return;

                        returnOwner.Zones.Exile.RemoveCard(target);
                        returnOwner.Zones.Battlefield.AddCard(target);
                        target.SetZone(ZoneType.Battlefield);
                        target.SetController(returnOwner);
                    });

                var delayed = new DelayedTriggeredAbility(
                    source: card,
                    controller: card.Controller ?? owner,
                    condition: new EventTriggerCondition<StepStartedEvent>(
                        (e, _) => e.StepType == PhaseStateType.End
                                  && e.Timestamp > resolvedAt),
                    effects: new IEffect[] { returnEffect });

                triggers.RegisterDelayed(delayed);
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: attackCondition,
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target nonland permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 109.5 / CR 305 — "nonland permanent" excludes
                    // Lands. "Another" excludes Phelia herself. Permanent
                    // = battlefield-side card with a permanent card type.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Where(p => !ReferenceEquals(p, card))
                        .Where(p => !p.HasType(CardType.Land))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
