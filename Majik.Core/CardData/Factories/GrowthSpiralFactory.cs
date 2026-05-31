using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Growth Spiral (Ravnica Allegiance, {G}{U}).
///
/// Instant. Oracle text:
///   "Draw a card. You may put a land card from your hand onto the
///    battlefield."
///
/// ## Implemented (v1)
/// - <b>Instant</b> shape, mana cost {G}{U}. The card shape (name, type,
///   cost) is data-driven: loaded from
///   <c>Majik.Core/CardData/Cards/growth-spiral.json</c> via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built by
///   <see cref="CardDefinitionFactory"/> (mirrors <see cref="RoastFactory"/>
///   and <see cref="ExploreFactory"/>). The resolution body is supplied in
///   code via <see cref="BuildResolveEffect"/> because the JSON ability
///   schema does not yet model a spell's resolve effect (same posture as
///   Roast / Flame Slash).
/// - On resolution (<see cref="BuildResolveEffect"/>), in printed order:
///     1. <b>Draw a card</b> (CR 121.1) via <see cref="Fx.DrawCards"/> —
///        empty library stamps the <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///        loss flag (CR 704.5b) rather than throwing.
///     2. <b>You may put a land card from your hand onto the battlefield</b>
///        (CR 113.6c). The draw happens FIRST, so a land drawn by Growth
///        Spiral is itself a legal candidate. v1 prompts the controller's
///        agent (<see cref="IPlayerAgent.ChooseYesNoAsync"/> +
///        <see cref="IPlayerAgent.ChooseFromHandAsync"/>, intent
///        <see cref="BotIntent.Ramp"/>) when registered; the no-agent
///        fallback auto-accepts the "you may" and picks the first land in
///        hand deterministically (same posture as
///        <see cref="UroTitanFactory"/> / <see cref="SakuraTribeScoutFactory"/>).
///
/// ## Why "put a land onto the battlefield" is NOT a land drop
/// CR 305.9 / 113.6c — putting a land directly onto the battlefield via an
/// effect bypasses the per-turn land-drop cap (CR 305.2) and the
/// main-phase / empty-stack timing restriction entirely. This factory does
/// NOT touch <see cref="Majik.Core.Game.LandDropTracker"/> — Growth Spiral's
/// land enters regardless of how many lands the controller has already
/// played this turn. (Contrast <see cref="ExploreFactory"/>, whose printed
/// text is "play an additional land", which DOES bump the tracker.)
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt without an agent</b>: the no-agent fallback always
///   plays the first land in hand when one exists. A registered agent gets
///   the full opt-in + land-pick prompts. Same simplification every "may"
///   factory carries pre-prompt (Uro / Sakura-Tribe Scout / Stoneforge).
/// - <b>ETB-on-land routing</b>: when a <see cref="ZoneService"/> is supplied
///   the hand → battlefield move routes through
///   <see cref="ZoneService.MoveCard"/> so ETB triggers / replacements on
///   the played land fire (CR 603.6a, CR 614 — bounce-land ETB bounce,
///   Lotus Cobra landfall, etc.). The shape-only test path falls back to
///   <see cref="ZoneServiceRegistry"/> then raw zone manipulation.
/// </summary>
[CardName("Growth Spiral")]
public static class GrowthSpiralFactory
{
    public const string CardName = "Growth Spiral";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("growth-spiral");

    /// <summary>Construct Growth Spiral owned and controlled by
    /// <paramref name="owner"/>. Card shape only on the dispatcher path; the
    /// resolve body is supplied via <see cref="BuildResolveEffect"/> at
    /// resolution time.</summary>
    public static Instant Create(Player owner) =>
        (Instant)CardDefinitionFactory.Build(Definition, owner);

    /// <summary>
    /// Build Growth Spiral's resolution effect. In printed order: draw a
    /// card (CR 121.1), then optionally put a land card from hand onto the
    /// battlefield (CR 113.6c).
    ///
    /// Pass the live <paramref name="zoneService"/> so the land move routes
    /// through <see cref="ZoneService.MoveCard"/> and ETB triggers /
    /// replacements on the played land fire (CR 603.6a); null falls back to
    /// <see cref="ZoneServiceRegistry"/> then raw zone manipulation (the
    /// shape-only test path).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                "Growth Spiral: draw a card, then you may put a land from hand onto the battlefield.",
                async ctx =>
                {
                    // CR 121.1 — "Draw a card." Empty library stamps the
                    // CR 704.5b pending-loss flag via Fx.DrawCards' internal
                    // MarkTriedToDrawFromEmptyLibrary path (no throw).
                    Fx.DrawCards(caster, 1);

                    // CR 113.6c — "You may put a land card from your hand
                    // onto the battlefield." The draw above runs first, so a
                    // land just drawn is a legal candidate here.
                    await PutLandFromHandAsync(caster, zoneService, ctx).ConfigureAwait(false);
                }),
        };
    }

    /// <summary>
    /// CR 113.6c — optional "put a land card from your hand onto the
    /// battlefield". Candidate set = every land card in the controller's
    /// hand. Agent-driven opt-in + land-pick when an agent is registered
    /// (intent <see cref="BotIntent.Ramp"/>); no-agent fallback auto-accepts
    /// and takes the first land deterministically. No land in hand → clean
    /// no-op. Movement prefers <paramref name="zoneService"/> (then the
    /// registry, then raw zone manipulation) so ETB-on-land triggers fire
    /// (CR 603.6a).
    /// </summary>
    private static async ValueTask PutLandFromHandAsync(Player controller, ZoneService? zoneService, ResolutionContext ctx)
    {
        var candidates = controller.Zones.Hand.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();
        if (candidates.Count == 0) return; // No lands → "may" no-op.

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);

        ICard? land;
        if (agent != null)
        {
            // CR 117.1a — optional "you may" gesture, resolved by the agent.
            var optIn = await agent.ChooseYesNoAsync(
                    "Put a land card from your hand onto the battlefield?",
                    BotIntent.Ramp)
                .ConfigureAwait(false);
            if (!optIn) return;

            land = await agent.ChooseFromHandAsync(controller, candidates, BotIntent.Ramp)
                .ConfigureAwait(false);
            // CR 608.2b — re-validate the agent's pick at resolution.
            if (land == null || !candidates.Contains(land)) return;
        }
        else
        {
            // No-agent fallback: auto-accept + first land (v1 posture shared
            // with Uro / Sakura-Tribe Scout / Stoneforge Mystic).
            land = candidates[0];
        }

        // CR 603.6a — prefer ZoneService.MoveCard so ETB triggers /
        // replacements on the played land fire (bounce-land ETB bounce,
        // Lotus Cobra landfall, Amulet of Vigor untap). Fall back to the
        // registry, then raw zone manipulation for the shape/test path.
        var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(controller);
        if (effectiveZones != null)
        {
            effectiveZones.MoveCard(land, ZoneType.Hand, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Hand.RemoveCard(land);
            controller.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
            land.SetController(controller);
        }
    }
}
