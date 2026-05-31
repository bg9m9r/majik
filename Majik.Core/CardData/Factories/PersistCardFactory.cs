using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Persist (Modern Horizons 3, {2}{B}).
///
/// Sorcery. Oracle text:
///   "Return target creature card with mana value 3 or less from your
///    graveyard to the battlefield. It gains haste. Exile it at the
///    beginning of the next end step."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}.
/// - Cast-time target: a single creature card in the caster's graveyard
///   whose printed mana value is ≤ 3 (CR 202.3b — mana value computed
///   from the printed cost, X = 0). Surfaced as a <see cref="TargetRequest"/>
///   in <see cref="BuildSpellDefinition"/>, populated with the legal
///   candidates at cast time. Same graveyard-card-as-target shape as
///   <see cref="AnimateDeadFactory"/>.
/// - Resolve effect mirrors <see cref="GoryosVengeanceFactory"/>:
///   1. Move the chosen creature card from graveyard to caster's
///      battlefield via <see cref="Fx.ReturnFromGraveyardToBattlefield"/>
///      (routes through <see cref="ZoneService.MoveCard"/> when supplied
///      so ETB triggers fire — CR 603.6a).
///   2. Grant Haste via <see cref="GrantKeywordUntilEndOfTurnEffect"/>
///      (CR 613.1c Layer 6 / CR 702.10). End-of-turn-expirable matches
///      the paired "exile at next end step" terminal clause; also clears
///      <see cref="Permanent.HasSummoningSickness"/> so the reanimated
///      creature can attack immediately (CR 702.10b).
///   3. Register a one-shot <see cref="DelayedTriggeredAbility"/>
///      (CR 603.7) that exiles the reanimated creature at the start of
///      the next end step. Activation-time fence (Timestamp &gt; resolvedAt)
///      mirrors Goryo's Vengeance / Through the Breach / Sneak Attack so
///      the end step in progress (if any) doesn't trip the trigger.
///      Zone-check at fire time so a bounced / destroyed / milled creature
///      isn't yanked from elsewhere (CR 603.10c).
///
/// ## Deferred (v1 gaps)
/// - <b>Target-on-resolution recheck</b>: CR 608.2b — "if all targets are
///   illegal on resolution, the spell does nothing". v1 reads the target
///   from the chosen targets and verifies the card still satisfies
///   "creature card in caster's graveyard with mv ≤ 3" at resolution; if
///   not, the spell fizzles cleanly (no haste, no delayed exile).
/// - <b>ActiveEffects on reanimated creature</b>: if the picked creature
///   has no <see cref="Creature.ActiveEffects"/> wired (shape mode),
///   the Haste grant is skipped silently — same posture as Goryo's
///   Vengeance.
/// </summary>
[CardName("Persist")]
public static class PersistCardFactory
{
    public const string CardName = "Persist";
    public const string PrintedManaCost = "{2}{B}";
    public const int ManaValueCeiling = 3;

    /// <summary>Granted keyword. CR 702.10 — Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>Printed oracle text — cross-checked at import time
    /// against Scryfall.</summary>
    public const string OracleText =
        "Return target creature card with mana value 3 or less from your " +
        "graveyard to the battlefield. It gains haste. Exile it at the " +
        "beginning of the next end step.";

    /// <summary>
    /// Build a Persist sorcery owned by <paramref name="owner"/>. Card
    /// shape only — see <see cref="BuildSpellDefinition"/> for the
    /// resolve-time reanimate + haste-grant + delayed exile.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Persist.
    /// Single target — a creature card in the caster's graveyard with
    /// printed mana value ≤ <see cref="ManaValueCeiling"/>. On resolution
    /// the target is reanimated under the caster's control, gains Haste,
    /// and a delayed end-step exile trigger is registered.
    /// </summary>
    /// <param name="caster">Spell controller — graveyard source +
    /// battlefield destination + delayed-trigger controller.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard
    /// → battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers fire (CR 603.6a).</param>
    /// <param name="triggers">Optional. When supplied the delayed
    /// end-step exile trigger is registered with the trigger manager.
    /// Shape-only callers can pass null — the reanimate + haste grant
    /// still happen but the creature won't be exiled automatically.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        // CR 202.3b — printed mana value from ManaCost.Parse. Filter the
        // caster's graveyard down to creature cards with mv ≤ 3 at cast
        // time so the agent prompt only sees legal candidates.
        var candidates = caster.Zones.Graveyard.GetCards()
            .OfType<Creature>()
            .Where(c => c.ManaCostValue.TotalValue <= ManaValueCeiling)
            .Cast<object>()
            .ToList();

        var request = new TargetRequest(
            Description: $"target creature card with mana value {ManaValueCeiling} or less from your graveyard",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: candidates);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { request },
            EffectFactory: chosen =>
            {
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    // CR 608.2b — no legal target on resolution → no-op.
                    return Array.Empty<IEffect>();
                }

                if (chosen.Targets[0][0] is not Creature picked)
                {
                    return Array.Empty<IEffect>();
                }

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: reanimate {picked.Name} with haste; exile at next end step",
                        () => ResolveBody(caster, picked, zoneService, triggers)),
                };
            });
    }

    private static void ResolveBody(
        Player caster,
        Creature picked,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        // CR 608.2b — illegal-on-resolution recheck. If the picked card is
        // no longer a creature card in the caster's graveyard with mv ≤ 3,
        // the spell does nothing (no reanimate, no haste, no delayed
        // trigger).
        if (picked.Zone != ZoneType.Graveyard) return;
        if (picked.Owner != caster && !caster.Zones.Graveyard.GetCards().Contains(picked)) return;
        if (!picked.HasType(CardType.Creature)) return;
        if (picked.ManaCostValue.TotalValue > ManaValueCeiling) return;

        // Graveyard → Battlefield under caster's control. Fx helper routes
        // through ZoneService when supplied so ETB triggers fire
        // (CR 603.6a); raw-zone fallback otherwise also sets controller.
        Fx.ReturnFromGraveyardToBattlefield(picked, caster, zoneService);

        // CR 702.10 / CR 613.1c Layer 6 — grant Haste. End-of-turn-
        // expirable matches the paired exile clause's terminal nature.
        if (picked.ActiveEffects != null)
        {
            picked.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(picked, GrantedKeyword));
        }
        picked.HasSummoningSickness = false;

        // CR 603.7 — delayed end-step exile trigger. Activation-time
        // fence (Timestamp > resolvedAt) so the end step in progress
        // doesn't trip it. Zone-check at fire time so a creature that
        // already left the battlefield doesn't get yanked from elsewhere.
        if (triggers == null) return;

        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var exileEffect = new Effect(
            $"{CardName}: exile {picked.Name} at next end step",
            () =>
            {
                if (picked.Zone != ZoneType.Battlefield) return;
                var bfPlayer = picked.Controller;
                if (bfPlayer == null) return;
                if (!bfPlayer.Zones.Battlefield.GetCards().Contains(picked)) return;

                var exileOwner = picked.Owner ?? caster;
                if (zoneService != null)
                {
                    zoneService.MoveCard(
                        picked, ZoneType.Battlefield, ZoneType.Exile, exileOwner);
                }
                else
                {
                    bfPlayer.Zones.Battlefield.RemoveCard(picked);
                    exileOwner.Zones.Exile.AddCard(picked);
                    picked.SetZone(ZoneType.Exile);
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
