using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Explore (Worldwake / Modern Horizons 2, {1}{G}).
///
/// Sorcery. Oracle text:
///   "You may play an additional land this turn.
///    Draw a card."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{G}.
/// - On-resolve effect via <see cref="BuildResolveEffect"/>:
///     1. Bump the controller's land-drop max for this turn by one via
///        <see cref="LandDropTracker.SetMaxLandDropsThisTurn"/>
///        (CR 305.2 — "extra land per turn" effects increase the cap;
///        the cap is reset on turn change so the bump expires naturally
///        without an end-of-turn cleanup hook).
///     2. Controller draws one card via <see cref="Fx.DrawCards"/>
///        (CR 121.1 — empty library stamps the
///        <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> loss
///        flag via Fx.DrawCards' internal CR 704.5b path).
///
/// ## Audit: <see cref="LandDropTracker"/>'s "add" surface
/// As of PR #447 (LandDropTracker introduction) and PR #529 (bounce
/// land cycle), <see cref="LandDropTracker"/> exposes only
/// <see cref="LandDropTracker.SetMaxLandDropsThisTurn"/> (absolute
/// setter) and <see cref="LandDropTracker.MaxLandDropsThisTurn"/>
/// (current cap reader). There is NO additive <c>AddExtraLandDrop</c>
/// or <c>BumpMaxLandDropsThisTurn</c> method. Explore (and any other
/// "you may play an additional land this turn" effect — Azusa, Lost
/// but Seeking, Exploration, Oracle of Mul Daya, etc.) reads the
/// current max and writes <c>current + 1</c> as the new max. This is
/// stack-safe (the bump is applied at spell resolution, not on cast)
/// and turn-safe (<see cref="LandDropTracker.ResetTurn"/> clears the
/// per-turn max on every turn change so multi-turn carry-over isn't
/// possible). The "you may" clause is auto-accepted at v1 — same
/// posture as Sneak Attack / Through the Breach's "may" gestures.
///
/// ## Why a named factory (not template broaden)
/// The "extra land drop + cantrip" composite isn't covered by any
/// existing spell template (the templates handle either pure-cantrip
/// (Opt, Preordain, Ponder) or pure-resource (Mox Diamond's land
/// drop, Lotus Field) clauses, not the join). Explore's resolve effect
/// is two independent statements with no shared state, so a named
/// factory keeps both reads (cap bump + draw) local to the card. When
/// Azusa / Exploration ship they'll likely share helpers but stay
/// per-card for the same reason Sakura-Tribe Scout / Uro keep their
/// per-card "play extra land" wiring.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven "may" prompt</b>: the v1 resolve always applies
///   the land-drop bump. A first-class
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseYesNoAsync"/>
///   prompt against <see cref="Majik.Core.Cards.BotIntent.Ramp"/> would
///   model the rare case where a controller intentionally declines
///   (effectively never happens in paper — Explore is always a Ramp
///   spell — but the optional gesture is printed). Same simplification
///   every "may" factory carries pre-prompt.
/// - <b>Multi-Explore stacking</b>: two Explores cast in the same turn
///   each read the current max + 1 — they stack additively because
///   the second reads the post-first cap. Verified in
///   <c>ExploreFactoryTests.Explore_TwoCopiesStackAdditively</c>.
/// </summary>
[CardName("Explore")]
public static class ExploreFactory
{
    public const string CardName = "Explore";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>
    /// Construct Explore owned and controlled by <paramref name="owner"/>.
    /// Card shape only on the dispatcher path; wire the resolve effect
    /// via <see cref="BuildResolveEffect"/> at cast / resolution time
    /// once the live <see cref="LandDropTracker"/> is available.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Explore's resolution effect. Pass the live
    /// <paramref name="landDropTracker"/> so the extra-land bump
    /// applies (CR 305.2); null skips the bump (shape-only path, e.g.
    /// when invoking the resolve in a unit test that doesn't exercise
    /// the land-drop ledger). The draw effect always runs.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        LandDropTracker? landDropTracker)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                "Explore: bump controller's max land drops this turn by 1, then draw a card.",
                () =>
                {
                    // CR 305.2 — bump the per-turn land-drop cap by 1.
                    // ResetTurn (CR 500.1 on turn change) drops the bump
                    // so multi-turn carry-over isn't possible.
                    // Null tracker = shape-only test path; skip bump.
                    if (landDropTracker != null)
                    {
                        var current = landDropTracker.MaxLandDropsThisTurn(caster);
                        landDropTracker.SetMaxLandDropsThisTurn(caster, current + 1);
                    }

                    // CR 121.1 — "Draw a card." Empty library stamps the
                    // CR 704.5b loss flag via Fx.DrawCards' internal
                    // MarkTriedToDrawFromEmptyLibrary path.
                    Fx.DrawCards(caster, 1);
                }),
        };
    }
}
