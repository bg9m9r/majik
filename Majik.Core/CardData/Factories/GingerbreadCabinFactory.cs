using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gingerbread Cabin (Throne of Eldraine).
///
/// Land — Forest. Oracle text (verified against Scryfall):
///   "({T}: Add {G}.)
///    This land enters tapped unless you control three or more other Forests.
///    When this land enters untapped, create a Food token. (It's an artifact
///    with "{2}, {T}, Sacrifice this token: You gain 3 life.")"
///
/// Gingerbread Cabin is the green member of the ELD "cabin / cottage" cycle of
/// nonbasic basic-typed lands — the direct sibling of
/// <see cref="WitchsCottageFactory"/> (Land — Swamp, same enters-tapped gate +
/// enters-untapped trigger shape). The only behavioural difference is the
/// trigger's effect: Gingerbread Cabin creates a Food token (CR 111.10), where
/// Witch's Cottage recurs a creature card.
///
/// ## Implemented (v1)
/// - <b>Land with the <see cref="CardSubtype.Forest"/> subtype</b> — base
///   shape (name, Land type, Forest subtype) materialised from the embedded
///   JSON (<c>gingerbread-cabin.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON also carries the
///   intrinsic <b>{T}: Add {G}</b> <see cref="ManaAbility"/> (CR 605.1 — mana
///   ability, doesn't use the stack; the parenthesised reminder text is the
///   intrinsic Forest mana ability per CR 305.6). The Forest subtype is set so
///   downstream "is a Forest" predicates and the enters-tapped gate's own
///   Forest count work without special-casing.
/// - <b>"enters tapped unless you control three or more other Forests"
///   (CR 614.1c)</b> — a <see cref="ConditionalEntersTappedReplacement"/> whose
///   predicate counts permanents with the Forest subtype on the controller's
///   battlefield, excluding Gingerbread Cabin itself (CR 109.2 "other"). ≥3 ⇒
///   enters untapped, otherwise enters tapped. Subtype-count variant the
///   generic <see cref="ConditionalEntersTappedBinder"/> deliberately does NOT
///   claim, so the predicate is declared inline (same posture as Witch's
///   Cottage). Registered only when a <see cref="ReplacementBus"/> is supplied.
/// - <b>"When this land enters untapped, create a Food token" (CR 603.6e)</b> —
///   a <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/> gated to
///   this card entering the battlefield AND being untapped at the moment the
///   move event publishes. <see cref="ZoneService"/> applies the enters-tapped
///   intent (taps the permanent) BEFORE publishing <see cref="CardMovedEvent"/>,
///   so reading <c>!IsTapped</c> in the trigger condition faithfully
///   distinguishes the "entered untapped" case. The trigger has no target; on
///   resolution it always creates one Food token (CR 111.10 — colourless
///   artifact with "{2}, {T}, Sacrifice this token: You gain 3 life.") via
///   <see cref="TokenFactory.CreateFood"/>, threading the supplied
///   <see cref="ZoneService"/> so the token's ETB <see cref="CardMovedEvent"/>
///   fires. Registered with the supplied <see cref="TriggerManager"/> for
///   bus-driven firing.
///
/// ## Lifecycle — overloads
/// The single-arg <see cref="Create(Player)"/> overload produces the correct
/// card shape — the enters-untapped trigger is attached for shape inspection
/// but not registered with a <see cref="TriggerManager"/>, no enters-tapped
/// replacement is wired (no <see cref="ReplacementBus"/>), and the Food token
/// bypasses <see cref="ZoneService"/> when the trigger is driven manually. Use
/// the wiring overload to register the trigger + replacement for live firing.
/// </summary>
[CardName("Gingerbread Cabin")]
public static class GingerbreadCabinFactory
{
    public const string CardName = "Gingerbread Cabin";
    public const string Slug = "gingerbread-cabin";

    /// <summary>
    /// Construct Gingerbread Cabin with no runtime service wiring. The
    /// enters-untapped Food trigger is attached for shape inspection but is not
    /// registered with a <see cref="TriggerManager"/>; no enters-tapped
    /// replacement is wired (no <see cref="ReplacementBus"/>), and the Food
    /// token created on manual resolution bypasses <see cref="ZoneService"/>.
    /// Suitable for dispatcher-path and shape-only tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Gingerbread Cabin with optional live wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the "enters tapped unless you
    /// control three or more other Forests" replacement is registered
    /// (CR 614.1c).</param>
    /// <param name="triggers">When supplied, the "when this land enters
    /// untapped" Food trigger is registered for bus-driven firing
    /// (CR 603.6e).</param>
    /// <param name="zoneService">Zone service threaded into the Food token's
    /// creation so its ETB <see cref="CardMovedEvent"/> fires. May be null.</param>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Land, Forest subtype, {T}: Add {G}) from the
        // embedded JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // "This land enters tapped unless you control three or more other
        // Forests." (CR 614.1c). Subtype-count predicate; CR 109.2 — "other"
        // excludes Gingerbread Cabin itself.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) => CountOtherForests(controller, self) >= 3));
        }

        // ----------------------------------------------------------------
        // "When this land enters untapped, create a Food token." (CR 603.6e)
        //
        // Fires on CardMovedEvent → Battlefield for this card, gated to the
        // land being UNtapped at event-publish time. ZoneService taps the
        // permanent (when the enters-tapped intent is set) BEFORE publishing
        // CardMovedEvent, so !IsTapped here means it entered untapped. The
        // trigger has no target — it unconditionally creates one Food token
        // (CR 111.10) on resolution.
        // ----------------------------------------------------------------
        var foodEffect = new Effect(
            "Gingerbread Cabin: create a Food token",
            () => TokenFactory.CreateFood(land.Controller ?? owner, zoneService));

        var etb = new TriggeredAbility(
            source: land,
            controller: owner,
            // CR 603.6e — "enters untapped" trigger. The land has already had
            // the enters-tapped intent applied by the time CardMovedEvent
            // publishes, so IsTapped distinguishes the untapped entry.
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                ReferenceEquals(e.Card, land)
                && e.ToZone == ZoneType.Battlefield
                && land is Permanent p && !p.IsTapped),
            effects: new IEffect[] { foodEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return land;
    }

    /// <summary>
    /// Count permanents on <paramref name="controller"/>'s battlefield that
    /// have the Forest subtype, excluding <paramref name="self"/> (Gingerbread
    /// Cabin itself — CR 109.2 "other"). Includes all Forests regardless of
    /// whether they are basic or nonbasic (Forest-typed duals, shock lands
    /// after retype effects, etc.).
    /// </summary>
    private static int CountOtherForests(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasSubtype(CardSubtype.Forest));
}
