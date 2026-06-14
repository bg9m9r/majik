using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Idyllic Grange (Throne of Eldraine).
///
/// Land — Plains. Oracle text (verified against Scryfall):
///   "({T}: Add {W}.)
///    This land enters tapped unless you control three or more other Plains.
///    When this land enters untapped, put a +1/+1 counter on target creature
///    you control."
///
/// Idyllic Grange is the white member of the ELD "grange / cottage" cycle of
/// nonbasic basic-typed lands. <see cref="WitchsCottageFactory"/> (the black
/// member) is the closest engine analogue — same Land + basic subtype + "enters
/// tapped unless you control three or more other &lt;basic type&gt;" gate + an
/// enters-untapped trigger. The only differences here are the gating subtype
/// (Plains vs Swamp), the produced colour ({W} vs {B}), and the enters-untapped
/// effect (place a +1/+1 counter on target creature you control vs graveyard
/// recur).
///
/// ## Implemented (v1)
/// - <b>Land with the <see cref="CardSubtype.Plains"/> subtype</b> — base shape
///   (name, type, Plains subtype, intrinsic {T}: Add {W} <see cref="ManaAbility"/>)
///   is materialised from the embedded JSON (<c>idyllic-grange.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The parenthesised reminder text
///   is the intrinsic Plains mana ability (CR 305.6 / 605.1 — mana abilities
///   don't use the stack). "Plains" subtype is set so downstream "is a Plains"
///   predicates and the enters-tapped gate's own Plains count work without
///   special-casing.
/// - <b>"enters tapped unless you control three or more other Plains"
///   (CR 614.1c)</b> — modelled as a <see cref="ConditionalEntersTappedReplacement"/>
///   whose predicate counts permanents with the Plains subtype on the
///   controller's battlefield, excluding Idyllic Grange itself (CR 109.2
///   "other"). ≥3 ⇒ enters untapped, otherwise enters tapped. This is the
///   subtype-count variant the generic <see cref="ConditionalEntersTappedBinder"/>
///   deliberately does NOT claim (it only matches "N or more/fewer other
///   lands"), so the predicate is declared inline. Registered only when a
///   <see cref="ReplacementBus"/> is supplied.
/// - <b>"When this land enters untapped, put a +1/+1 counter on target creature
///   you control." (CR 603.6e)</b> — a <see cref="TriggeredAbility"/> over
///   <see cref="CardMovedEvent"/> gated to this card entering the battlefield
///   AND being untapped at the moment the move event publishes.
///   <see cref="ZoneService"/> applies the enters-tapped intent (taps the
///   permanent) BEFORE publishing <see cref="CardMovedEvent"/>, so reading
///   <c>!IsTapped</c> in the trigger condition faithfully distinguishes the
///   "entered untapped" case (the replacement above either left it untapped or
///   tapped it first — same posture as <see cref="WitchsCottageFactory"/>). A
///   1..1 <see cref="TargetRequest"/> declares the "creature you control"
///   target slot. On resolution a single +1/+1 counter is placed on the chosen
///   creature via <c>Counters.Add(CounterType.PlusOnePlusOne, 1)</c> (same
///   primitive as <see cref="HeliodSunCrownedFactory"/>'s lifegain trigger).
///   CR 608.2b illegal-on-resolution rechecks gate out targets no longer on
///   the battlefield, no longer controlled by Idyllic Grange's controller, or
///   no longer creatures. Registered with the supplied
///   <see cref="TriggerManager"/> for bus-driven firing.
///
/// ## Lifecycle — overloads
/// The single-arg <see cref="Create(Player)"/> overload produces the correct
/// card shape — the enters-untapped trigger is attached for shape inspection
/// but not registered with a <see cref="TriggerManager"/>, and no enters-tapped
/// replacement is wired (no <see cref="ReplacementBus"/>). Use the wiring
/// overload to register the trigger + replacement for live firing.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent target legality at choose-time</b>: <see cref="TargetRequest"/>
///   carries empty <c>LegalCandidates</c> (mirrors Heliod / Witch's Cottage).
///   The resolution guard enforces the creature + battlefield + controller
///   checks per CR 608.2b. (Note: the counter effect is not "you may" — it is
///   mandatory; if no legal target exists the ability isn't put on the stack
///   per CR 603.3c, the standard targeted-trigger posture.)
/// </summary>
[CardName("Idyllic Grange")]
public static class IdyllicGrangeFactory
{
    public const string CardName = "Idyllic Grange";
    public const string Slug = "idyllic-grange";

    /// <summary>
    /// Construct Idyllic Grange with no runtime service wiring. The
    /// enters-untapped trigger is attached for shape inspection but is not
    /// registered with a <see cref="TriggerManager"/>; no enters-tapped
    /// replacement is wired (no <see cref="ReplacementBus"/>). Suitable for
    /// dispatcher path and shape-only tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>
    /// Construct Idyllic Grange with optional live wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "enters tapped unless you
    /// control three or more other Plains" replacement is registered
    /// (CR 614.1c).</param>
    /// <param name="triggers">When supplied, the "when this land enters
    /// untapped" +1/+1-counter trigger is registered for bus-driven firing
    /// (CR 603.6e).</param>
    public static Land Create(Player owner, ReplacementBus? replacements, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Land, Plains subtype, {T}: Add {W}) from the
        // embedded JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // "This land enters tapped unless you control three or more other
        // Plains." (CR 614.1c). Subtype-count predicate; CR 109.2 — "other"
        // excludes Idyllic Grange itself.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) => CountOtherPlains(controller, self) >= 3));
        }

        // ----------------------------------------------------------------
        // "When this land enters untapped, put a +1/+1 counter on target
        // creature you control." (CR 603.6e)
        //
        // Fires on CardMovedEvent → Battlefield for this card, gated to the
        // land being UNtapped at event-publish time. ZoneService taps the
        // permanent (when the enters-tapped intent is set) BEFORE publishing
        // CardMovedEvent, so !IsTapped here means it entered untapped.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbEffect = new Effect(
            "Idyllic Grange: put a +1/+1 counter on target creature you control",
            () =>
            {
                if (etb is null) return;
                if (etb.ChosenTargets.Count == 0) return;
                if (etb.ChosenTargets[0].Count == 0) return;
                if (etb.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution rechecks.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!ReferenceEquals(target.Controller, land.Controller)) return;
                if (!target.HasType(CardType.Creature)) return;

                target.Counters.Add(CounterType.PlusOnePlusOne, 1);
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
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        land.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return land;
    }

    /// <summary>
    /// Count permanents on <paramref name="controller"/>'s battlefield that
    /// have the Plains subtype, excluding <paramref name="self"/> (Idyllic
    /// Grange itself — CR 109.2 "other"). Includes all Plains regardless of
    /// whether they are basic or nonbasic (Plains-typed duals, shock lands
    /// after retype effects, snow-covered Plains, etc.).
    /// </summary>
    private static int CountOtherPlains(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Plains));
}
