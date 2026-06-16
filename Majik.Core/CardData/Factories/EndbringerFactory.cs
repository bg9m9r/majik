using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Endbringer (Oath of the Gatewatch, {5}{C}).
///
/// Creature — Eldrazi 5/5. Oracle text (Scryfall, verified 2025):
///   "Untap this creature during each other player's untap step.
///    {T}: This creature deals 1 damage to any target.
///    {C}, {T}: Target creature can't attack or block this turn.
///    {C}{C}, {T}: Draw a card."
///
/// (Earlier shipped a STALE oracle: "Vigilance, reach / {C},{T}: Target
/// player draws / {C},{T}: Tap target creature." Rewritten to the current
/// printed text — see the
/// `endbringer-stale-body-rewrite-then-resolutioncontext-migrate` deferral.)
///
/// ## Implemented
/// - 5/5 Creature — Eldrazi at {5}{C}.
/// - <b>"Untap this creature during each other player's untap step." (CR
///   502.1 + the printed static)</b>: lifecycle binder
///   <see cref="UntapsDuringOtherUntapStepsStaticEffect"/> registers an
///   extra-untap rider while Endbringer is on the battlefield;
///   <see cref="Majik.Core.Game.TurnDriver"/>'s untap step untaps it during
///   each non-controller's untap step. Wired only when an event bus is
///   supplied (shape-only constructors stay side-effect-free).
/// - <b>{T}: This creature deals 1 damage to any target (CR 602)</b>:
///   <see cref="ActivatedAbility"/> with sole cost
///   <see cref="AdditionalCost.Tap"/>(self) + 1..1 "any target"
///   <see cref="TargetRequest"/>. Resolution reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> (Player / Creature / Planeswalker
///   funnel per CR 119.3 / CR 306.7), same posture as Pyrite Spellbomb /
///   Walking Ballista.
/// - <b>{C}, {T}: Target creature can't attack or block this turn (CR 602 +
///   CR 508.1c / 509.1c)</b>: <see cref="ActivatedAbility"/> with cost
///   stack <c>[ManaCostCost("{C}"), AdditionalCost.Tap(self)]</c> + 1..1
///   "target creature" <see cref="TargetRequest"/>. Resolution rechecks the
///   target is still a creature on the battlefield (CR 608.2b) and registers
///   BOTH a <see cref="CombatRestriction.CannotAttack"/> and a
///   <see cref="CombatRestriction.CannotBlock"/>
///   <see cref="CombatRestrictionEffect"/> on the target's
///   <see cref="Permanent.ActiveEffects"/> (default EOT expiry — the printed
///   "this turn", CR 514.2). Same posture as Earthshaker Khenra's
///   "can't block this turn".
/// - <b>{C}{C}, {T}: Draw a card (CR 602)</b>:
///   <see cref="ActivatedAbility"/> with cost stack
///   <c>[ManaCostCost("{C}{C}"), AdditionalCost.Tap(self)]</c>, no target.
///   Resolution draws one card for the ability's controller
///   (<see cref="ResolutionContext.Controller"/>) through
///   <see cref="Fx.DrawCards"/> so future
///   <see cref="Majik.Core.Events.DrawCardIntent"/> replacements (Dredge,
///   etc.) participate.
///
/// ## Re-source migration (Agatha's Soul Cauldron)
/// All three activated abilities are marked <c>rebindSafe: true</c> and read
/// their chosen target / drawing player off the live
/// <see cref="ResolutionContext"/> (ChosenTargets / Controller) rather than
/// capturing the authoring handle, so Agatha's Soul Cauldron's group-grant
/// re-homes the REAL abilities onto a counter-bearing bearer via
/// <see cref="ActivatedAbility.RebindTo"/> (CR 707.2 / 613.1f) — the {T} taps
/// the BEARER, never the exiled Endbringer.
///
/// ## Deferred (v1 gaps)
/// - <b>Colorless mana spend restriction enforcement</b>: the {C} cost
///   pips are parsed by <see cref="ManaCost.Parse"/> and stored as a
///   colorless requirement; <see cref="ManaPaymentResolver"/> currently
///   accepts generic mana for colorless slots (same posture as Eldrazi
///   Temple's colorless-only rider — the engine-wide colorless-only
///   payment gate is pending the per-slot provenance work flagged in
///   `EldraziTempleFactory`).
/// </summary>
[CardName("Endbringer")]
public static class EndbringerFactory
{
    public const string CardName = "Endbringer";
    public const string PrintedManaCost = "{5}{C}";
    public const string ColorlessActivationCost = "{C}";
    public const string DoubleColorlessActivationCost = "{C}{C}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Endbringer with no event-bus wiring (shape-only / unit-test
    /// path). The three activated abilities are attached; the "untap during
    /// each other player's untap step" static does NOT register (it needs an
    /// event bus to track the battlefield lifecycle). Use the
    /// <see cref="Create(Player, IEventBus)"/> overload for the full surface.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Endbringer owned and controlled by <paramref name="owner"/>.
    /// All three activated abilities are attached. When
    /// <paramref name="eventBus"/> is supplied the
    /// <see cref="UntapsDuringOtherUntapStepsStaticEffect"/> lifecycle binder
    /// is attached so the printed "untap this creature during each other
    /// player's untap step" static activates on ETB and lifts on LTB.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Endbringer deals 1 damage to any target.
        // CR 602 — activated ability. Sole cost is the self-tap; no mana
        // pip. 1..1 "any target" TargetRequest. Resolution funnels through
        // Fx.DealDamageAny (Player / Creature / Planeswalker — CR 119.3,
        // CR 306.7 loyalty conversion).
        //
        // RE-SOURCE-SAFE (agatha-bespoke-factory-resolutioncontext-source-
        // migration): the effect reads its chosen target off the live
        // ResolutionContext.ChosenTargets and attributes the damage to the
        // ability's own ResolutionContext.Source (the bearer at resolution)
        // rather than capturing the authoring ability handle / `card`,
        // falling back to `card` only on the context-less legacy sync path
        // (ResolutionContext.Legacy). Marked RebindSafe so Agatha's Soul
        // Cauldron's group-grant re-homes the REAL ping onto a counter-
        // bearing bearer via ActivatedAbility.RebindTo (CR 707.2 / 613.1f):
        // the {T} taps the BEARER (Stage-1 cost re-home) and the damage is
        // sourced from the BEARER, never the exiled Endbringer.
        // ----------------------------------------------------------------
        var damageEffect = new Effect(
            $"{CardName}: 1 damage to any target",
            ctx =>
            {
                if (ctx.ChosenTargets.Count == 0
                    || ctx.ChosenTargets[0].Count == 0)
                {
                    return ValueTask.CompletedTask;
                }

                var target = ctx.ChosenTargets[0][0];
                Fx.DealDamageAny(target, 1, (ctx.Source as Creature) ?? card);
                return ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            },
            rebindSafe: true));

        // ----------------------------------------------------------------
        // {C}, {T}: Target creature can't attack or block this turn.
        // CR 602 + CR 508.1c (CannotAttack) + CR 509.1c (CannotBlock). Cost
        // stack: ManaCostCost("{C}") + AdditionalCost.Tap(self). 1..1
        // "target creature" TargetRequest. Resolution rechecks the chosen
        // target is still a creature on the battlefield (CR 608.2b —
        // illegal-on-resolution fails silently) and registers BOTH a
        // CannotAttack and a CannotBlock CombatRestrictionEffect on the
        // target's ContinuousEffectsService (default EOT expiry — the
        // printed "this turn", CR 514.2). The combat validator queries those
        // restrictions directly. When ActiveEffects is null (shape tests),
        // the grant silently no-ops. Same posture as Earthshaker Khenra.
        //
        // RE-SOURCE-SAFE (agatha-bespoke-factory-resolutioncontext-source-
        // migration): the effect reads its chosen target off the live
        // ResolutionContext.ChosenTargets rather than capturing the
        // authoring ability handle. Marked RebindSafe so Agatha's Soul
        // Cauldron's group-grant re-homes the REAL restriction grant onto a
        // counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2 /
        // 613.1f); the {T} taps the BEARER (Stage-1 cost re-home), never the
        // exiled Endbringer.
        // ----------------------------------------------------------------
        var cantAttackOrBlockEffect = new Effect(
            $"{CardName}: target creature can't attack or block this turn",
            ctx =>
            {
                if (ctx.ChosenTargets.Count == 0
                    || ctx.ChosenTargets[0].Count == 0
                    || ctx.ChosenTargets[0][0] is not Creature target)
                {
                    return ValueTask.CompletedTask;
                }

                // CR 608.2b — recheck legality at resolution.
                if (!target.HasType(CardType.Creature)) return ValueTask.CompletedTask;
                if (target.Zone != ZoneType.Battlefield) return ValueTask.CompletedTask;
                if (target.ActiveEffects == null) return ValueTask.CompletedTask;

                target.ActiveEffects.Register(
                    new CombatRestrictionEffect(CombatRestriction.CannotAttack, target));
                target.ActiveEffects.Register(
                    new CombatRestrictionEffect(CombatRestriction.CannotBlock, target));
                return ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ColorlessActivationCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { cantAttackOrBlockEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            rebindSafe: true));

        // ----------------------------------------------------------------
        // {C}{C}, {T}: Draw a card.
        // CR 602 — activated ability. Cost stack: ManaCostCost("{C}{C}") +
        // AdditionalCost.Tap(self). No target. Resolution draws one card for
        // the ability's controller (ResolutionContext.Controller) through
        // Fx.DrawCards so DrawCardIntent replacement subscribers (Dredge /
        // future replacement primitives) participate.
        //
        // RE-SOURCE-SAFE (agatha-bespoke-factory-resolutioncontext-source-
        // migration): the drawing player is read off ResolutionContext.
        // Controller (the activator) rather than capturing `owner`. Marked
        // RebindSafe so Agatha's Soul Cauldron's group-grant re-homes the
        // REAL draw onto a counter-bearing bearer via ActivatedAbility.
        // RebindTo (CR 707.2 / 613.1f); the {T} taps the BEARER (Stage-1
        // cost re-home), never the exiled Endbringer. The card is drawn by
        // the bearer's controller, never the exiled Endbringer's owner.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            ctx =>
            {
                var drawer = ctx.Controller ?? owner;
                Fx.DrawCards(drawer, 1);
                return ValueTask.CompletedTask;
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(DoubleColorlessActivationCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { drawEffect },
            rebindSafe: true));

        // ----------------------------------------------------------------
        // "Untap this creature during each other player's untap step."
        // CR 502.1 + the printed static. Wired via the lifecycle binder;
        // only attaches when an event bus is supplied so the shape-only
        // constructors stay zero-side-effect for structural tests that
        // don't drive zone moves (same posture as Mana Vault's untap
        // static). On ETB the extra-untap rider registers; on LTB it lifts.
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            new UntapsDuringOtherUntapStepsStaticEffect(card, eventBus).Attach();
        }

        return card;
    }
}
