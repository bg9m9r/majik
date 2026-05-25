using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goryo's Vengeance (Champions of Kamigawa, {1}{B}).
///
/// Instant — Arcane. Oracle text:
///   "Return target legendary creature card from your graveyard to the
///    battlefield. That creature gains haste. Exile it at the beginning
///    of the next end step.
///    Splice onto Arcane {2}{B}."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}. Printed Arcane subtype is omitted
///   from the runtime card (CR 205.3 — Arcane is a spell subtype; the
///   engine's <see cref="CardSubtype"/> enum doesn't yet carry an
///   Arcane member, same gap as Through the Breach. The Splice-onto-
///   Arcane primitive is the work item that would land both).
/// - Card shape only on <see cref="Create(Player)"/>. The resolve effect
///   is built on demand via <see cref="BuildResolveEffect"/> so tests /
///   integrations can splice it into a <see cref="Majik.Core.Game.SpellDefinition"/>
///   or pass it directly to a <see cref="Majik.Core.Spells.Spell"/>.
/// - Resolve effect:
///   1. Picks a legendary creature card from the caster's graveyard (v1
///      deterministic first-match pick — same shape as Reanimate /
///      Priest of Fell Rites). If no legendary creature is in the
///      graveyard the effect is a clean no-op (CR 117.x — "target"
///      effect with no legal target).
///   2. Moves the picked card from graveyard to the caster's
///      battlefield via <see cref="Fx.ReturnFromGraveyardToBattlefield"/>.
///      Routes through <see cref="ZoneService.MoveCard"/> when supplied
///      so ETB triggers on the reanimated creature fire (CR 603.6a).
///   3. Grants Haste via <see cref="GrantKeywordUntilEndOfTurnEffect"/>
///      on the reanimated creature's <see cref="Creature.ActiveEffects"/>
///      when one is attached (CR 613.1c Layer 6 / CR 702.10). Printed
///      oracle reads "gains haste" without an explicit duration; the
///      paired "exile at the next end step" clause makes the haste
///      grant terminal-anyway, so an end-of-turn-expirable continuous
///      effect matches observable behaviour. Also clears
///      <see cref="Permanent.HasSummoningSickness"/> so the reanimated
///      creature is attack-ready immediately (CR 702.10b).
///   4. Registers a one-shot <see cref="DelayedTriggeredAbility"/>
///      (CR 603.7) on the supplied <see cref="TriggerManager"/> that
///      exiles the reanimated creature at the start of the next end
///      step (CR 500.4 / CR 701.21 — controller's battlefield → owner's
///      exile). Activation-time fence (<c>e.Timestamp &gt; resolvedAt</c>)
///      mirrors Through the Breach / Sneak Attack / Splinter Twin so
///      the end step in progress (if any) doesn't trip it.
///
/// ## Deferred (v1 gaps)
/// - <b>Splice onto Arcane (CR 702.46)</b>: the splice alt-cost
///   primitive isn't in the engine yet — same gap as Through the
///   Breach. Goryo's Vengeance is still castable for its printed cost;
///   the splice rider {2}{B} is structural-only on the oracle text
///   and will be added when Arcane-spell awareness lands. Documented
///   here as <see cref="SpliceOntoArcaneManaCost"/>.
/// - <b>"Target" prompt</b>: defaults to the first legendary creature
///   card in the caster's graveyard. Real agent-driven choose-from-
///   graveyard awaits the prompt MVP (same posture as Reanimate).
/// - <b>Empty-graveyard / no-legendary-creature</b>: clean no-op. The
///   spell still resolves; no creature is reanimated and no delayed
///   trigger is registered (there is no creature to exile).
/// - <b>ActiveEffects on reanimated creature</b>: if the picked
///   creature has no <see cref="Creature.ActiveEffects"/> wired
///   (shape mode), the Haste grant is skipped silently. Production
///   callers wire a <see cref="ContinuousEffectsService"/> on
///   creatures before they hit the battlefield (same shape as
///   Through the Breach).
/// </summary>
[CardName("Goryo's Vengeance")]
public static class GoryosVengeanceFactory
{
    public const string CardName = "Goryo's Vengeance";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>Documented splice rider — not enforced (no Splice primitive).
    /// CR 702.46.</summary>
    public const string SpliceOntoArcaneManaCost = "{2}{B}";

    /// <summary>Granted keyword. CR 702.10 — Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>
    /// Build a Goryo's Vengeance instant owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildResolveEffect"/> for the
    /// resolve-time reanimate + haste-grant + delayed exile.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);

        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Goryo's Vengeance's resolve effect. On resolution
    /// deterministically picks the first legendary creature card in
    /// <paramref name="caster"/>'s graveyard and: moves it to the
    /// battlefield (routing through <paramref name="zoneService"/> when
    /// supplied), grants Haste, and (when <paramref name="triggers"/>
    /// is supplied) registers a delayed end-step exile trigger.
    /// </summary>
    /// <param name="caster">Spell controller — graveyard source +
    /// battlefield destination + delayed trigger controller.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard
    /// → battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers on the reanimated creature fire (CR 603.6a).</param>
    /// <param name="triggers">Optional. When supplied the delayed
    /// end-step exile trigger is registered with the trigger manager.
    /// Shape-only callers can pass null — the reanimate + haste grant
    /// still happen but the creature won't be exiled automatically.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: reanimate legendary creature from graveyard, haste, exile next end step.",
                () => ResolveBody(caster, zoneService, triggers)),
        };
    }

    private static void ResolveBody(
        Player caster,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        // -------------------------------------------------------------------
        // "Return target legendary creature card from your graveyard…"
        // v1 deterministic: first legendary creature card in caster's
        // graveyard. No legal target → no-op (CR 117.x).
        // -------------------------------------------------------------------
        var pick = caster.Zones.Graveyard.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.HasSupertype(CardSupertype.Legendary));
        if (pick == null) return;

        // -------------------------------------------------------------------
        // Graveyard → Battlefield. Fx.ReturnFromGraveyardToBattlefield
        // routes through ZoneService when supplied (ETB triggers publish
        // CardMovedEvent — CR 603.6a). Raw-zone fallback otherwise sets
        // controller too.
        // -------------------------------------------------------------------
        Fx.ReturnFromGraveyardToBattlefield(pick, caster, zoneService);

        // -------------------------------------------------------------------
        // "That creature gains haste." (CR 702.10) — Layer 6 keyword
        // grant. Printed oracle has no explicit duration; the paired
        // "exile at the next end step" clause makes the grant terminal,
        // so an end-of-turn-expirable layer effect matches observable
        // behaviour. No-op silently when no ActiveEffects service is
        // wired (shape mode).
        // -------------------------------------------------------------------
        if (pick.ActiveEffects != null)
        {
            pick.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(pick, GrantedKeyword));
        }
        pick.HasSummoningSickness = false;

        // -------------------------------------------------------------------
        // "Exile it at the beginning of the next end step."
        // CR 603.7 — one-shot delayed triggered ability. Fires on the
        // first StepStartedEvent(End) strictly after this resolve
        // (activation-time fence mirrors Through the Breach / Sneak
        // Attack / Splinter Twin). Resolution moves the creature from
        // controller's battlefield to owner's exile (CR 701.21).
        // Zone-check at fire time so a creature that's already left the
        // battlefield (bounce, destroy, mill) doesn't get yanked from
        // elsewhere.
        // -------------------------------------------------------------------
        if (triggers == null) return;

        var resolvedAt = DateTime.UtcNow;
        var exileEffect = new Effect(
            $"{CardName}: exile {pick.Name} at next end step",
            () =>
            {
                if (pick.Zone != ZoneType.Battlefield) return;
                var battlefield = pick.Controller?.Zones.Battlefield;
                if (battlefield == null) return;
                if (!battlefield.GetCards().Contains(pick)) return;

                // CR 701.21 — exile: controller's battlefield → owner's
                // exile. ZoneService routes the publish when supplied.
                var bfPlayer = pick.Controller!;
                var exileOwner = pick.Owner ?? caster;
                if (zoneService != null)
                {
                    zoneService.MoveCard(
                        pick, ZoneType.Battlefield, ZoneType.Exile, exileOwner);
                }
                else
                {
                    bfPlayer.Zones.Battlefield.RemoveCard(pick);
                    exileOwner.Zones.Exile.AddCard(pick);
                    pick.SetZone(ZoneType.Exile);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: caster,
            controller: caster,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { exileEffect });

        triggers.RegisterDelayed(delayed);
    }
}
