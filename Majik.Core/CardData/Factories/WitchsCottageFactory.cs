using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Witch's Cottage (Throne of Eldraine).
///
/// Land — Swamp. Oracle text (verified against Scryfall):
///   "({T}: Add {B}.)
///    This land enters tapped unless you control three or more other Swamps.
///    When this land enters untapped, you may put target creature card from
///    your graveyard on top of your library."
///
/// Witch's Cottage is the black member of the ELD "cottage / cycle" of
/// nonbasic basic-typed lands (Mystic Sanctuary is the closest engine
/// analogue — Land — Island with the same recur-from-graveyard shape, but
/// gated by an intervening-if rather than an enters-untapped trigger).
///
/// ## Implemented (v1)
/// - <b>Land with the <see cref="CardSubtype.Swamp"/> subtype</b> — base
///   shape (name, type, Swamp subtype) is materialised from the embedded
///   JSON (<c>witchs-cottage.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON also carries the
///   <b>{T}: Add {B}</b> <see cref="ManaAbility"/> (CR 605.1 — mana ability,
///   doesn't use the stack; the parenthesised reminder text on the card is
///   the intrinsic Swamp mana ability per CR 305.6). "Swamp" subtype is set
///   so downstream "is a Swamp" predicates and the enters-tapped gate's own
///   Swamp count work without special-casing.
/// - <b>"enters tapped unless you control three or more other Swamps"
///   (CR 614.1c)</b> — modelled as a <see cref="ConditionalEntersTappedReplacement"/>
///   whose predicate counts permanents with the Swamp subtype on the
///   controller's battlefield, excluding Witch's Cottage itself (CR 109.2
///   "other"). ≥3 ⇒ enters untapped, otherwise enters tapped. This is the
///   subtype-count variant the generic
///   <see cref="ConditionalEntersTappedBinder"/> deliberately does NOT claim
///   (it only matches "N or more/fewer other lands"), so the predicate is
///   declared inline. Registered only when a <see cref="ReplacementBus"/> is
///   supplied.
/// - <b>"When this land enters untapped, you may put target creature card
///   from your graveyard on top of your library" (CR 603.6e)</b> — a
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> gated
///   to this card entering the battlefield AND being untapped at the moment
///   the move event publishes. <see cref="ZoneService"/> applies the
///   enters-tapped intent (taps the permanent) BEFORE publishing
///   <see cref="CardMovedEvent"/>, so reading <c>!IsTapped</c> in the trigger
///   condition faithfully distinguishes the "entered untapped" case (the
///   replacement above either left it untapped or tapped it first). A 1..1
///   <see cref="TargetRequest"/> declares the "creature card in your
///   graveyard" target slot. On resolution the chosen card is moved
///   Graveyard → top of Library via <see cref="IZone.InsertCardAt"/>(0)
///   (same primitive as Mystic Sanctuary / Mystical Tutor). CR 608.2b
///   illegal-on-resolution rechecks gate out cards no longer in the
///   graveyard, not owned by the controller, or no longer creature cards.
///   Registered with the supplied <see cref="TriggerManager"/> for bus-driven
///   firing.
///
/// ## Lifecycle — overloads
/// The single-arg <see cref="Create(Player)"/> overload produces the correct
/// card shape — the enters-untapped trigger is attached for shape inspection
/// but not registered with a <see cref="TriggerManager"/>, and no
/// enters-tapped replacement is wired (no <see cref="ReplacementBus"/>). Use
/// the wiring overload to register the trigger + replacement for live firing.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: the recur auto-takes the action when a target
///   was supplied; agent-driven decline is deferred (same posture as Mystic
///   Sanctuary / Snapcaster Mage / Tireless Tracker).
/// - <b>Agent target legality at choose-time</b>: <see cref="TargetRequest"/>
///   carries empty <c>LegalCandidates</c> (mirrors Mystic Sanctuary). The
///   resolution guard enforces the creature + graveyard + owner checks per
///   CR 608.2b.
/// </summary>
[CardName("Witch's Cottage")]
public static class WitchsCottageFactory
{
    public const string CardName = "Witch's Cottage";
    public const string Slug = "witchs-cottage";

    /// <summary>
    /// Construct Witch's Cottage with no runtime service wiring. The
    /// enters-untapped recur trigger is attached for shape inspection but is
    /// not registered with a <see cref="TriggerManager"/>; no enters-tapped
    /// replacement is wired (no <see cref="ReplacementBus"/>). Suitable for
    /// dispatcher path and shape-only tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>
    /// Construct Witch's Cottage with optional live wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "enters tapped unless you
    /// control three or more other Swamps" replacement is registered
    /// (CR 614.1c).</param>
    /// <param name="triggers">When supplied, the "when this land enters
    /// untapped" recur trigger is registered for bus-driven firing
    /// (CR 603.6e).</param>
    public static Land Create(Player owner, ReplacementBus? replacements, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Land, Swamp subtype, {T}: Add {B}) from the
        // embedded JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // "This land enters tapped unless you control three or more other
        // Swamps." (CR 614.1c). Subtype-count predicate; CR 109.2 — "other"
        // excludes Witch's Cottage itself.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) => CountOtherSwamps(controller, self) >= 3));
        }

        // ----------------------------------------------------------------
        // "When this land enters untapped, you may put target creature card
        // from your graveyard on top of your library." (CR 603.6e)
        //
        // Fires on CardMovedEvent → Battlefield for this card, gated to the
        // land being UNtapped at event-publish time. ZoneService taps the
        // permanent (when the enters-tapped intent is set) BEFORE publishing
        // CardMovedEvent, so !IsTapped here means it entered untapped.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbEffect = new Effect(
            "Witch's Cottage: put target creature card from graveyard on top of library",
            () =>
            {
                if (etb is null) return;
                if (etb.ChosenTargets.Count == 0) return;
                if (etb.ChosenTargets[0].Count == 0) return;
                if (etb.ChosenTargets[0][0] is not Card target) return;

                // CR 608.2b — illegal-on-resolution rechecks.
                if (target.Zone != ZoneType.Graveyard) return;
                if (target.Owner is null || !ReferenceEquals(target.Owner, owner)) return;
                if (!target.HasType(CardType.Creature)) return;

                owner.Zones.Graveyard.RemoveCard(target);
                owner.Zones.Library.InsertCardAt(0, target);
                target.SetZone(ZoneType.Library);
            });

        etb = new TriggeredAbility(
            source: land,
            controller: owner,
            // CR 603.6e — "enters untapped" trigger. The land has already had
            // the enters-tapped intent applied by the time CardMovedEvent
            // publishes, so IsTapped distinguishes the untapped entry.
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                ReferenceEquals(e.Card, land)
                && e.ToZone == ZoneType.Battlefield
                && land is Permanent p && !p.IsTapped),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        land.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return land;
    }

    /// <summary>
    /// Count permanents on <paramref name="controller"/>'s battlefield that
    /// have the Swamp subtype, excluding <paramref name="self"/> (Witch's
    /// Cottage itself — CR 109.2 "other"). Includes all Swamps regardless of
    /// whether they are basic or nonbasic (Swamp-typed duals, shock lands
    /// after retype effects, etc.).
    /// </summary>
    private static int CountOtherSwamps(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Swamp));
}
