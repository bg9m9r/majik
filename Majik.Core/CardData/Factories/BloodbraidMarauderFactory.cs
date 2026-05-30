using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodbraid Marauder (Modern Horizons 3, {1}{R}).
///
/// Creature — Human Berserker 3/1. Oracle text (verified against Scryfall
/// 2026-05-29):
///   "This creature can't block.
///    Delirium — This spell has cascade as long as there are four or more
///    card types among cards in your graveyard. (When you cast this spell,
///    exile cards from the top of your library until you exile a nonland
///    card that costs less. You may cast it without paying its mana cost.
///    Put the exiled cards on the bottom in a random order.)"
///
/// Bloodbraid Marauder composes three analogue shapes already in the engine:
/// - <b>"This creature can't block." (CR 509.1c)</b> — same non-expiring
///   <see cref="CombatRestrictionEffect"/> rider as
///   <see cref="GravecrawlerFactory"/> / Bloodghast.
/// - <b>Delirium-gated Cascade (CR 702.85 + CR 702.105)</b> — the cascade
///   <see cref="SpellCastEvent"/> trigger + <see cref="CascadeAction.Cascade"/>
///   routing from <see cref="ArdentPleaFactory"/> / <see cref="BloodbraidElfFactory"/>,
///   with the trigger condition AND-gated on
///   <see cref="DragonsRageChannelerFactory.IsDeliriumActive"/> (4+ card
///   types in the controller's graveyard). Unlike a printed-cascade card,
///   "this spell HAS cascade" is conditional: when delirium is off the
///   trigger does not match its own cast, so no cascade fires (CR 702.105
///   — the keyword is granted only while the condition holds).
///
/// The base card shape (name / Creature type / Human Berserker subtypes /
/// {1}{R} cost / 3/1 body) is materialised from the embedded JSON definition
/// (<c>bloodbraid-marauder.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the can't-block rider and the
/// cascade trigger are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses neither yet (same posture as
/// <see cref="ArdentPleaFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Creature shape</b> at printed cost {1}{R} (mana value 2 — a cascade
///   source whose hit pool is nonland MV ≤ 1 when delirium is live).
/// - <b>"This creature can't block." (CR 509.1c)</b> — non-expiring
///   <see cref="CombatRestriction.CannotBlock"/> rider, registered only on
///   the two-arg+ overloads (the shape-only path has no effects service,
///   mirroring Gravecrawler).
/// - <b>Delirium-gated Cascade trigger (CR 702.85 / CR 702.105)</b>:
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> for this
///   card, <see cref="TriggeredAbility.ActiveZones"/> = { Stack }. The
///   condition matches iff (a) the cast spell is this card AND (b) delirium
///   is active for the controller. On resolution invokes
///   <see cref="CascadeAction.Cascade"/> with <c>sourceManaValue: 2</c>; the
///   effect body re-checks delirium so a stale/inactive gate is a no-op.
///   The optional <c>willCast</c> predicate forwards the controller's "you
///   may" decision and <c>onCascadeResolved</c> hands the
///   <see cref="CascadeAction.CascadeResult"/> back for caller-driven
///   free-cast through <see cref="Costs.CastFromExileAlternativeCost"/>.
/// - <b>Cascade discovery</b>: registered in
///   <see cref="Players.Agents.CascadeAltCostProbe.DefaultIsCascadeCard"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Free-cast wiring</b>: caller-driven via the <c>onCascadeResolved</c>
///   hook (same shape as Ardent Plea / Bloodbraid Elf). The single-arg
///   dispatcher path attaches the trigger structurally with no SpellCastFlow
///   wired — suitable for shape / dispatcher tests.
/// </summary>
[CardName("Bloodbraid Marauder")]
public static class BloodbraidMarauderFactory
{
    public const string CardName = "Bloodbraid Marauder";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "bloodbraid-marauder";

    /// <summary>Cascade source mana value — Bloodbraid Marauder is MV 2 ({1}{R}).</summary>
    public const int CascadeSourceManaValue = 2;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches the cascade trigger structurally so the card shape is correct;
    /// the can't-block rider is skipped (no effects service) and the cascade
    /// effect has no result callback. No TriggerManager wiring.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null, willCast: null,
            onCascadeResolved: null);

    /// <summary>
    /// Construct with a <see cref="ContinuousEffectsService"/> for the
    /// can't-block rider (CR 509.1c) but no live trigger wiring.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects, triggers: null, willCast: null,
            onCascadeResolved: null);

    /// <summary>
    /// Fully-wired construction.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service for the non-expiring
    /// can't-block rider (CR 509.1c). May be null — the rider is then skipped
    /// (shape only, same posture as Gravecrawler).</param>
    /// <param name="triggers">TriggerManager to register the cascade trigger
    /// against. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="willCast">Controller's "you may" decision on the cascaded
    /// card (default = always cast).</param>
    /// <param name="onCascadeResolved">Receives the
    /// <see cref="CascadeAction.CascadeResult"/> for caller-driven free-cast.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers,
        Func<ICard, bool>? willCast = null,
        Action<CascadeAction.CascadeResult>? onCascadeResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Creature / Human Berserker / {1}{R} / 3/1) from
        // the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // "This creature can't block." — CR 509.1c. Non-expiring
        // CombatRestriction.CannotBlock scoped to this creature so
        // CombatValidator rejects block declarations naming it. Skipped on
        // the shape-only path (no effects service), mirroring Gravecrawler.
        // ----------------------------------------------------------------
        effects?.Register(new CombatRestrictionEffect(
            CombatRestriction.CannotBlock,
            target: card,
            expiresAtEndOfTurn: false));

        // ----------------------------------------------------------------
        // Delirium — "This spell has cascade as long as there are four or
        // more card types among cards in your graveyard." (CR 702.105).
        // Cascade (CR 702.85): "When you cast this spell, exile cards from
        // the top of your library until you exile a nonland card whose mana
        // value is less than this spell's mana value …"
        //
        // The cascade trigger condition is AND-gated on delirium being
        // active for the controller: when delirium is off the spell simply
        // has no cascade ability (CR 702.105 — the keyword is granted only
        // while the condition holds), so the trigger does not match its own
        // cast. The effect body re-checks delirium defensively so a stale
        // gate resolves to a no-op. Same SpellCastEvent + CascadeAction
        // routing as Ardent Plea / Bloodbraid Elf.
        // ----------------------------------------------------------------
        var cascadeCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card)
                && DragonsRageChannelerFactory.IsDeliriumActive(owner));

        var cascadeEffect = new Effect(
            $"{CardName} — Delirium Cascade (CR 702.85 / CR 702.105)",
            () =>
            {
                // Re-check delirium at resolution (CR 702.105): the spell
                // only has cascade while four-plus card types sit in the
                // graveyard.
                if (!DragonsRageChannelerFactory.IsDeliriumActive(owner)) return;

                var result = CascadeAction.Cascade(
                    controller: owner,
                    sourceManaValue: CascadeSourceManaValue,
                    willCast: willCast);
                onCascadeResolved?.Invoke(result);
            });

        var cascadeTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cascadeCondition,
            effects: new IEffect[] { cascadeEffect },
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(cascadeTrigger);
        triggers?.RegisterTriggeredAbility(cascadeTrigger);

        return card;
    }
}
