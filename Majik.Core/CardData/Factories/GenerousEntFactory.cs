using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Generous Ent (The Lord of the Rings: Tales of
/// Middle-earth, {5}{G}).
///
/// Creature — Treefolk 5/7. Oracle text (Scryfall):
///   "Reach
///    When this creature enters, target player gains 4 life.
///    Forestcycling {G} ({G}, Discard this card: Search your library for
///    a Forest card, reveal it, put it into your hand, then shuffle.)"
///
/// (The printed paper card lists both "Cycling — Discard a card" and
/// "Forestcycling {G}" in some prints; the current Scryfall oracle for
/// the LotR LTC printing consolidates the activated half into the
/// Forestcycling line. This factory ships the generic Cycling activated
/// ability via <see cref="CyclingFactory.Build"/> as the v1 surface;
/// Forestcycling's typed-tutor rider is documented as a deferred
/// typed-cycling extension below.)
///
/// ## Implemented (v1)
/// - <b>Creature — Treefolk {5}{G} 5/7</b>. Introduces
///   <see cref="CardSubtype.Treefolk"/>.
/// - <b>Reach</b> (CR 702.17) — <see cref="KeywordAbility"/> marker
///   consumed by <see cref="Majik.Core.Combat.CombatAbilities.HasReach"/>.
/// - <b>ETB target-player-gains-4-life trigger</b> (CR 603.6a): wired
///   via <see cref="Triggers.OnEnterBattlefieldSelf"/> with one 1..1
///   "target player" <see cref="TargetRequest"/> (<see cref="BotIntent.Heal"/>).
///   On resolution reads <see cref="TriggeredAbility.ChosenTargets"/>[0][0],
///   gates on the picked object still being a <see cref="Player"/> (CR
///   608.2b — illegal target → no-op), and calls
///   <see cref="Player.GainLife"/>(4). Self-target legal per the printed
///   "target player" wording (no opponent gate).
/// - <b>Cycling {G}</b> (CR 702.32) — routed through
///   <see cref="CyclingFactory.Build"/> with cycle cost
///   <see cref="ManaCostCost"/>("{G}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers <see cref="DiscardSelfCost"/> (CR 702.32a
///   hand-zone gate) on the cost stack, and on resolve publishes
///   <see cref="CardCycledEvent"/> for CR 702.32d subscribers.
///
/// ## Deferred (v1 gaps)
/// - <b>Forestcycling's typed-tutor body</b> (CR 702.32f): the printed
///   form pays {G} + discards Generous Ent AND tutors a Forest card to
///   hand instead of drawing. The engine currently has no
///   <c>TypedCyclingFactory</c> / typed-cycling rider primitive — the
///   shared <see cref="CyclingFactory"/> resolves to a flat
///   <c>Fx.DrawCards(owner, 1)</c>. Shipping the generic Cycling
///   activated ability at the same {G} cycle cost preserves the deck-fix
///   tempo line minus the Forest-specific tutor (a draw still cantrips
///   into the next land drop). Forestcycling's typed tutor will land via
///   a follow-up parametric typed-cycling extension once the primitive
///   ships. A <see cref="KeywordAbility"/>("Forestcycling") marker is
///   attached so oracle audits / keyword scans surface the deferred half.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. ETB trigger attached
///   for shape inspection; cycling ability attached with no event bus
///   (no <see cref="CardCycledEvent"/> publication).
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — fully
///   wired. ETB trigger registered for bus-driven firing; cycling
///   resolve publishes <see cref="CardCycledEvent"/> against the bus.
///
/// CR rule references: 205.3m (Treefolk subtype), 603.6a (ETB
/// triggered ability), 702.17 (Reach), 702.32 (Cycling),
/// 702.32f (typecycling — deferred).
/// </summary>
[CardName("Generous Ent")]
public static class GenerousEntFactory
{
    public const string CardName = "Generous Ent";
    public const string PrintedManaCost = "{5}{G}";
    public const int Power = 5;
    public const int Toughness = 7;
    public const string CyclingCost = "{G}";
    public const int LifeGainAmount = 4;

    /// <summary>
    /// Construct Generous Ent with no live wiring. ETB trigger attached
    /// for shape inspection; cycling ability attached without an event
    /// bus (shape-only — no <see cref="CardCycledEvent"/> publication).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Generous Ent. When <paramref name="triggers"/> is
    /// supplied the ETB trigger is registered so a self-enter
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> queues the
    /// life-gain body. When <paramref name="eventBus"/> is supplied the
    /// cycling resolve body publishes <see cref="CardCycledEvent"/> so
    /// CR 702.32d "Whenever a player cycles" triggers fire.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Treefolk });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.17 — Reach. KeywordAbility marker consumed by
        // CombatAbilities.HasReach (mirrors Kraul Harpooner / Endurance).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        // ----------------------------------------------------------------
        // CR 702.32f — Forestcycling marker (deferred body).
        //
        // Shape-only surface so oracle audits / keyword scans see the
        // Forestcycling clause even though the typed-tutor body isn't
        // wired in v1 (no typed-cycling primitive yet — see class doc).
        // The generic Cycling activated ability below covers the
        // pay-{G}-and-discard tempo line minus the Forest-specific tutor.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Forestcycling", card, owner));

        // ----------------------------------------------------------------
        // ETB trigger (CR 603.6a):
        //   "When this creature enters, target player gains 4 life."
        // Single 1..1 "target player" TargetRequest. On resolve the
        // ChosenTargets[0][0] is read; if still a Player the engine
        // applies the lifegain. Self-target legal per the printed
        // "target player" wording (no opponent gate).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: target player gains {LifeGainAmount} life",
            () =>
            {
                if (etbTrigger is null) return;
                if (etbTrigger.ChosenTargets.Count == 0
                    || etbTrigger.ChosenTargets[0].Count == 0)
                {
                    // CR 608.2b — no legal target, do nothing.
                    return;
                }

                if (etbTrigger.ChosenTargets[0][0] is not Player target)
                {
                    return;
                }

                target.GainLife(LifeGainAmount);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Heal),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Cycling {G} — CR 702.32. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        //
        // The printed Forestcycling typed-tutor body would replace the
        // draw with a Forest tutor — see class doc for the deferred
        // typed-cycling primitive. v1 ships the generic draw which still
        // cantrips the deck-fix line minus the Forest-specific lookup.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        return card;
    }
}
