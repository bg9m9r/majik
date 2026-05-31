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
/// Named-card factory for Farhaven Elf (Shadowmoor, {2}{G}).
///
/// Creature — Elf Druid 1/1. Oracle text:
///   "When this creature enters, you may search your library for a basic land
///    card, put it onto the battlefield tapped, then shuffle."
///
/// ## Shape source
/// Card identity (name, {2}{G}, 1/1, Creature — Elf Druid) is loaded from
/// <c>Majik.Core/CardData/Cards/farhaven-elf.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The single ETB triggered ability is
/// attached in code below — same effect shape as the suggested analogue
/// <see cref="SolemnSimulacrumFactory"/> (which has an identical ETB tutor)
/// minus Solemn's separate "dies → draw" trigger. The JSON ability schema
/// does not yet express a "search for A basic land → battlefield tapped →
/// shuffle" effect, so it is hand-rolled here.
///
/// ## Implemented (v1)
/// - 1/1 Elf Druid, mana cost {2}{G}.
/// - <b>ETB trigger (CR 603.6a)</b>: "you may search your library for a basic
///   land card, put it onto the battlefield tapped, then shuffle." Searches
///   for ONE basic land (CR 305.6 — Basic supertype + Land card type),
///   consults the registered <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (CR 701.19a — agent may
///   decline; "you may" + the search can fail to find, both legal). Moves the
///   pick Library → Battlefield through <see cref="ZoneServiceRegistry"/> so
///   ETB-tapped replacements + <c>CardMovedEvent</c> subscribers fire, applies
///   the printed "tapped" rider after the move (CR 701.18), then shuffles ONCE
///   via <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a — a single
///   search effect performs one shuffle). Deterministic first-basic fallback
///   when no agent is registered — same posture as Solemn Simulacrum.
///
/// ## Deferred (v1)
/// - "You may" auto-accepts in v1 (the search consults the agent) —
///   consistent with the rest of the factory family.
/// - Tutored basic moves Library → Battlefield without a reveal event — same
///   gap as every tutor factory.
/// </summary>
[CardName("Farhaven Elf")]
public static class FarhavenElfFactory
{
    public const string CardName = "Farhaven Elf";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("farhaven-elf");

    /// <summary>
    /// Construct Farhaven Elf with its ETB trigger attached to the card shape
    /// but NOT registered with a <see cref="TriggerManager"/>. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Farhaven Elf with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger
    /// is registered so the entering <c>CardMovedEvent</c> places it on the
    /// stack automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you may search your library for a
        //    basic land card, put it onto the battlefield tapped, then
        //    shuffle."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: search a basic land -> battlefield tapped, then shuffle",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                await TutorOneBasicToBattlefieldTappedAsync(controller, ctx).ConfigureAwait(false);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent (which
    /// may decline; deterministic first-basic fallback when no agent), move
    /// the pick to the battlefield with the printed "tapped" rider applied
    /// after the move (CR 701.18), then shuffle once (CR 701.20a).
    /// </summary>
    private static async ValueTask TutorOneBasicToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLand).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "basic land card to put onto the battlefield tapped")
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

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "farhaven-elf");
    }
}
