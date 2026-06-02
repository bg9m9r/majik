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
/// Named-card factory for Springbloom Druid (Modern Horizons 2, {2}{G}).
///
/// Creature — Elf Druid 1/1. Oracle text:
///   "When this creature enters, you may sacrifice a land. If you do, search
///    your library for up to two basic land cards, put them onto the
///    battlefield tapped, then shuffle."
///
/// ## Shape source
/// Card identity (name, {2}{G}, 1/1, Creature — Elf Druid) is loaded from
/// <c>Majik.Core/CardData/Cards/springbloom-druid.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The single ETB triggered ability is
/// attached in code below — same posture as the analogue
/// <see cref="BorderlandRangerFactory"/> (also a JSON-shape + hand-rolled ETB
/// tutor) and <see cref="BurnishedHartFactory"/> (the up-to-two-basics →
/// battlefield-tapped tutor body this card reuses verbatim).
///
/// ## Implemented (v1)
/// - 1/1 Elf Druid (CR 205.3m) at {2}{G}.
/// - <b>ETB trigger (CR 603.6a)</b>: "you may sacrifice a land. If you do,
///   search your library for up to two basic land cards, put them onto the
///   battlefield tapped, then shuffle."
///   - <b>"You may sacrifice a land"</b>: an optional additional sacrifice
///     gating the rest of the effect (CR 601.2b-style "if you do"). The
///     controller's agent is asked <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///     with <see cref="BotIntent.Ramp"/> — sacrificing one land to fetch two
///     basics is net +1 land, an upside, so the default bot accepts. If the
///     controller declines (or controls no land to sacrifice) the search is
///     skipped entirely (CR 608.2 — the "if you do" clause fails its
///     condition, so its dependent search never happens).
///   - The land to sacrifice is chosen via
///     <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> over the lands the
///     controller controls (CR 701.16 — "sacrifice a land" lets the controller
///     pick which one). It moves Battlefield → owner's graveyard.
///   - <b>Search up to two basics → battlefield tapped</b>: identical body to
///     <see cref="BurnishedHartFactory"/> — consult the agent twice via
///     <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for two basic-land
///     picks (CR 305.6 — Basic supertype + Land card type; CR 701.19a — agent
///     may decline either pick, "up to two" permits 0..2), move each pick to
///     the battlefield with the printed "tapped" rider applied AFTER the move
///     (so ETB-tapped replacements like snow basics have already applied),
///     then shuffle ONCE via <see cref="LibraryShuffle.ShuffleLibrary"/>
///     (CR 701.20a — one shuffle per search effect even when finding two cards).
///   - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/>
///     so ETB-tapped replacements and <c>CardMovedEvent</c> subscribers
///     (Amulet of Vigor, Lotus Cobra) fire on each tutored basic. Raw-zone
///     fallback when no live service is wired (shape / dispatcher tests).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the sacrificed land's move to the
///   graveyard is performed directly in the closure — same posture as
///   <see cref="BurnishedHartFactory"/> / Sakura-Tribe Elder (the engine's
///   generic sacrifice-cost payment is a no-op stub).
/// - <b>Reveal event</b>: the tutored basics move Library → Battlefield without
///   publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Springbloom Druid")]
public static class SpringbloomDruidFactory
{
    public const string CardName = "Springbloom Druid";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("springbloom-druid");

    /// <summary>
    /// Construct Springbloom Druid with its ETB trigger attached to the card
    /// shape but NOT registered with a <see cref="TriggerManager"/>. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Springbloom Druid with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the relevant <c>CardMovedEvent</c> places it on the stack
    /// automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you may sacrifice a land. If you do,
        //    search your library for up to two basic land cards, put them
        //    onto the battlefield tapped, then shuffle."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: may sac a land -> tutor up to two basics to battlefield tapped",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return MaybeSacLandThenTutorAsync(controller, ctx);
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
    /// "You may sacrifice a land. If you do, search ... up to two basic land
    /// cards ... onto the battlefield tapped, then shuffle." (CR 603.6a)
    /// Asks the controller whether to sacrifice (default bot says yes — net
    /// +1 land is an upside, <see cref="BotIntent.Ramp"/>), picks a land to
    /// sacrifice, then runs the up-to-two-basics tutor only if a land was
    /// actually sacrificed (CR 608.2 — the "if you do" condition gates the
    /// search).
    /// </summary>
    private static async ValueTask MaybeSacLandThenTutorAsync(Player player, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        // Lands the controller controls and could sacrifice (CR 701.16 —
        // "sacrifice a land" lets the controller choose which one). No land =>
        // the "may" can't be paid, so the search never happens.
        var sacrificeable = player.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();
        if (sacrificeable.Count == 0) return;

        // "you may sacrifice a land" — optional. Net +1 land is an upside so
        // the default bot accepts (BotIntent.Ramp). A remote agent overrides
        // ChooseYesNoAsync to prompt the UI.
        bool wantsToSac = agent != null
            ? await agent.ChooseYesNoAsync(
                    "Sacrifice a land to search for up to two basic lands?",
                    BotIntent.Ramp)
                .ConfigureAwait(false)
            : true;
        if (!wantsToSac) return;

        // Pick WHICH land to sacrifice (CR 701.16 — controller's choice).
        ICard? sac = agent != null
            ? await agent.ChooseFromBattlefieldAsync(player, sacrificeable, BotIntent.Ramp)
                .ConfigureAwait(false)
            : sacrificeable[0];
        if (sac == null) return;

        // CR 701.16 — move the sacrificed land Battlefield → owner's graveyard.
        // Done directly: the generic sacrifice-cost payment is a no-op stub
        // (same posture as Burnished Hart / Sakura-Tribe Elder).
        var sacController = sac.Controller ?? player;
        sacController.Zones.Battlefield.RemoveCard(sac);
        sac.Owner!.Zones.Graveyard.AddCard(sac);
        sac.SetZone(ZoneType.Graveyard);

        // "If you do" — a land WAS sacrificed, so run the search.
        await TutorUpToTwoBasicsToBattlefieldTappedAsync(player, ctx).ConfigureAwait(false);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for up to two basic land
    /// cards (CR 305.6 — Basic supertype + Land card type), consult the agent
    /// twice (each pick may decline, "up to two" permits 0..2; deterministic
    /// first-two-basics fallback when no agent), move each pick to the
    /// battlefield with the printed "tapped" rider applied after the move, then
    /// shuffle once (CR 701.20a — one shuffle per search effect even when
    /// multiple cards are found). Identical body to
    /// <see cref="BurnishedHartFactory"/>.
    /// </summary>
    private static async ValueTask TutorUpToTwoBasicsToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);
        var picks = new List<ICard>(capacity: 2);

        // First pick.
        var firstCandidates = player.Zones.Library.GetCards()
            .Where(IsBasicLand).ToList();
        if (firstCandidates.Count > 0)
        {
            ICard? first = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, firstCandidates,
                        "basic land card to put onto the battlefield tapped")
                    .ConfigureAwait(false)
                : firstCandidates[0];
            if (first != null) picks.Add(first);
        }

        // Second pick (excluding the first).
        var secondCandidates = player.Zones.Library.GetCards()
            .Where(c => IsBasicLand(c) && (picks.Count == 0 || !ReferenceEquals(c, picks[0])))
            .ToList();
        if (secondCandidates.Count > 0)
        {
            ICard? second = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, secondCandidates,
                        "basic land card to put onto the battlefield tapped")
                    .ConfigureAwait(false)
                : secondCandidates[0];
            if (second != null) picks.Add(second);
        }

        var zones = ZoneServiceRegistry.Get(player);
        foreach (var pick in picks)
        {
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
                if (pick is Permanent permTapped && !permTapped.IsTapped)
                {
                    permTapped.Tap();
                }
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
        LibraryShuffle.ShuffleLibrary(player, "springbloom-druid");
    }
}
