using System.Linq;
using System.Threading.Tasks;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Raffine, Scheming Seer (Streets of New Capenna,
/// {W}{U}{B}). Legendary Creature — Sphinx Demon 1/4. Oracle text (verified
/// against Scryfall):
///   "Flying, ward {1}
///    Whenever you attack, target attacking creature connives X, where X is
///    the number of attacking creatures. (Draw X cards, then discard X cards.
///    Put a +1/+1 counter on that creature for each nonland card discarded
///    this way.)"
///
/// ## Implemented (v1)
/// - <b>1/4 Legendary Sphinx Demon at {W}{U}{B}</b>, owner/controller wired.
/// - <b>Flying (CR 702.9) + Ward {1} (CR 702.21)</b> — keyword markers via
///   <see cref="KeywordAbility"/> (same posture as Tolarian Terror's Ward /
///   Stormbreath Dragon's Flying — the battlefield-attached Ward trigger
///   surface is shared-deferred across the Ward family).
/// - <b>Attack trigger (CR 508.1 / 603.1)</b> — "Whenever you attack, target
///   attacking creature connives X, where X is the number of attacking
///   creatures." Fires on <see cref="AttackersDeclaredEvent"/> when Raffine's
///   controller is the attacking player. The connive amount X is read LIVE off
///   the resolving <see cref="Majik.Core.Game.GameContext.TurnState"/>
///   (<c>AttackersDeclaredThisCombat</c> — the CURRENT combat's attacker count
///   per CR 508.1, reset at each combat begin so extra-combat turns don't
///   over-count) — NOT a captured build-time TurnState, so it is correct on the
///   production routed build. The
///   target attacking creature comes from the trigger's
///   <see cref="TriggeredAbility.ChosenTargets"/> (the prod async trigger-drain
///   prompts the controller's agent), falling back to the first attacking
///   creature the controller controls at resolution.
///
/// ## Deferred (v1 gaps)
/// - <b>Ward {1} trigger enforcement</b> — the keyword marker is attached; the
///   battlefield-attached "whenever this becomes the target of a spell or
///   ability an opponent controls, counter it unless they pay {1}" trigger is
///   shared-deferred across the Ward family (Tolarian Terror / Kappa Cannoneer).
/// </summary>
[CardName("Raffine, Scheming Seer")]
public static class RaffineSchemingSeerFactory
{
    public const string CardName = "Raffine, Scheming Seer";
    public const string PrintedManaCost = "{W}{U}{B}";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>CR 702.21 — printed Ward cost: {1}.</summary>
    public const string WardCost = "{1}";

    /// <summary>
    /// Construct Raffine. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to on the production routed build. The attack trigger reads
    /// the live attacker count off the resolution context's TurnState — no
    /// captured resolver, so it is live on prod.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Raffine, optionally registering the attack trigger with a
    /// <paramref name="triggers"/> manager so it surfaces as pending.
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
            subtypes: new[] { CardSubtype.Sphinx, CardSubtype.Demon });

        card.SetOwner(owner);
        card.SetController(owner);

        // Flying (CR 702.9) + Ward {1} (CR 702.21) keyword markers.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        BuildAttackTrigger(card, owner, triggers);

        return card;
    }

    /// <summary>
    /// CR 702.21 — Raffine's printed Ward {1} effect, bound to
    /// <paramref name="card"/>. Builder mirrors Tolarian Terror's
    /// <c>BuildWardEffect</c> (the attached trigger surface is shared-deferred).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, ManaCost.Parse(WardCost));

    // --- Attack trigger (CR 508.1 / 603.1) ---------------------------------

    private static void BuildAttackTrigger(Creature card, Player owner, TriggerManager? triggers)
    {
        // Capture the triggering combat so the resolve body can default the
        // target to a declared attacker (CR 603.2 — the ability is associated
        // with the event that triggered it) when no target was chosen.
        Majik.Core.Combat.Combat? capturedCombat = null;
        TriggeredAbility? trigger = null;

        var conniveEffect = new Effect(
            $"{CardName}: target attacking creature connives X (X = number of attacking creatures)",
            rc =>
            {
                var combat = capturedCombat;
                capturedCombat = null;

                var controller = card.Controller ?? owner;
                var target = ResolveConniveTarget(trigger, combat, controller);
                if (target == null) return ValueTask.CompletedTask;

                // CR 608.2b — resolution-time legality re-check.
                if (target.Zone != ZoneType.Battlefield) return ValueTask.CompletedTask;

                // X = number of attacking creatures in the CURRENT combat (CR
                // 508.1), read LIVE off the resolution context's TurnState's
                // per-combat tally (reset at each combat begin) — never the
                // turn-cumulative sum, so an extra-combat turn (Aggravated
                // Assault etc.) doesn't over-count X, and never a captured
                // build-time count. 0 ⇒ Fx.Connive no-ops.
                var x = rc.Game?.TurnState?.AttackersDeclaredThisCombat ?? 0;
                Fx.Connive(target, x);
                return ValueTask.CompletedTask;
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
            {
                // "Whenever you attack" — Raffine's controller is the attacking
                // player (CR 508.1 / 109.5).
                if (!ReferenceEquals(e.Combat.AttackingPlayer, card.Controller ?? owner))
                    return false;
                capturedCombat = e.Combat;
                return true;
            }),
            effects: new IEffect[] { conniveEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target attacking creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    // The attacking creatures (CR 508.1) — read off the live
                    // combat captured by the condition.
                    CandidateGatherer: _ => (capturedCombat?.Attackers ?? Array.Empty<Majik.Core.Combat.Attacker>())
                        .Select(a => a?.Creature)
                        .Where(c => c != null)
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    private static Creature? ResolveConniveTarget(
        TriggeredAbility? trigger,
        Majik.Core.Combat.Combat? combat,
        Player controller)
    {
        // CR 115 — honour the chosen target (the prod async trigger-drain
        // prompts the agent).
        if (trigger != null
            && trigger.ChosenTargets.Count > 0
            && trigger.ChosenTargets[0].Count > 0
            && trigger.ChosenTargets[0][0] is Creature chosen)
        {
            return chosen;
        }

        // Fallback — first declared attacker the controller controls.
        if (combat == null) return null;
        foreach (var atk in combat.Attackers)
        {
            // CR 508 — Attacker.Creature is now Permanent-typed; connive targets
            // a real attacking CREATURE card, so skip an animated land attacker.
            if (atk?.Creature is not Creature creature) continue;
            if (ReferenceEquals(creature.Controller, controller)) return creature;
        }
        return null;
    }
}
