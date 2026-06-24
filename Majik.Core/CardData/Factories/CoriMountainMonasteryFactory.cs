using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cori Mountain Monastery (Tarkir: Dragonstorm). Land.
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "This land enters tapped unless you control a Plains or an Island.
///    {T}: Add {R}.
///    {3}{R}, {T}: Exile the top card of your library. Until the end of your
///    next turn, you may play that card."
///
/// A conditional-tapland mana factory with an impulse-draw activated ability —
/// the conditional ETB-tapped rider mirrors the Verge cycle's "Plains or an
/// Island" predicate (<see cref="FloodfarmVergeFactory"/>) and the impulse
/// ability reuses the shared <see cref="ExilePlayPermission"/> primitive that
/// Light Up the Stage / Tersa Lightshatter / the Reckless Impulse family use.
///
/// ## Implemented (v1)
/// - <b>Plain Land identity</b> — nonbasic, no printed subtype, no supertype.
///   The base shape (Land + the <c>{T}: Add {R}</c> mana ability) is
///   materialised declaratively from the embedded JSON definition
///   (<c>cori-mountain-monastery.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="SecludedGlenFactory"/>.
/// - <b>Conditional ETB-tapped (CR 614.1c)</b> — "This land enters tapped
///   unless you control a Plains or an Island." Wired as a
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>. Predicate: enters untapped iff the
///   controller controls at least one permanent with the Plains or Island
///   subtype (CR 305.6 / 205.3i — basic Plains/Island, dual lands typed
///   Plains/Island, and type-granting effects all qualify). Same shape as
///   <see cref="FloodfarmVergeFactory"/>'s "{U} only if you control a Plains or
///   an Island" mana-restriction predicate. The entering land is excluded from
///   the search by reference equality (it carries no Plains/Island subtype
///   anyway).
/// - <b>{T}: Add {R}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1, no
///   stack) declared in the JSON def.
/// - <b>{3}{R}, {T}: impulse from library top</b> — wired as an
///   <see cref="ActivatedAbility"/> (CR 602 — ordinary activated ability, uses
///   the stack) with a hybrid cost: the <see cref="ManaCostCost"/> {3}{R} plus
///   the <c>{T}</c> tap-self cost (<see cref="Primitives.Costs.TapSelf"/>, CR 602.5 /
///   605.3a). On resolution it exiles the top card of the controller's library
///   (CR 701.20) and stamps the reusable "you may play that card" permission
///   (<see cref="ExilePlayPermission.GrantUntil"/> with
///   <see cref="ExilePlayExpiry.EndOfYourNextTurn"/>) — which covers BOTH the
///   spell-cast half and the exiled-land land-play half (CR 305.2 / 601.1), so
///   an impulsed land is playable and an impulsed spell castable, both bounded
///   to "the end of your next turn" (CR 514.2 — the grant clears on the second
///   Cleanup the caster owns, the first being the activation turn's). With a
///   live <see cref="IEventBus"/> the revocation fires automatically; without
///   one the grant persists until cleared by hand (the test path).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — canonical / dispatcher path: no
///   <see cref="ReplacementBus"/> and no <see cref="IEventBus"/>. The mana
///   ability + the impulse ability are attached so the card surface is
///   complete; the ETB-tapped replacement is omitted (shape-only posture
///   matching every other ETB-replacement factory's single-arg path) and the
///   impulse grant does not auto-expire.
/// - <see cref="Create(Player, ReplacementBus?, IEventBus?)"/> — fully wired:
///   the ETB-tapped predicate registers on the bus and the impulse grant clears
///   at the caster's next-turn Cleanup via the event bus.
/// </summary>
[CardName("Cori Mountain Monastery")]
public static class CoriMountainMonasteryFactory
{
    public const string CardName = "Cori Mountain Monastery";
    public const string Slug = "cori-mountain-monastery";

    /// <summary>The impulse ability's mana cost rider ({3}{R}); the {T} cost is
    /// added alongside via <see cref="Primitives.Costs.TapSelf"/>.</summary>
    public const string ImpulseManaCost = "{3}{R}";

    /// <summary>
    /// Canonical build with no live wiring (the dispatcher / shape path). The
    /// mana ability and the impulse activated ability are attached; the
    /// ETB-tapped replacement is omitted and the impulse play-permission will
    /// not auto-expire without an event bus.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Cori Mountain Monastery with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">Replacement bus for the "enters tapped unless
    /// you control a Plains or an Island" rider (CR 614.1c). May be null — the
    /// land enters untapped unconditionally in that posture (mirrors every other
    /// conditional-tapped factory's shape-only single-arg path).</param>
    /// <param name="eventBus">Event bus for the impulse ability's "until the end
    /// of your next turn" expiry (CR 514.2). May be null — the grant persists
    /// until cleared by hand (the test path).</param>
    public static Land Create(
        Player owner,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (Land, no subtype/supertype, the {T}: Add {R} mana
        // ability) from the embedded JSON definition.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "This land enters tapped unless you control a Plains or an
        // Island." (CR 614.1c.) Predicate: enters UNTAPPED iff the
        // controller controls at least one permanent with the Plains or
        // Island subtype (CR 305.6 / 205.3i). Self excluded by reference.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (controller, self) =>
                    ControlsPlainsOrIsland(controller, self)));
        }

        // ----------------------------------------------------------------
        // {3}{R}, {T}: Exile the top card of your library. Until the end of
        // your next turn, you may play that card.
        //
        // CR 602 — ordinary activated ability (uses the stack). Hybrid cost:
        // the {3}{R} mana cost plus the {T} tap-self cost (CR 602.5 /
        // 605.3a). Resolution exiles the library-top card and stamps the
        // reusable "you may play that card until end of your next turn"
        // permission.
        // ----------------------------------------------------------------
        var impulseEffect = new Effect(
            $"{CardName}: exile top card of library; you may play it until end of your next turn",
            () => ResolveImpulse(land, owner, eventBus));

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ImpulseManaCost),
                Primitives.Costs.TapSelf(land),
            },
            effects: new IEffect[] { impulseEffect }));

        return land;
    }

    // -----------------------------------------------------------------------
    // Impulse resolution — "Exile the top card of your library. Until the end
    // of your next turn, you may play that card." (CR 701.20 / 118.9 / 514.2.)
    // -----------------------------------------------------------------------
    private static void ResolveImpulse(Land land, Player owner, IEventBus? eventBus)
    {
        var controller = land.Controller ?? owner;

        // "Exile the top card of your library" (CR 701.20). Empty library is a
        // clean no-op — there is no "tried to draw from empty library" flag for
        // an exile move (same posture as Light Up the Stage).
        var top = controller.Zones.Library.GetCards().OfType<Card>().FirstOrDefault();
        if (top == null) return;

        controller.Zones.Library.RemoveCard(top);
        controller.Zones.Exile.AddCard(top);
        top.SetZone(ZoneType.Exile);

        // "Until the end of your next turn, you may play that card." —
        // CR 118.9 / 514.2. The reusable permission covers BOTH the spell-cast
        // half and the exiled-land land-play half (CR 305.2 / 601.1); it clears
        // at the caster's SECOND Cleanup step (the first belongs to the
        // activation turn) when an event bus is supplied — else persists (the
        // test path clears it by hand). The grant pays the card's printed mana
        // cost ("you may play that card" with no alternate-cost rider).
        ExilePlayPermission.GrantUntil(
            top, controller, top.ManaCostValue,
            ExilePlayExpiry.EndOfYourNextTurn, eventBus);
    }

    /// <summary>
    /// CR 305.6 / 205.3i — does <paramref name="controller"/> control at least
    /// one permanent with the Plains or Island subtype? Used by the conditional
    /// ETB-tapped predicate. The entering <paramref name="self"/> is excluded by
    /// reference equality.
    /// </summary>
    private static bool ControlsPlainsOrIsland(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Any(c => !ReferenceEquals(c, self)
                   && (c.HasSubtype(CardSubtype.Plains)
                    || c.HasSubtype(CardSubtype.Island)));
}
