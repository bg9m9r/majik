using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Assassin's Trophy (Guilds of Ravnica, {B}{G}).
///
/// Instant. Oracle text:
///   "Destroy target permanent an opponent controls. Its controller
///    searches their library for a basic land card, puts it onto the
///    battlefield, then shuffles."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}{G}, owner / controller.
/// - <b>Destroy target permanent an opponent controls</b> — a single
///   1..1 "target permanent an opponent controls"
///   <see cref="TargetRequest"/> (CR 115.1). v1 sets
///   <c>LegalCandidates</c> to empty (no choose-time filter); the
///   "opponent controls" constraint is enforced at resolve time
///   (CR 608.2b — if the target's controller is the same as the caster
///   at resolution, the spell does nothing).
/// - <b>Destroyed permanent's controller searches for a basic land</b> —
///   the controller at the moment of resolution (CR 608.2b
///   last-known-information) is offered the basic-land tutor via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>. The land enters
///   the battlefield untapped (no tapped qualifier in the oracle text).
///   v1: deterministic first basic land found when no agent is registered.
///   Shuffle deferred — same MVP gap as every other tutor
///   (<see cref="PathToExileFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Indestructible</b>: the destroy call moves the permanent to the
///   graveyard without checking for Indestructible (same gap as every
///   other single-target destroy template — Terminate, Abrupt Decay,
///   Slaughter Pact).
/// - <b>Library shuffle</b>: no IZone.Shuffle entry point yet (same
///   rationale as SearchSpellFactory / PathToExileFactory).
/// - <b>Resolve-time opponent check</b>: if the target's controller has
///   changed (e.g. control-effect) to the caster by the time the spell
///   resolves, the spell does nothing (CR 608.2b target legality).
/// </summary>
[CardName("Assassin's Trophy")]
public static class AssassinsTrophyFactory
{
    public const string CardName = "Assassin's Trophy";
    public const string PrintedManaCost = "{B}{G}";

    /// <summary>Basic land names per CR 305.6.</summary>
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    /// <summary>
    /// Construct the Assassin's Trophy card shape (Instant, {B}{G}).
    /// Resolve behaviour is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Assassin's Trophy
    /// is cast. Single 1..1 "target permanent an opponent controls" request;
    /// on resolution:
    /// <list type="number">
    ///   <item>Confirms the target is still on the battlefield and is
    ///     controlled by an opponent of <paramref name="caster"/>
    ///     (CR 608.2b target-legality / opponent check).</item>
    ///   <item>Destroys the target via
    ///     <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7).</item>
    ///   <item>Offers the destroyed permanent's controller a basic-land
    ///     tutor: search for a basic land card and put it onto the
    ///     battlefield untapped (CR 701.19a).</item>
    /// </list>
    /// </summary>
    /// <param name="caster">The player casting Assassin's Trophy. Used at
    /// resolve time to enforce the "opponent controls" constraint
    /// (CR 608.2b).</param>
    /// <param name="resolver">Resolves the raw target token to a live
    /// engine object (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target permanent an opponent controls",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target permanent + basic-land tutor",
                        () =>
                        {
                            if (raw is not Permanent target) return;

                            // CR 608.2b — resolution-time legality check.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Opponent-controls check: if the target's
                            // controller is the same player as the caster
                            // at resolution, the spell does nothing
                            // (CR 608.2b target-legality / last-known info).
                            var targetController = target.Controller ?? target.Owner;
                            if (targetController == caster) return;

                            // CR 701.7 — Destroy. Indestructible rider deferred
                            // (same gap as Terminate / Abrupt Decay / Slaughter Pact).
                            OracleSpellBinder.MoveToGraveyard(target);

                            // Basic-land tutor rider — "its controller searches
                            // their library for a basic land card, puts it onto
                            // the battlefield" (CR 701.19a). The land enters
                            // untapped — no "tapped" qualifier in the oracle text.
                            if (targetController == null) return;
                            TutorBasicLandUntapped(targetController);
                        }),
                };
            });
    }

    /// <summary>
    /// Offer <paramref name="player"/>'s registered agent the basic-land
    /// tutor. Picks via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>;
    /// returning <c>null</c> declines (CR 701.19a — the search effect
    /// consumes no card if the player can't or doesn't find one). When no
    /// agent is registered, falls back to first basic candidate
    /// (deterministic test default — mirrors PathToExileFactory).
    /// </summary>
    private static void TutorBasicLandUntapped(Player player)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name))
            .ToList();
        if (candidates.Count == 0) return;

        var agent = AgentRegistry.Get(player);
        ICard? pick = agent != null
            ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "basic land card")
                .GetAwaiter().GetResult()
            : candidates[0];
        if (pick == null) return; // CR 701.19a — finding nothing is legal.

        player.Zones.Library.RemoveCard(pick);
        player.Zones.Battlefield.AddCard(pick);
        pick.SetZone(ZoneType.Battlefield);
        pick.SetController(player);
        // Oracle text: "puts it onto the battlefield" — no "tapped" qualifier.
        // CR 701.19c — shuffle after a search effect. Skipped for MVP
        // (no IZone.Shuffle entry point yet; same rationale as
        // PathToExileFactory / SearchSpellFactory).
    }
}
