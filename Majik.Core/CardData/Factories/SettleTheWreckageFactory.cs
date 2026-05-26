using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Settle the Wreckage (Ixalan, {2}{W}{W}).
///
/// Instant. Oracle text:
///   "Exile all attacking creatures target player controls. That player
///    may search their library for that many basic land cards, put them
///    onto the battlefield tapped, then shuffle."
///
/// ## Implementation
///
/// Card shape at the dispatcher; the exile + tutor body lives in
/// <see cref="BuildSpellDefinition"/>. Single 1..1 "target player"
/// request (CR 115.1c — "target player"). On resolution:
///
/// 1. Snapshot every attacking creature the targeted player controls
///    (CR 506.2 — attacking creatures are the creatures declared as
///    attackers this combat that haven't been removed from combat) via
///    the caller-supplied <c>attackerLookup</c>. Production callers feed
///    this from <see cref="Majik.Core.Combat.CombatManager.CurrentCombat"/>'s
///    <see cref="Majik.Core.Combat.Combat.Attackers"/> list filtered to
///    that player; tests inject the list directly.
/// 2. Exile each attacker via <see cref="OracleSpellBinder.MoveToExile"/>
///    (CR 701.18 — exile from battlefield). MoveToExile is a non-destroy
///    move, so indestructible / regeneration DO NOT gate exile (CR 702.12b).
/// 3. Offer the targeted player a basic-land tutor for N cards, where N
///    is the count of creatures exiled in step 2 (CR 701.19a — "may
///    search"). The agent picks up to N basics; declines are legal.
///    Picked basics go to the battlefield tapped (CR 305.6 — basic land
///    types) and the library is shuffled once at the end of the search
///    (CR 701.20a).
///
/// ## Why a named factory (over the existing template)
///
/// The existing search/land-to-battlefield templates only handle fixed
/// counts and unconditional searches; Settle the Wreckage gates the N
/// off a runtime count of attackers controlled by the target player.
/// That cross-reference (attacker zone → tutor count) is bespoke.
///
/// ## Indestructible / regeneration
///
/// Exile is not a destroy effect — CR 702.12b ("a permanent with
/// indestructible can't be destroyed") does not apply to exile. The
/// attackers go to exile regardless of indestructible / regeneration
/// shields. <see cref="OracleSpellBinder.MoveToExile"/> reflects this
/// posture (no <see cref="ZoneMoveReason"/> consultation).
///
/// CR rule references: 115.1c (target player), 305.6 (basic land types),
/// 506.2 (attacking creatures), 608.2b (illegal-target fizzle),
/// 701.18 (exile), 701.19a (library search), 701.20a (shuffle),
/// 702.12b (indestructible scope).
/// </summary>
[CardName("Settle the Wreckage")]
public static class SettleTheWreckageFactory
{
    public const string CardName = "Settle the Wreckage";
    public const string PrintedManaCost = "{2}{W}{W}";

    /// <summary>Basic land names per CR 305.6.</summary>
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    /// <summary>
    /// Build a Settle the Wreckage instant owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve body
    /// via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Settle the
    /// Wreckage is cast. Single 1..1 "target player" request; on
    /// resolution every attacking creature that player controls is
    /// exiled, and that player gets a basic-land tutor for that many
    /// cards.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="attackerLookup">Returns the attacking creatures the
    /// argument player controls right now. Production callers wire this
    /// from <see cref="Majik.Core.Combat.CombatManager.CurrentCombat"/>'s
    /// <see cref="Majik.Core.Combat.Combat.Attackers"/> filtered to the
    /// argument player's <see cref="Creature.Controller"/>. Tests inject
    /// the list directly. Returning null / empty means no attackers —
    /// the spell still resolves (CR 608.2 — partial resolution) but no
    /// tutor is offered (N = 0).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        Func<Player, IReadOnlyList<Creature>> attackerLookup)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(attackerLookup);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Settle the Wreckage: exile attackers + basic-land tutor", () =>
                    {
                        // CR 608.2b — illegal-target fizzle is handled by
                        // the cast flow; guard defensively.
                        if (raw is not Player victim) return;

                        // Snapshot the attackers controlled by the target
                        // player. Defensive filters: only creatures still
                        // on the battlefield and still controlled by the
                        // target player at resolution time (CR 506.4 — a
                        // creature can be removed from combat by various
                        // effects; we honour the live combat state).
                        var attackers = attackerLookup(victim)
                            ?.Where(c => c != null)
                            .Where(c => c.Zone == ZoneType.Battlefield)
                            .Where(c => ReferenceEquals(c.Controller, victim))
                            .ToList()
                            ?? new List<Creature>();

                        // CR 701.18 — exile each attacker. Non-destroy
                        // move; indestructible / regeneration do not gate.
                        foreach (var atk in attackers)
                        {
                            OracleSpellBinder.MoveToExile(atk);
                        }

                        // N = count of attackers actually exiled (CR
                        // 608.2c — effect resolves with the final values).
                        // Even N = 0 keeps the "may search" offer alive,
                        // but with nothing legal to find the tutor is a
                        // no-op — short-circuit to avoid agent prompts on
                        // empty searches.
                        var n = attackers.Count;
                        if (n <= 0) return;

                        TutorBasicLandsTapped(victim, n);
                    }),
                };
            });
    }

    /// <summary>
    /// Offer <paramref name="player"/>'s registered agent up to
    /// <paramref name="count"/> basic-land tutors. Picks via repeated
    /// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>; returning
    /// <c>null</c> declines the remainder (CR 701.19a — "may search").
    /// Mirrors <see cref="PathToExileFactory"/>'s tutor helper, lifted
    /// to multiple picks. Each picked basic goes to the battlefield
    /// tapped; the library is shuffled once at the end (CR 701.20a).
    /// </summary>
    private static void TutorBasicLandsTapped(Player player, int count)
    {
        if (count <= 0) return;

        var agent = AgentRegistry.Get(player);
        var picked = 0;

        for (var i = 0; i < count; i++)
        {
            var candidates = player.Zones.Library.GetCards()
                .Where(c => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name))
                .ToList();
            if (candidates.Count == 0) break;

            ICard? pick = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "basic land card")
                    .GetAwaiter().GetResult()
                : candidates[0];

            // CR 701.19a — decline is legal mid-search. Stop offering
            // further picks (consistent with how a player would handle
            // "search for up to N" once they decide to stop).
            if (pick == null) break;

            player.Zones.Library.RemoveCard(pick);
            player.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(player);
            if (pick is Permanent perm)
            {
                perm.Tap();
            }
            picked++;
        }

        // CR 701.20a — shuffle after the search resolves, regardless of
        // how many cards (if any) were found.
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "settle-the-wreckage");
    }
}
