using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sea Gate Stormcaller (Zendikar Rising, {1}{U}).
/// Creature — Human Wizard 2/1. Oracle text (verified against Scryfall):
///   "Kicker {4}{U} (You may pay an additional {4}{U} as you cast this
///    spell.)
///    When this creature enters, copy the next instant or sorcery spell
///    with mana value 2 or less you cast this turn when you cast it. If
///    this creature was kicked, copy that spell twice instead. You may
///    choose new targets for the copies."
///
/// The base shape (name, Creature, Human + Wizard subtypes, {1}{U}, 2/1) is
/// materialised from the embedded JSON definition
/// (<c>sea-gate-stormcaller.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Kicker + the ETB delayed-copy
/// rider are layered on here — the JSON <c>AbilityDefinition</c> schema
/// doesn't yet express parameterised costs or delayed triggers (same posture
/// as <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Kicker {4}{U} (CR 702.33)</b>: shipped as a real
///   <see cref="KickerAdditionalCost"/> via <see cref="BuildAdditionalCost"/>
///   (same primitive Goblin Bushwhacker / Burst Lightning use). On payment
///   the cost stamps <see cref="Card.WasKicked"/> = true so the ETB body
///   sees the kicked posture (CR 702.33b — "if [spell] was kicked").
///   Registered in <see cref="Players.Agents.KickerAltCostProbe.DefaultLookup"/>
///   so the bot recognises the {4}{U} kicker without per-card bot wiring.
/// - <b>ETB delayed "copy next instant/sorcery MV≤2 you cast this turn"
///   rider (CR 603.6a → CR 603.7 / 603.8)</b>: an ETB
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>
///   (gated to this card entering the Battlefield) whose resolution
///   registers a one-shot <see cref="DelayedTriggeredAbility"/> against the
///   <see cref="TriggerManager"/>. The delayed condition matches the
///   controller's NEXT <see cref="SpellCastEvent"/> whose spell card is an
///   <see cref="CardType.Instant"/> or <see cref="CardType.Sorcery"/> with
///   <see cref="Card.ManaCostValue"/>.<see cref="ManaCost.TotalValue"/> ≤ 2;
///   on match, <see cref="SpellCopier.PushCopyOfTopSpell"/> re-executes the
///   captured spell's effect list (CR 707.10 — copy primitive, lossy v1
///   stub). Mirrors <see cref="GalvanicIterationFactory.BuildResolveEffect"/>.
/// - <b>Kicked → copy twice (CR 707.10)</b>: the kicked-ness is snapshotted
///   into the delayed trigger at ETB-resolution time (the ETB reads
///   <see cref="Card.WasKicked"/> when it registers the delayed rider). On a
///   kicked Stormcaller the captured spell's effect list is re-executed
///   TWICE (two independent copies); otherwise once.
///
/// ## Deferred (v1 gaps — inherited from <see cref="SpellCopier"/>)
/// - <b>"You may choose new targets for the copies" (CR 707.10a)</b>: the v1
///   copier reuses the original spell's targets verbatim; no re-target
///   prompt is surfaced. Tracked on <see cref="SpellCopier"/>.
/// - <b>Copies as distinct stack objects</b>: the v1 copier re-runs the
///   original's effect list in place rather than pushing new
///   <see cref="Majik.Core.Stack.IStackObject"/>s — subscribers to
///   <see cref="StackObjectAddedEvent"/> / <see cref="Majik.Core.Stack.Stack.Count"/>
///   won't see them. Same gap as Galvanic Iteration / Doublecast.
/// - <b>"this turn" expiry on the delayed trigger</b>: the delayed trigger
///   is one-shot and self-unregisters on first match; if no qualifying
///   instant/sorcery is cast for the rest of the turn the registration
///   silently lingers (an end-of-turn cleanup hook is deferred). Practical
///   "fires only on the NEXT qualifying spell" semantic is preserved. Same
///   posture as Galvanic Iteration.
/// - <b>Kicker cleanup timing for creature ETBs</b>: <see cref="WasKicked"/>
///   on a creature spell can be cleared by
///   <see cref="Majik.Core.Game.SpellCastFlow"/>'s post-body cleanup before
///   the ETB trigger resolves (the printed body of a creature spell is
///   empty). Same known v1 gap documented on
///   <see cref="GoblinBushwhackerFactory"/>; tests exercise the kicked
///   branch by pre-stamping <see cref="Card.SetWasKicked"/>.
/// </summary>
[CardName("Sea Gate Stormcaller")]
public static class SeaGateStormcallerFactory
{
    public const string CardName = "Sea Gate Stormcaller";
    public const string Slug = "sea-gate-stormcaller";
    public const string PrintedManaCost = "{1}{U}";
    public const string KickerCostText = "{4}{U}";

    /// <summary>CR 707.10 — max mana value of the spell whose copy the ETB
    /// rider makes: "instant or sorcery spell with mana value 2 or less".</summary>
    public const int MaxCopyableManaValue = 2;

    /// <summary>
    /// Construct Sea Gate Stormcaller with no live trigger wiring. The ETB
    /// trigger attaches structurally (it registers the delayed rider only
    /// when a <see cref="TriggerManager"/> + <see cref="Majik.Core.Stack.Stack"/>
    /// are threaded through). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, stack: null);

    /// <summary>
    /// Construct a Sea Gate Stormcaller whose ETB trigger, on resolution,
    /// registers the delayed "copy next instant/sorcery MV≤2 you cast"
    /// rider against the supplied <paramref name="triggers"/> /
    /// <paramref name="stack"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager that owns the delayed
    /// registration. Pass null for the shape-only path — the ETB body then
    /// no-ops.</param>
    /// <param name="stack">Active stack — forwarded to
    /// <see cref="SpellCopier.PushCopyOfTopSpell"/>. May be null (shape-only).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human + Wizard subtypes, {1}{U}, 2/1). The JSON carries no
        // abilities — Kicker + the ETB rider are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, copy the next instant or sorcery
        //    spell with mana value 2 or less you cast this turn when you
        //    cast it. If this creature was kicked, copy that spell twice
        //    instead."
        //
        // The ETB fires on this card moving to the battlefield. Its
        // resolution registers a one-shot delayed trigger (CR 603.7) that
        // fires on the controller's NEXT qualifying instant/sorcery cast.
        // The kicked-ness (→ copy count) is snapshotted at ETB-resolution
        // time from Card.WasKicked.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: register delayed 'copy next instant/sorcery (MV≤{MaxCopyableManaValue}) you cast this turn' trigger",
            () =>
            {
                if (triggers == null || stack == null) return;

                var controller = card.Controller ?? owner;

                // CR 702.33b / CR 707.10 — kicked Stormcaller copies the
                // spell twice; otherwise once. Snapshot now (ETB resolution).
                var copyCount = card.WasKicked ? 2 : 1;

                RegisterDelayedCopy(triggers, stack, controller, copyCount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Register the one-shot delayed "copy next instant/sorcery MV≤2 you
    /// cast" trigger (CR 603.7). On the controller's next qualifying
    /// <see cref="SpellCastEvent"/>, the captured spell's effect list is
    /// re-executed <paramref name="copyCount"/> times via
    /// <see cref="SpellCopier.PushCopyOfTopSpell"/> (CR 707.10 — v1 lossy
    /// stub). Exposed for unit tests so the kicked / unkicked copy counts
    /// can be exercised without a full ETB round-trip.
    /// </summary>
    /// <param name="copyCount">1 for an unkicked Stormcaller, 2 if kicked.</param>
    public static void RegisterDelayedCopy(
        TriggerManager triggers,
        Majik.Core.Stack.Stack stack,
        Player controller,
        int copyCount)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(controller);

        // The triggering spell is captured into a closure by the condition
        // — same plumbing as Galvanic Iteration. The condition runs at
        // event-publish time (TriggerManager.EvaluateTriggers), before the
        // queued effect resolves, so the capture is well-defined.
        ISpell? captured = null;

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var spell = e.Spell;
            if (!ReferenceEquals(spell.Controller, controller)) return false;

            var card = spell.Card;
            if (card is null) return false;

            var isInstantOrSorcery =
                card.HasType(CardType.Instant) || card.HasType(CardType.Sorcery);
            if (!isInstantOrSorcery) return false;

            // CR 707.10 — "with mana value 2 or less". Read the spell card's
            // mana value (X counts as 0 on the stack pre-payment; v1 uses
            // the printed cost's TotalValue, matching Amped Raptor's gate).
            if (card is not Card concrete) return false;
            if (concrete.ManaCostValue.TotalValue > MaxCopyableManaValue) return false;

            captured = spell;
            return true;
        });

        var copyEffect = new Effect(
            $"{CardName}: copy captured spell {copyCount}x (CR 707.10)",
            () =>
            {
                if (captured is null) return;

                // CR 707.10 — kicked Stormcaller makes two copies; each copy
                // is an independent re-execution of the captured spell's
                // effects (v1 SpellCopier stub).
                for (var i = 0; i < copyCount; i++)
                {
                    SpellCopier.PushCopyOfTopSpell(stack, captured);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: controller,
            controller: controller,
            condition: condition,
            effects: new IEffect[] { copyEffect });

        triggers.RegisterDelayed(delayed);
    }

    /// <summary>
    /// CR 702.33 — construct Sea Gate Stormcaller's Kicker {4}{U} rider for
    /// the supplied <paramref name="card"/> instance. Layer the returned
    /// cost onto the cast via
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter (same wiring shape as Goblin
    /// Bushwhacker). On payment the cost stamps <see cref="Card.WasKicked"/>.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }
}
