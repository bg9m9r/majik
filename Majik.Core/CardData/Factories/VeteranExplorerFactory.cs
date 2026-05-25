using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Veteran Explorer (Mercadian Masques, {1}{G}).
///
/// Creature — Human Scout 1/2. Oracle text:
///   "When this creature dies, each player may search their library for up
///    to two basic land cards, put them onto the battlefield, then
///    shuffle."
///
/// ## Implemented (v1)
/// - 1/2 Creature — Human Scout (CardSubtype.Human + CardSubtype.Scout),
///   mana cost {1}{G}.
/// - <b>Death-trigger basic-land tutor (CR 603.6c / CR 700.4)</b> wired via
///   the shared <see cref="Triggers.OnDies"/> condition (ActiveZones =
///   Battlefield + Graveyard so the trigger still matches after
///   ZoneService stamps Zone=Graveyard before publishing the
///   <see cref="Majik.Core.Events.CardMovedEvent"/> — same posture as
///   Matter Reshaper / Wurmcoil Engine).
/// - On resolution iterates every player in APNAP order (CR 101.4) and
///   offers each player a TWO-land tutor (CR 701.19a): the agent's
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> is called up to
///   two times, each candidate filtered to basic land cards (CR 305.6).
///   Each picked card is moved Library → Battlefield untapped (no
///   "tapped" qualifier in the oracle text — Veteran Explorer entered
///   the Modern format on Mercadian Masques printing). Library is
///   shuffled per player after the search resolves
///   (CR 701.20a, <see cref="LibraryShuffle.ShuffleLibrary"/>).
/// - The "may" wording (CR 701.19a) is honoured by the agent returning
///   <c>null</c> (declines). When no agent is registered, the
///   deterministic first-basic-land pick (mirrors
///   <see cref="AssassinsTrophyFactory"/>) takes the up-to-two slots
///   greedily so shape tests have a stable observation.
///
/// ## Deferred (v1 gaps)
/// - <b>APNAP ordering</b> — v1 iterates the
///   <see cref="Create(Player, TriggerManager?, Func{IReadOnlyList{Player}}?)"/>
///   <c>allPlayersResolver</c>'s yield order. Full CR 101.4 APNAP
///   turn-order resolution needs a
///   <see cref="Majik.Core.Game.GameContext"/> handle, which factories
///   don't carry; the v1 fixed order matches every other "each player"
///   tutor factory (PathToExile-shape).
/// - <b>Reveal events</b> — the picked basic lands move Library →
///   Battlefield without publishing a reveal event (same gap as
///   <see cref="SylvanScryingFactory"/> / <see cref="AssassinsTrophyFactory"/>).
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + dies trigger attached
///   for shape inspection; the trigger's "each player" walk reduces to
///   the dying card's owner only (no broader player list available).
/// - <see cref="Create(Player, TriggerManager?, Func{IReadOnlyList{Player}}?)"/>
///   — registers the dies trigger with the supplied
///   <see cref="TriggerManager"/> and walks the supplied
///   <c>allPlayersResolver</c> on resolution so opponents tutor too.
/// </summary>
[CardName("Veteran Explorer")]
public static class VeteranExplorerFactory
{
    public const string CardName = "Veteran Explorer";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int MaxLandsPerPlayer = 2;

    /// <summary>Basic land names per CR 305.6.</summary>
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    /// <summary>
    /// Shape-only overload — attaches the dies trigger to the card without
    /// registering with a <see cref="TriggerManager"/>. The trigger's
    /// "each player" walk reduces to the dying card's owner only (no
    /// broader player list available).
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Veteran Explorer with its dies trigger attached and
    /// optionally registered against the supplied
    /// <paramref name="triggers"/> manager.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the dies trigger registers
    /// so a qualifying <see cref="Majik.Core.Events.CardMovedEvent"/>
    /// automatically queues the ability on the stack.</param>
    /// <param name="allPlayersResolver">When supplied, the dies trigger
    /// walks the resolver's player list on resolution so each player
    /// gets their basic-land tutor. Null → owner-only (shape tests).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Scout });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c / CR 700.4.
        //   "When this creature dies, each player may search their library
        //    for up to two basic land cards, put them onto the battlefield,
        //    then shuffle."
        //
        // ActiveZones = Battlefield + Graveyard (Wurmcoil posture) so the
        // trigger still matches after ZoneService has stamped the card's
        // Zone = Graveyard before publishing CardMovedEvent.
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName}: each player may tutor up to two basic lands to battlefield, then shuffle",
            () =>
            {
                // Walk the configured "each player" resolver (CR 101.4
                // APNAP). When unset (shape-test path) reduce to the
                // dying card's owner only — deterministic single-player
                // observation that mirrors the AssassinsTrophy shape.
                var players = allPlayersResolver?.Invoke() ?? new[] { owner };
                foreach (var player in players)
                {
                    if (player == null) continue;
                    TutorUpToTwoBasicsToBattlefield(player);
                    Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "veteran-explorer");
                }
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            // ActiveZones = Battlefield + Graveyard so the trigger matches
            // after ZoneService stamps Zone = Graveyard pre-publish.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }

    /// <summary>
    /// Tutor up to <see cref="MaxLandsPerPlayer"/> basic lands from
    /// <paramref name="player"/>'s library and put them onto the
    /// battlefield untapped (CR 701.19a). Greedy fixed-point: re-asks
    /// the agent after each pick (so the agent can stop at one). When
    /// no agent is registered the deterministic first-basic-card path
    /// is taken twice — mirrors
    /// <see cref="AssassinsTrophyFactory.TutorBasicLandUntapped"/>.
    /// </summary>
    private static void TutorUpToTwoBasicsToBattlefield(Player player)
    {
        var agent = AgentRegistry.Get(player);
        for (var i = 0; i < MaxLandsPerPlayer; i++)
        {
            var candidates = player.Zones.Library.GetCards()
                .Where(c => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name))
                .ToList();
            if (candidates.Count == 0) break;

            ICard? pick = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "basic land card")
                    .GetAwaiter().GetResult()
                : candidates[0];
            // CR 701.19a — agent declining (null) is a legal "may" no-op;
            // stop tutoring further lands for this player when declined.
            if (pick == null) return;

            player.Zones.Library.RemoveCard(pick);
            player.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            if (pick is Permanent perm)
            {
                perm.SetController(player);
                perm.MarkEnteredBattlefield();
            }
        }
    }

}
