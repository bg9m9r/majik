using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phantasmal Image (Magic 2012 / Modern Horizons 2,
/// {1}{U}).
///
/// ## Card text
/// "You may have Phantasmal Image enter as a copy of any creature on the
///  battlefield, except it's an Illusion in addition to its other types
///  and has 'When this creature becomes the target of a spell or ability,
///  sacrifice it.'"
///
/// ## Implemented (v1)
/// - 0/0 Illusion creature with mana cost {1}{U} (printed 0/0 per CR
///   706.10 — Phantasmal Image's printed P/T is overwritten by CopyEffect
///   when it enters as a copy; if it doesn't copy, the 0/0 dies to SBA
///   per CR 704.5f).
/// - Enters-as-copy replacement (CR 706.10) via the shared
///   <see cref="EntersAsCopyReplacement"/> with pool
///   <see cref="EntersAsCopyReplacement.CopyPool.AnyBattlefield"/>. The
///   replacement is registered against the provided <see cref="ReplacementBus"/>
///   when the binder-aware overload is used; the single-arg dispatcher
///   path produces shape only.
/// - "Illusion in addition to its other types" (CR 613.1d Layer 4 type-
///   adding rider) via <see cref="AddSubtypeEffect"/>(Illusion). Wired
///   on the supplied <see cref="ContinuousEffectsService"/> so the
///   subtype shows up in the layer-computed characteristics even after a
///   future <see cref="CopyEffect"/> grows to copy subtypes too — at
///   v1, CopyEffect mirrors P/T + keywords only, so the printed Illusion
///   subtype already sticks; AddSubtypeEffect keeps the rider correct
///   under the future subtype-copy expansion.
/// - Targeted-by-spell-or-ability sacrifice trigger (CR 603.6c, 115.6).
///   Fires on <see cref="TargetsChosenEvent"/> when any chosen target
///   references this Phantasmal Image — both spells AND
///   activated/triggered abilities trigger it (unlike Bonecrusher Giant
///   which is spell-only). On resolution the card is sacrificed (moved
///   to its owner's graveyard via <see cref="OracleSpellBinder.MoveToGraveyard"/>).
///
/// ## Deferred (v1 gaps)
/// - "You may" choice — <see cref="EntersAsCopyReplacement"/> auto-yes
///   when any candidate exists; no agent prompt yet. Tests cover
///   "decline" by leaving the battlefield empty (no candidates → enters
///   as printed 0/0).
/// - Self-sacrifice via SBA vs explicit move — production game flow
///   should route the sac through a sacrifice intent that publishes
///   appropriate events; the v1 effect uses
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> directly (same
///   pattern as Dress Down's end-step self-sac).
/// </summary>
[CardName("Phantasmal Image")]
public static class PhantasmalImageFactory
{
    /// <summary>
    /// Construct Phantasmal Image with no live event-bus, replacement-bus,
    /// or trigger-manager wiring. The targeted-by-spell-or-ability sacrifice
    /// trigger is attached to the card so structural / dispatch tests see
    /// the ability shape, but neither the enters-as-copy replacement nor
    /// the trigger is registered with their respective services.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, replacements: null, effects: null);

    /// <summary>
    /// Construct Phantasmal Image with optional event bus, trigger manager,
    /// replacement bus, and continuous-effects service. When all four are
    /// supplied:
    /// <list type="bullet">
    ///   <item>The enters-as-copy replacement (CR 706.10) is registered on
    ///         <paramref name="replacements"/> so a ZoneService move onto
    ///         the battlefield triggers <see cref="CopyEffect"/> via the
    ///         shared <see cref="EntersAsCopyReplacement"/>.</item>
    ///   <item>The "Illusion in addition" rider is registered on
    ///         <paramref name="effects"/> as an <see cref="AddSubtypeEffect"/>.</item>
    ///   <item>The targeted-by-spell-or-ability sacrifice trigger is
    ///         registered on <paramref name="triggers"/> so the bus surfaces
    ///         it as pending when any spell or ability picks this card.</item>
    /// </list>
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed: Creature — Illusion {1}{U}, 0/0.
        var card = new Creature(
            name: "Phantasmal Image",
            manaCost: "{1}{U}",
            power: 0,
            toughness: 0,
            subtypes: new[] { CardSubtype.Illusion });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Enters-as-copy replacement (CR 706.10). Reuses the shared
        // EntersAsCopyReplacement with pool AnyBattlefield. When the
        // continuous-effects service is supplied, the replacement also
        // registers a CopyEffect against the entering Phantasmal Image
        // (using the v1 deterministic first-candidate pick).
        // ----------------------------------------------------------------
        if (replacements != null && effects != null)
        {
            replacements.Register(new EntersAsCopyReplacement(
                card,
                EntersAsCopyReplacement.CopyPool.AnyBattlefield,
                effects));

            // CR 613.1d Layer 4 — "except it's an Illusion in addition to
            // its other types". The printed Illusion subtype is already
            // on the card, but registering an AddSubtypeEffect here keeps
            // the rider correct under a future CopyEffect that mirrors
            // subtypes (today CopyEffect handles P/T + keywords only).
            effects.Register(new AddSubtypeEffect(card, CardSubtype.Illusion));

            // Plumb ContinuousEffects into the card so P/T lookups consult
            // the layer system (CR 613). The CopyEffect registered by the
            // replacement's Replace() callback writes the source's P/T,
            // which is read back via Creature.GetPower/GetToughness.
            card.ActiveEffects = effects;
        }

        // ----------------------------------------------------------------
        // Targeted-by-spell-or-ability self-sacrifice trigger — CR
        // 603.6c, 115.6.
        //   "When this creature becomes the target of a spell or ability,
        //    sacrifice it."
        //
        // Fires on TargetsChosenEvent where ANY chosen target references
        // this Phantasmal Image. Unlike Bonecrusher Giant (spell-only),
        // both spells and abilities (activated/triggered) trigger this.
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            // Match on any chosen target referencing this card — Permanent
            // or Card target types (CR 115.4: spells/abilities target
            // permanents on the battlefield, so Permanent is the common
            // case; Card covers grave/exile-zone targeting for symmetry
            // with Bonecrusher's predicate).
            return e.Targets.Any(t =>
                (t.TargetType == TargetType.Permanent || t.TargetType == TargetType.Card)
                && t is Target concrete
                && ReferenceEquals(concrete.TargetObject, card));
        });

        var sacEffect = new Effect(
            "Phantasmal Image: sacrifice it",
            () =>
            {
                // Only sacrifice if still on the battlefield — if it's
                // already left (e.g. a previous trigger this turn already
                // sacrificed it, or another effect moved it), the
                // sacrifice is a no-op (CR 701.16, CR 603.7c).
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 701.16 — sacrifice bypasses Indestructible /
                // regeneration (CR 702.12b, CR 701.15c). Pass the
                // Sacrifice reason so the binder doesn't gate.
                OracleSpellBinder.MoveToGraveyard(card, Majik.Core.Zones.ZoneMoveReason.Sacrifice);

                // Drop the AddSubtypeEffect once the card has left the
                // battlefield — the Illusion subtype rider only applies
                // while Phantasmal Image is on the battlefield, and
                // pruning keeps the effects list clean. The CopyEffect
                // also expires naturally (CopyEffect's AppliesTo gates
                // on reference equality, but the effect would otherwise
                // linger in the registry without a battlefield gate).
                effects?.Prune();
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { sacEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        // Live registration with TriggerManager so the bus actually
        // surfaces the trigger as pending when a spell or ability targets
        // this card.
        triggers?.RegisterTriggeredAbility(trigger);

        // Avoid "unused parameter" warnings — eventBus is currently not
        // consulted directly by this factory (sacrifice is modelled as a
        // raw zone move per Dress Down's pattern; no DamageDealtEvent or
        // similar to publish here). Retained in the signature for parity
        // with other live-wiring overloads (Bonecrusher / Dress Down) and
        // for future extension if the sacrifice grows event publication.
        _ = eventBus;

        return card;
    }
}
