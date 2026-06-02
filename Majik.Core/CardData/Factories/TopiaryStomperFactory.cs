using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Topiary Stomper (Streets of New Capenna, {1}{G}{G}).
///
/// Creature — Plant Dinosaur 4/4. Oracle text (verified against Scryfall):
///   "Vigilance (Attacking doesn't cause this creature to tap.)
///    When this creature enters, search your library for a basic land card,
///    put it onto the battlefield tapped, then shuffle.
///    This creature can't attack or block unless you control seven or more
///    lands."
///
/// ## Shape source
/// Card identity (name, {1}{G}{G}, 4/4, Creature — Plant Dinosaur) is loaded
/// from <c>Majik.Core/CardData/Cards/topiary-stomper.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The printed behaviours (Vigilance
/// marker, the ETB tutor-to-battlefield-tapped trigger, and the
/// can't-attack-or-block-unless-seven-lands static) are layered on in code
/// below: the JSON ability schema doesn't express keyword markers, a
/// "search for A basic land → battlefield tapped → shuffle" effect, or a
/// predicate-mode combat restriction. Same posture as the suggested analogue
/// <see cref="SolemnSimulacrumFactory"/> (ETB tutor-to-battlefield-tapped) +
/// <see cref="HazoretTheFerventFactory"/> (predicate-mode can't-attack-or-block).
///
/// ## Implemented (v1)
/// - 4/4 Plant Dinosaur at {1}{G}{G}, owner / controller wired.
/// - <b>Vigilance (CR 702.20)</b>: a <see cref="KeywordAbility"/> marker. The
///   combat-abilities subsystem reads it via CombatAbilities.HasVigilance so
///   the creature does not tap when declared as an attacker — same wiring as
///   <see cref="StandingTroopsFactory"/>.
/// - <b>ETB trigger (CR 603.6a)</b>: "search your library for a basic land
///   card, put it onto the battlefield tapped, then shuffle." This search is
///   MANDATORY (no "you may") — but the search may still fail to find
///   (CR 701.19a), so a deck with no basic lands legally finds nothing.
///   Searches for ONE basic land (CR 305.6 — Basic supertype + Land card
///   type), consults the registered <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>, moves the pick
///   Library → Battlefield through <see cref="ZoneServiceRegistry"/> so
///   ETB-tapped replacements + <c>CardMovedEvent</c> subscribers fire, applies
///   the printed "tapped" rider after the move (CR 701.18), then shuffles ONCE
///   via <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a — a single
///   search effect performs one shuffle, found or not). Deterministic
///   first-basic fallback when no agent is registered — same posture as
///   <see cref="SolemnSimulacrumFactory"/>.
/// - <b>"can't attack or block unless you control seven or more lands"
///   (CR 508.1c / CR 509.1c)</b>: two predicate-mode
///   <see cref="CombatRestrictionEffect"/> instances
///   (<see cref="CombatRestriction.CannotAttack"/> +
///   <see cref="CombatRestriction.CannotBlock"/>), each self-scoped (the
///   predicate matches only when the queried creature IS this Stomper) and
///   tripping while the controller controls FEWER than seven lands ("unless
///   seven or more" == "while six or fewer"). The land count is read live, so
///   the lock lifts the instant a seventh land hits the battlefield. Gated on
///   the Stomper being on the battlefield (CR 603.6e). Same predicate-mode
///   shape as <see cref="HazoretTheFerventFactory"/>. Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape + Vigilance marker + the ETB
///   trigger attached (but NOT registered with a <see cref="TriggerManager"/>);
///   the combat restriction is NOT registered (no continuous-effects service).
///   The overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ContinuousEffectsService?)"/> —
///   fully wired: ETB trigger registered + the can't-attack-or-block
///   restriction registered.
///
/// ## Deferred (v1)
/// - Tutored basic moves Library → Battlefield without a reveal event — same
///   gap as every tutor factory (<see cref="SolemnSimulacrumFactory"/>,
///   <see cref="BorderlandRangerFactory"/>).
/// - <b>Bot attack/block planner</b>: the heuristic bot does not yet read the
///   land-count <see cref="CombatRestriction"/> when proposing attackers /
///   blockers; the engine rejects any illegal declaration the predicate
///   catches (same posture as Hazoret / Ensnaring Bridge).
/// </summary>
[CardName("Topiary Stomper")]
public static class TopiaryStomperFactory
{
    public const string CardName = "Topiary Stomper";
    public const string Slug = "topiary-stomper";

    /// <summary>
    /// CR 508.1c / 509.1c — "can't attack or block unless you control seven or
    /// more lands": the lock is active while the controller controls FEWER than
    /// this many lands.
    /// </summary>
    public const int LandsRequiredToAttackOrBlock = 7;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Topiary Stomper with the Vigilance marker and its ETB trigger
    /// attached to the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>, and the combat restriction NOT registered
    /// (no continuous-effects service). Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Topiary Stomper. When <paramref name="triggers"/>
    /// is supplied the ETB trigger is registered so the relevant
    /// <c>CardMovedEvent</c> places it on the stack automatically (CR 603.3).
    /// When <paramref name="continuousEffects"/> is supplied the two
    /// predicate-mode combat restrictions (CannotAttack + CannotBlock) are
    /// registered, gated on the Stomper being on the battlefield.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.20 — Vigilance marker. The combat-abilities subsystem reads
        // this marker so the creature does not tap when attacking.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, search your library for a basic land
        //    card, put it onto the battlefield tapped, then shuffle."
        // Mandatory search (no "you may"); may still fail to find.
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

        // ----------------------------------------------------------------
        // "This creature can't attack or block unless you control seven or
        // more lands." CR 508.1c (attack) + CR 509.1c (block).
        //
        // Predicate-mode CombatRestrictionEffect, self-scoped: the predicate
        // matches only when the queried creature IS this Stomper, and only
        // while the controller controls FEWER than seven lands ("unless seven
        // or more" == "while six or fewer"). The land count is read live every
        // validation pass, so the lock lifts the instant a seventh land enters.
        //
        // "you" — the Stomper's controller (CR 109.5). Gate: only active while
        // the Stomper is on the battlefield (CR 603.6e).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            bool LockedForCombat(Creature queried)
            {
                if (!ReferenceEquals(queried, card)) return false; // self-scoped
                var ctrl = card.Controller;
                if (ctrl == null) return false;
                return ControlledLandCount(ctrl) < LandsRequiredToAttackOrBlock;
            }

            bool OnBattlefield() => card.Zone == ZoneType.Battlefield;

            continuousEffects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotAttack,
                predicate: LockedForCombat,
                isActiveGate: OnBattlefield,
                expiresAtEndOfTurn: false));

            continuousEffects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotBlock,
                predicate: LockedForCombat,
                isActiveGate: OnBattlefield,
                expiresAtEndOfTurn: false));
        }

        return card;
    }

    /// <summary>
    /// CR 305 — count the lands <paramref name="player"/> controls on the
    /// battlefield (any card with the Land card type; basic / nonbasic alike).
    /// </summary>
    public static int ControlledLandCount(Player player) =>
        player.Zones.Battlefield.GetCards().Count(c => c.HasType(CardType.Land));

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent
    /// (deterministic first-basic fallback when no agent), move the pick to the
    /// battlefield with the printed "tapped" rider applied after the move
    /// (CR 701.18), then shuffle once (CR 701.20a). The search is mandatory but
    /// can fail to find when no basic land is in the library.
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
        LibraryShuffle.ShuffleLibrary(player, Slug);
    }
}
