using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flagstones of Trokair (Time Spiral) — Legendary Land.
///
/// Oracle text:
///   "{T}: Add {W}.
///    When Flagstones of Trokair is put into a graveyard from the battlefield,
///    you may search your library for a Plains card, put it onto the
///    battlefield tapped, then shuffle."
///
/// ## Shape source
/// Card identity (name, Legendary supertype, Land type, the {T}: Add {W} mana
/// ability) is loaded from
/// <c>Majik.Core/CardData/Cards/flagstones-of-trokair.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="SolemnSimulacrumFactory"/>. The leaves-the-battlefield triggered
/// tutor is attached in code below because the JSON
/// <see cref="AbilityDefinition"/> schema does not yet model a
/// "put into a graveyard from the battlefield → search → enters tapped →
/// shuffle" trigger (it currently covers mana abilities only).
///
/// ## Implemented (v1)
/// - Legendary Land identity producing {W} (the JSON mana ability; CR 305.6).
/// - <b>Leaves-the-battlefield trigger (CR 603.6c / 603.10a)</b>: "When ~ is
///   put into a graveyard from the battlefield, you may search your library for
///   a Plains card, put it onto the battlefield tapped, then shuffle." This is
///   a zone-change trigger that looks back in time at the game state before the
///   land left (CR 603.6e / 603.10a) — modelled by
///   <see cref="Triggers.OnDies"/>, whose condition is the pure
///   Battlefield → Graveyard move (the same condition the engine uses for
///   creatures dying; lands are "put into a graveyard from the battlefield"
///   rather than "die" per CR 700.4, but the underlying CardMovedEvent is
///   identical). Active in both Battlefield and Graveyard because
///   <see cref="Majik.Core.Zones.ZoneService"/> stamps <c>card.Zone =
///   Graveyard</c> before publishing the move event — mirrors Solemn
///   Simulacrum's dies trigger.
/// - The search matches any <b>Plains card</b> — CR 205.4b: a "Plains card" is
///   any card with the Plains land subtype, so this picks up basic Plains AND
///   nonbasic Plains-typed lands (e.g. dual lands with the Plains type).
/// - "You may" + the search may fail to find: consults the registered
///   <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (which may decline);
///   deterministic first-Plains fallback when no agent is registered — same
///   posture as <see cref="SolemnSimulacrumFactory"/>.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/> so
///   ETB-tapped replacements and CardMovedEvent subscribers fire; the printed
///   "tapped" rider is applied after the move (CR 701.18), then the library is
///   shuffled once (CR 701.20a — shuffle whether or not a card was found).
///
/// ## Deferred (v1)
/// - "You may" auto-accepts the search in v1 (the search consults the agent;
///   no explicit decline-the-whole-trigger prompt) — consistent with the rest
///   of the tutor factory family.
/// - The tutored Plains moves Library → Battlefield without publishing a reveal
///   event — same gap as every tutor factory.
/// </summary>
[CardName("Flagstones of Trokair")]
public static class FlagstonesOfTrokairFactory
{
    public const string CardName = "Flagstones of Trokair";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("flagstones-of-trokair");

    /// <summary>
    /// Construct Flagstones of Trokair with its leaves-the-battlefield trigger
    /// attached to the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Flagstones of Trokair with optional
    /// <see cref="TriggerManager"/> wiring. When <paramref name="triggers"/> is
    /// supplied, the trigger is registered so the Battlefield → Graveyard
    /// <c>CardMovedEvent</c> places it on the stack automatically (CR 603.3).
    /// </summary>
    public static Land Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Leaves-the-battlefield triggered ability — CR 603.6c / 603.10a.
        //   "When Flagstones of Trokair is put into a graveyard from the
        //    battlefield, you may search your library for a Plains card, put it
        //    onto the battlefield tapped, then shuffle."
        // Active in Battlefield + Graveyard: ZoneService stamps the zone before
        // publishing the CardMovedEvent (mirrors Solemn Simulacrum's dies
        // trigger). "You may" auto-accepts in v1.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: search a Plains card -> battlefield tapped, then shuffle",
            async ctx =>
            {
                var controller = land.Controller ?? owner;
                await TutorPlainsToBattlefieldTappedAsync(controller, ctx).ConfigureAwait(false);
            });

        var ltbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnDies(land), // Battlefield -> Graveyard zone move
            effects: new IEffect[] { tutorEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        land.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return land;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for a Plains card (CR 205.4b —
    /// any card with the Plains land subtype, basic or nonbasic), consult the
    /// agent (which may decline; deterministic first-Plains fallback when no
    /// agent), move the pick to the battlefield with the printed "tapped" rider
    /// applied after the move (CR 701.18), then shuffle once (CR 701.20a —
    /// shuffle whether or not a card was found).
    /// </summary>
    private static async ValueTask TutorPlainsToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        bool IsPlainsCard(ICard c) =>
            c.HasType(CardType.Land) && c.HasSubtype(CardSubtype.Plains);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsPlainsCard).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "Plains card to put onto the battlefield tapped")
                    .ConfigureAwait(false)
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
                if (pick is Permanent perm && !perm.IsTapped) perm.Tap();
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(player);
                if (pick is Permanent perm) perm.Tap();
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero cards were
        // found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "flagstones-of-trokair");
    }
}
