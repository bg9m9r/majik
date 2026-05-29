using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Questing Beast (Throne of Eldraine, {2}{G}{G}).
///
/// Legendary Creature — Beast 4/4. Oracle text (verified against Scryfall):
///   "Vigilance, deathtouch, haste
///    Questing Beast can't be blocked by creatures with power 2 or less.
///    Combat damage that would be dealt by creatures you control can't be
///    prevented.
///    Whenever Questing Beast deals combat damage to an opponent, it deals
///    that much damage to target planeswalker that player controls."
///
/// The base shape (name, Legendary, Beast, {2}{G}{G}, 4/4) is materialised
/// from the embedded JSON definition (<c>questing-beast.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed behaviours are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// yet express keyword markers, block restrictions, or combat-damage
/// triggers, so they live in the factory (same posture as
/// <see cref="StormscaleScionFactory"/> and the other JSON-backed cards
/// whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>Vigilance (CR 702.20)</b>, <b>Deathtouch (CR 702.2)</b>,
///   <b>Haste (CR 702.10)</b> — <see cref="KeywordAbility"/> markers read
///   directly by <see cref="Majik.Core.Combat.CombatAbilities"/> (same
///   wiring as <see cref="AkromaAngelOfWrathFactory"/>). Haste overrides
///   summoning sickness (CR 302.6) so Questing Beast can attack the turn it
///   enters.
/// - <b>Can't be blocked by creatures with power 2 or less (CR 509.1b)</b>:
///   registered as a <see cref="CantBeBlockedExceptByEffect"/> on the
///   supplied <see cref="ContinuousEffectsService"/>. The effect's contract
///   is an <i>allowed-blocker</i> predicate (a blocker is legal iff the
///   predicate returns true), so the printed "can't be blocked by power ≤ 2"
///   is expressed as its inverse: allow only blockers with power ≥ 3.
///   Power is read live so pump/shrink effects flip block legality
///   correctly. Same predicate-threshold shape the effect's own doc cites
///   (Slith Firewalker / Skulking Knight) and the same registration wiring
///   as <see cref="SignalPestFactory"/>.
/// - <b>Combat-damage-to-an-opponent → damage a planeswalker that player
///   controls (CR 510 / 603.1)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="CombatDamageDealtEvent"/> gated to this card and a non-null
///   <see cref="DamageDealtEvent.TargetPlayer"/> (the "opponent" — in a
///   two-player game any player Questing Beast deals combat damage to is an
///   opponent; CR 102.4). The dealt amount and the damaged player are
///   captured off the event (Ragavan's capture-closure pattern). At
///   resolution the effect deals that much damage to a planeswalker the
///   damaged player controls via <see cref="Fx.DealDamageAny"/> (routes
///   Planeswalker targets to loyalty removal — CR 120.3 / 306.8). With no
///   planeswalker on the damaged player's battlefield the redirect is a
///   no-op (the printed ability requires a legal target; CR 603.3c removes
///   it from the stack if none exists).
///
/// ## Target selection
/// "target planeswalker that player controls" — the factory auto-selects
/// the first planeswalker the damaged player controls. The named-factory
/// surface carries no interactive single-target chooser (matching
/// <see cref="GiselaBladeOfGoldnightFactory"/> / <see cref="RagavanNimblePilfererFactory"/>);
/// the deterministic "first eligible" pick preserves the observable
/// contract (the damaged player's planeswalker loses that much loyalty)
/// without a prompt surface. Multi-planeswalker target choice is deferred.
///
/// ## Deferred (v1 gaps)
/// - <b>"Combat damage that would be dealt by creatures you control can't
///   be prevented" (CR 615.x prevention-suppression)</b>: NO-OP. The engine
///   has prevention <i>shields</i> (<see cref="PreventAllCombatDamageShield"/>
///   et al.) but no prevention-<i>suppression</i> surface to disable them —
///   the same documented gap deferred by <see cref="WildSlashFactory"/> and
///   <see cref="SkullcrackFactory"/> ("damage can't be prevented this turn").
///   Building that infrastructure here would be half-built engine work, so
///   the clause is intentionally not wired. The keyword/block/redirect
///   clauses are fully functional; this static is the lone deferral and
///   only matters opposite a prevention effect (rare in Modern).
/// - <b>Single-target chooser</b>: see Target selection above.
///
/// CR rule references: 205.2 (Legendary), 205.3m (Beast subtype), 509.1b
/// (block restrictions), 510 (combat damage), 603.1/603.3c (triggered
/// ability + targeting), 702.2 (Deathtouch), 702.10 (Haste), 702.20
/// (Vigilance), 120.3/306.8 (damage to a planeswalker = loyalty loss).
/// </summary>
[CardName("Questing Beast")]
public static class QuestingBeastFactory
{
    public const string CardName = "Questing Beast";
    public const string Slug = "questing-beast";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Minimum blocker power that may legally block Questing Beast. The
    /// printed "can't be blocked by creatures with power 2 or less" allows
    /// blockers with power 3+ (CR 509.1b).
    /// </summary>
    public const int MinBlockerPower = 3;

    /// <summary>
    /// Construct Questing Beast with no live wiring. Keyword markers and the
    /// combat-damage trigger are attached for shape; the block restriction is
    /// NOT registered (no effects service) and the trigger is not registered
    /// on a <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null);

    /// <summary>
    /// Construct Questing Beast with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Layers service the "can't be blocked by power
    /// ≤ 2" restriction registers against (also bound onto
    /// <see cref="Creature.ActiveEffects"/> so the combat validator picks it
    /// up). Pass null to skip the block restriction.</param>
    /// <param name="triggers">When supplied, the combat-damage trigger is
    /// registered so a <see cref="CombatDamageDealtEvent"/> automatically
    /// queues the ability.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary Creature —
        // Beast, {2}{G}{G}, 4/4). The JSON carries no abilities — keywords,
        // block restriction, and the combat trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Evergreen keywords — CR 702.20 Vigilance, CR 702.2 Deathtouch,
        // CR 702.10 Haste. KeywordAbility markers read directly by the
        // combat helpers (CombatAbilities.HasVigilance / HasDeathtouch /
        // HasHaste).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // CR 509.1b — "Questing Beast can't be blocked by creatures with
        // power 2 or less." Modeled as the inverse allowed-blocker predicate
        // on CantBeBlockedExceptByEffect: a blocker is legal iff its power is
        // >= 3 (MinBlockerPower). Power is read live so pump/shrink flips
        // legality. Only wired when a layers service is supplied.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            card.ActiveEffects = effects;
            effects.Register(new CantBeBlockedExceptByEffect(
                source: card,
                predicate: blocker => blocker is Creature c && c.Power >= MinBlockerPower));
        }

        // ----------------------------------------------------------------
        // CR 510 / 603.1 — "Whenever Questing Beast deals combat damage to
        // an opponent, it deals that much damage to target planeswalker that
        // player controls." Capture-closure pattern (mirrors Ragavan): the
        // condition stashes the dealt amount + damaged player off the event;
        // the resolved effect redirects that much damage to a planeswalker
        // the damaged player controls.
        // ----------------------------------------------------------------
        card.AddAbility(BuildPlaneswalkerRedirect(card, owner, triggers));

        return card;
    }

    /// <summary>
    /// Build the combat-damage-to-an-opponent → planeswalker redirect
    /// trigger. The dealt amount and damaged player are captured at
    /// condition time (CR 603.3 evaluates the condition before the ability
    /// hits the stack, so the capture is fresh at resolution).
    /// </summary>
    private static TriggeredAbility BuildPlaneswalkerRedirect(
        Creature card,
        Player controller,
        TriggerManager? triggers)
    {
        Player? capturedOpponent = null;
        var capturedAmount = 0;

        var effect = new Effect(
            "Questing Beast: deal that much combat damage to target planeswalker the damaged player controls (CR 510 / 120.3)",
            () =>
            {
                var opponent = capturedOpponent;
                if (opponent == null || capturedAmount <= 0) return;

                // CR 603.3c — "target planeswalker that player controls".
                // Auto-select the first planeswalker the damaged player
                // controls (no interactive chooser on the named-factory
                // surface — matches Gisela / Ragavan). With none on the
                // battlefield the ability would have no legal target and is
                // removed from the stack (CR 603.3c) — modeled here as a
                // no-op.
                var planeswalker = opponent.Zones.Battlefield.GetCards()
                    .OfType<Planeswalker>()
                    .FirstOrDefault();
                if (planeswalker == null) return;

                // CR 120.3 / 306.8 — damage to a planeswalker removes that
                // many loyalty counters. Fx.DealDamageAny routes Planeswalker
                // targets to RemoveLoyalty.
                Fx.DealDamageAny(planeswalker, capturedAmount);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: controller,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                // "deals combat damage to an opponent" — a player target
                // (CR 102.4: in a two-player game the other player is the
                // opponent). Creature/planeswalker combat damage doesn't fire.
                if (e.TargetPlayer == null) return false;
                capturedOpponent = e.TargetPlayer;
                capturedAmount = e.Amount;
                return true;
            }),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        triggers?.RegisterTriggeredAbility(trigger);

        return trigger;
    }
}
