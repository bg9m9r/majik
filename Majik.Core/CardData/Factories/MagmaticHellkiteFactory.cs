using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Magmatic Hellkite (Tarkir: Dragonstorm, {2}{R}{R}).
///
/// Creature — Dragon 4/5. Oracle text (Scryfall, verified 2026-06-24):
///   "Flying
///    When this creature enters, destroy target nonbasic land an opponent
///    controls. Its controller searches their library for a basic land card,
///    puts it onto the battlefield tapped with a stun counter on it, then
///    shuffles. (If a permanent with a stun counter would become untapped,
///    remove one from it instead.)"
///
/// The base shape (name / Creature — Dragon / {2}{R}{R} / 4/5) is materialised
/// from the embedded JSON definition (<c>magmatic-hellkite.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Flying keyword marker and the
/// ETB triggered ability are layered on here — the JSON ability schema
/// expresses neither (same posture as <see cref="FloodpitsDrownerFactory"/> /
/// <see cref="TopiaryStomperFactory"/>).
///
/// ## Implemented (v1)
/// - <b>4/5 Creature — Dragon, {2}{R}{R}</b>, owner / controller wired.
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/> marker
///   (the NamedCardFactory path doesn't run the keyword binder, so attach
///   inline — same wiring as Floodpits' Flash / Vigilance markers).
/// - <b>ETB triggered ability (CR 603.6a)</b>, fired by
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>:
///   "destroy target nonbasic land an opponent controls. Its controller
///   searches their library for a basic land card, puts it onto the battlefield
///   tapped with a stun counter on it, then shuffles."
///   - 1..1 TargetRequest "target nonbasic land an opponent controls"
///     (mandatory — no printed "may"). The CandidateGatherer enumerates lands
///     that are NOT Basic (CR 305.6 — Basic supertype) controlled by an
///     opponent (CR 109.5), mirroring <see cref="FieldOfRuinFactory"/>'s
///     destroy-half scoping.
///   - On resolution (CR 608.2b legality re-check): if the chosen target is
///     still a nonbasic land on the battlefield controlled by an opponent, its
///     controller is snapshotted (CR 608.2b last-known-information — "its
///     controller" is the land's controller at resolution, before it leaves),
///     the land is destroyed to its owner's graveyard (CR 701.7), then THAT
///     controller searches their library for a basic land card, puts it onto
///     the battlefield TAPPED with one <see cref="CounterType.Stun"/> counter on
///     it (CR 701.18 / CR 122.1c), and shuffles (CR 701.20a). If the target is
///     illegal at resolution the whole ability does nothing (it has only the
///     one target — the tutor rider's "its controller" pronoun has no referent).
///   - The stun counter is honoured by the untap-step replacement in
///     <c>TurnDriver.UntapStep</c> (CR 122.1g — same source of truth Floodpits'
///     and Kaito's stun counters read).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape + Flying marker + the ETB trigger
///   attached (but NOT registered with a <see cref="TriggerManager"/>). The
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — also registers the ETB
///   trigger so the relevant <c>CardMovedEvent</c> puts it on the stack
///   automatically (CR 603.3).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-on-find</b>: the tutored basic moves Library → Battlefield
///   without a reveal event — same gap as every tutor factory
///   (<see cref="TopiaryStomperFactory"/> / <see cref="FieldOfRuinFactory"/>).
/// </summary>
[CardName("Magmatic Hellkite")]
public static class MagmaticHellkiteFactory
{
    public const string CardName = "Magmatic Hellkite";
    public const string Slug = "magmatic-hellkite";
    public const int StunCountersPlaced = 1;

    private const string FlyingKeyword = "Flying";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Magmatic Hellkite with the Flying marker and its ETB trigger
    /// attached to the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests. This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct a fully-wired Magmatic Hellkite. When <paramref name="triggers"/>
    /// is supplied the ETB trigger is registered so the relevant
    /// <c>CardMovedEvent</c> places it on the stack automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 (Flying) keyword marker. The combat-abilities subsystem reads
        // this marker for evasion — same wiring as Floodpits' keyword markers.
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));

        // ETB trigger (CR 603.6a).
        var etbTrigger = BuildEtbTrigger(card, owner);
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // --- ETB: destroy target nonbasic opp land; its controller tutors a basic
    //         to battlefield tapped + stunned ---------------------------------

    private static TriggeredAbility BuildEtbTrigger(Creature card, Player owner)
    {
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: destroy target nonbasic opponent land; its controller tutors a basic to battlefield tapped + stunned",
            async ctx =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal target at resolution = the whole ability
                // does nothing (its single target gone ⇒ "its controller" has no
                // referent, so the tutor rider is suppressed too).
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Land)) return;
                if (target.HasSupertype(CardSupertype.Basic)) return;
                if (target.Controller == null) return;
                if (ReferenceEquals(target.Controller, card.Controller ?? owner)) return;

                // Snapshot "its controller" BEFORE the land leaves the
                // battlefield (CR 608.2b last-known-information). This is the
                // player who searches for the basic — the opponent, not the
                // Hellkite's controller.
                var landController = target.Controller;

                // CR 701.7 — destroy the land to its owner's graveyard.
                DestroyToOwnersGraveyard(target);

                // "Its controller searches their library for a basic land card,
                // puts it onto the battlefield tapped with a stun counter on it,
                // then shuffles." (CR 701.18 search + CR 122.1c stun + CR 701.20a
                // shuffle.) Mandatory search (no "may") — may still fail to find.
                await TutorBasicToBattlefieldTappedStunnedAsync(landController, ctx)
                    .ConfigureAwait(false);
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
                    Description: "target nonbasic land an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 109.5 + CR 305.6 — lands that are NOT Basic, controlled
                    // by a player OTHER than the Hellkite's controller.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller ?? owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Land)
                                    && !c.HasSupertype(CardSupertype.Basic))
                        .Cast<object>()
                        .ToList()),
            });

        return etbTrigger;
    }

    /// <summary>
    /// CR 701.18 + CR 122.1c — search <paramref name="player"/>'s library for ONE
    /// basic land card (CR 305.6 — Basic supertype + Land card type), consult the
    /// agent (deterministic first-basic fallback when no agent), move the pick to
    /// the battlefield via <see cref="ZoneServiceRegistry"/> (so ETB-tapped
    /// replacements + <c>CardMovedEvent</c> subscribers fire), apply the printed
    /// "tapped" rider plus one stun counter after the move, then shuffle once
    /// (CR 701.20a). The search is mandatory but can fail to find when no basic
    /// land is in the library. Mirrors <see cref="TopiaryStomperFactory"/>'s
    /// tutor blended with <see cref="FloodpitsDrownerFactory"/>'s stun counter.
    /// </summary>
    private static async ValueTask TutorBasicToBattlefieldTappedStunnedAsync(Player player, ResolutionContext ctx)
    {
        if (player == null) return;

        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLand).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "basic land card to put onto the battlefield tapped with a stun counter")
                    .ConfigureAwait(false)
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(player);
            }

            if (pick is Permanent perm)
            {
                // CR 701.18 — onto the battlefield tapped.
                if (!perm.IsTapped) perm.Tap();
                // CR 122.1c — with one stun counter on it.
                perm.Counters.Add(CounterType.Stun, StunCountersPlaced);
            }
        }

        // CR 701.20a — a search effect shuffles the searched library even when
        // zero cards were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, Slug);
    }

    /// <summary>
    /// CR 701.7 — move <paramref name="card"/> from the battlefield to its
    /// OWNER's graveyard (destroy). Mirrors
    /// <see cref="FieldOfRuinFactory"/>'s destroy helper.
    /// </summary>
    private static void DestroyToOwnersGraveyard(ICard card)
    {
        var ownerOfCard = card.Owner;
        if (ownerOfCard == null) return;

        var holder = card.Controller ?? ownerOfCard;
        holder.Zones.Battlefield.RemoveCard(card);
        ownerOfCard.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
