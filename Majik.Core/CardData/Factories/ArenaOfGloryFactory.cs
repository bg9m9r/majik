using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arena of Glory (Modern Horizons 3, Land).
///
/// Oracle text:
///   "This land enters tapped unless you control a Mountain."
///   "{T}: Add {R}."
///   "{R}, {T}, Exert this land: Add {R}{R}. If that mana is spent on a
///    creature spell, it gains haste until end of turn. (An exerted
///    permanent won't untap during your next untap step.)"
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain <see cref="Land"/>, no supertype, no
///   printed subtype (Arena is a nonbasic, non-typed land).
/// - <b>ETB tapped unless you control a Mountain (CR 614.1c)</b> —
///   registered as a <see cref="ConditionalEntersTappedReplacement"/> on the
///   supplied <see cref="ReplacementBus"/>. Predicate: enters untapped iff
///   the controller controls another land (excluding this one) with the
///   <see cref="CardSubtype.Mountain"/> subtype. Same single-subtype shape
///   as <see cref="CheckLandCycleFactory"/>'s two-subtype predicate, narrowed
///   to one basic type. Only the controller's own Mountains count.
/// - <b>{T}: Add {R}</b> — vanilla red <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{R}, {T}, Exert this land: Add {R}{R}</b> — modelled as a single
///   <see cref="ManaAbility"/> producing {R}{R} (CR 605.1a — adding mana
///   doesn't use the stack) with the cost-plus-payer overload:
///   <list type="bullet">
///     <item><c>canActivateCheck</c>: the land is untapped AND the
///       controller's pool already holds at least one {R} for the printed
///       {R} portion of the cost (CR 602.5 / CR 119.4 — can't pay a cost
///       you can't afford).</item>
///     <item><c>additionalCostPayer</c> (runs after the {T} tap): pays the
///       {R} from the pool, marks the land "doesn't untap during your next
///       untap step" via <see cref="UntapStepRestrictions.MarkPermanentDoesNotUntap"/>
///       — the Exert clause, CR 502.1 / CR 702.10 — and records the
///       haste-granting provenance on the controller (see below).</item>
///   </list>
/// - <b>Exert "next untap step" cleanup</b> — same one-shot pattern as
///   <see cref="FrostLynxFactory"/>: when an <see cref="IEventBus"/> is
///   supplied, a one-shot <see cref="StepStartedEvent"/> subscription lifts
///   the untap-skip on the controller's next Untap step. Without a bus the
///   skip persists in the registry until <see cref="UntapStepRestrictions.Clear"/>
///   is called (shared test-isolation posture with Mana Vault / Frost Lynx).
/// - <b>Mana provenance — "if that mana is spent on a creature spell, it
///   gains haste until end of turn" (CR 702.10)</b> — slot-level provenance
///   (CR 106.4). The exert ability sets a
///   <see cref="ManaAbility.ProvenanceReaction"/>; the
///   <see cref="Majik.Core.Services.ManaAbilityActivator"/> tags each {R} it
///   produces with a <see cref="Majik.Core.Mana.ManaProvenanceSlot"/> whose
///   source is the exert ability and whose OnSpent is that reaction. When the
///   <see cref="Majik.Core.Costs.ManaPaymentResolver"/> consumes one of those
///   tagged units paying a cost, it fires the reaction with the cast card; a
///   creature spell gets a Layer-6
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Haste") expiring in the
///   cleanup step. This is strictly per-pip: the haste attaches to the
///   creature the exert mana actually paid for, not "the first spell cast
///   after the exert" (the prior coarse player-scoped counter).
///
/// ## Deferred (v1 gaps)
/// - Single-arg dispatcher path constructs without a
///   <see cref="ReplacementBus"/> — the ETB-tapped replacement is omitted
///   (shape-only posture matching <see cref="CheckLandCycleFactory"/> and
///   every other ETB-replacement factory's single-arg path). Lands enter
///   untapped on this code path; the full overload wires the predicate.
/// </summary>
[CardName("Arena of Glory")]
public static class ArenaOfGloryFactory
{
    public const string CardName = "Arena of Glory";

    /// <summary>
    /// Construct Arena of Glory with no <see cref="ReplacementBus"/> /
    /// <see cref="IEventBus"/> wiring. The ETB-tapped replacement is omitted
    /// (shape-only posture); the exert untap-skip persists until the caller
    /// clears <see cref="UntapStepRestrictions"/>. Suitable for the
    /// <see cref="NamedCardFactory"/> dispatcher and shape tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Arena of Glory with an optional <see cref="ReplacementBus"/>
    /// for full ETB-tapped wiring (no event bus). Convenience overload for
    /// the ETB-tapped tests.
    /// </summary>
    public static Land Create(Player owner, ReplacementBus? replacements) =>
        Create(owner, eventBus: null, replacements: replacements);

    /// <summary>
    /// Construct Arena of Glory with optional <see cref="IEventBus"/> +
    /// <see cref="ReplacementBus"/> wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the exert ability's one-shot
    /// "doesn't untap next untap step" cleanup is driven by the controller's
    /// next Untap <see cref="StepStartedEvent"/> (CR 502.1).</param>
    /// <param name="replacements">When supplied, the
    /// "enters tapped unless you control a Mountain" replacement is
    /// registered (CR 614.1c).</param>
    public static Land Create(Player owner, IEventBus? eventBus, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(CardName);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // CR 614.1c — "This land enters tapped unless you control a
        // Mountain." Predicate returns true ⇒ enters untapped. The card
        // itself is excluded from the count via reference equality; only the
        // controller's own Mountains count (same shape as
        // CheckLandCycleFactory, narrowed to a single basic subtype).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    ControllerHasSubtype(controller, self, CardSubtype.Mountain)));
        }

        // ----------------------------------------------------------------
        // {T}: Add {R}  (CR 605.1 — mana ability, no stack).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        // ----------------------------------------------------------------
        // {R}, {T}, Exert this land: Add {R}{R}.
        //   - canActivateCheck: untapped land AND ≥1 {R} in the pool for the
        //     printed {R} portion of the cost.
        //   - additionalCostPayer (after the {T} tap): pay {R} and mark the
        //     land "doesn't untap during your next untap step" (Exert, CR
        //     502.1).
        //   - ProvenanceReaction (CR 702.10 / 106.4): the {R}{R} this ability
        //     produces is slot-tagged with this ability as its source; when
        //     one of those units is spent paying for a creature spell, that
        //     creature gains haste until end of turn. Strictly per-pip — the
        //     reaction fires only for the spell the exert mana actually paid,
        //     not "the first spell after the exert" (the old coarse counter).
        // ----------------------------------------------------------------
        var exert = new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("RR"),
            canActivateCheck: () =>
            {
                if (land.IsTapped) return false;
                var controller = land.Controller ?? owner;
                return controller.ManaPool.Red >= 1;
            },
            additionalCostPayer: controller =>
            {
                // CR 601.2g — pay the printed {R} portion of the cost.
                controller.PayMana(ManaCost.Parse("R"));

                // CR 502.1 / CR 702.10 — Exert: "this land won't untap during
                // your next untap step." One-shot per-permanent skip, keyed by
                // a fresh token so repeat exerts stack cleanly; lifts on the
                // controller's next Untap step when a bus is available
                // (mirrors FrostLynxFactory's "next untap step" cleanup).
                var skipToken = new object();
                UntapStepRestrictions.MarkPermanentDoesNotUntap(skipToken, land);

                if (eventBus != null)
                {
                    Action<StepStartedEvent>? cleanup = null;
                    cleanup = ev =>
                    {
                        var sse = ev;
                        if (sse.StepType != PhaseStateType.Untap) return;
                        if (!ReferenceEquals(sse.Player, controller)) return;

                        UntapStepRestrictions.RemoveAll(skipToken);
                        if (cleanup != null) eventBus.Unsubscribe(cleanup);
                    };
                    eventBus.Subscribe(cleanup);
                }
            });

        // CR 702.10 — "If that mana is spent on a creature spell, it gains
        // haste until end of turn." Fired by the payment resolver for each
        // exert-tagged {R} that pays a cost, carrying the cast card.
        exert.ProvenanceReaction = spentOn => GrantHasteIfCreature(spentOn);

        land.AddAbility(exert);

        return land;
    }

    private static bool ControllerHasSubtype(
        Player controller,
        ICard self,
        CardSubtype subtype) =>
        controller.Zones.Battlefield.GetCards()
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(subtype));

    /// <summary>
    /// CR 702.10 — grant the creature the exert mana paid for haste until end
    /// of turn. No-op when the mana was spent on a noncreature spell or a
    /// non-spell context (<paramref name="spentOn"/> is null or not a
    /// <see cref="Creature"/>). Registers a Layer-6
    /// <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Haste") on the
    /// creature's active effects, expiring in the cleanup step. Idempotent if
    /// multiple exert pips pay for the same creature spell — re-granting the
    /// same keyword is harmless.
    /// </summary>
    private static void GrantHasteIfCreature(ICard? spentOn)
    {
        if (spentOn is not Creature creature) return;
        creature.ActiveEffects ??= new ContinuousEffectsService();
        creature.ActiveEffects.Register(
            new GrantKeywordUntilEndOfTurnEffect(creature, "Haste"));
    }
}
