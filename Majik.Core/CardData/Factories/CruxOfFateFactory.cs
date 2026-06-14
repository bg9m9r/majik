using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crux of Fate (Fate Reforged, {3}{B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Choose one —
///    • Destroy all Dragon creatures.
///    • Destroy all non-Dragon creatures."
///
/// ## Why a named factory
/// Crux of Fate is a modal mass-destroy spell (CR 700.2 — "choose one")
/// whose two modes are board sweeps partitioned by the Dragon creature
/// subtype (CR 205.3 / 702 — Dragon is a creature type). The engine
/// already ships both primitives:
/// <list type="bullet">
///   <item><b>Modal choice</b> — the "choose one" pick is resolved through
///   <see cref="IPlayerAgent.ChooseModeAsync"/>, exactly the shape used by
///   <see cref="AbundantHarvestFactory"/>.</item>
///   <item><b>Symmetric mass destroy</b> — every creature matching the
///   chosen partition is routed to its owner's graveyard via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7), the same
///   board-wipe posture as <see cref="WrathOfGodFactory"/> /
///   <see cref="PathOfPerilFactory"/>.</item>
/// </list>
/// No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {3}{B}{B}, black. Card shape comes from the
///   embedded JSON (<c>crux-of-fate.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - Resolve: pick mode (agent <see cref="IPlayerAgent.ChooseModeAsync"/>,
///   default = "Destroy all Dragon creatures" when no agent / on error),
///   then <see cref="ResolveChoice"/> sweeps the matching partition on
///   every supplied player's battlefield (CR 109.5 — symmetric; no
///   controller restriction).
///
/// ## Rules citations
/// - CR 700.2 — "choose one" modal spell; the mode is chosen as the spell
///   is cast / put on the stack.
/// - CR 205.3 / CR 702 — Dragon is a creature subtype; "non-Dragon" is
///   every creature lacking it.
/// - CR 701.7 — destroy → owner's graveyard. Plain Destroy: Indestructible
///   (CR 702.12) cancels and active regeneration shields (CR 701.15) are
///   consumed normally — the printed text carries no "can't be regenerated"
///   rider.
/// </summary>
[CardName("Crux of Fate")]
public static class CruxOfFateFactory
{
    public const string CardName = "Crux of Fate";
    public const string Slug = "crux-of-fate";
    public const string PrintedManaCost = "{3}{B}{B}";

    /// <summary>Mode index 0 — destroy all <b>Dragon</b> creatures.</summary>
    public const int DestroyDragons = 0;

    /// <summary>Mode index 1 — destroy all <b>non-Dragon</b> creatures.</summary>
    public const int DestroyNonDragons = 1;

    /// <summary>
    /// The two printed modes, in oracle order. Surfaced verbatim by
    /// remote-agent UIs / <see cref="IPlayerAgent.ChooseModeAsync"/>.
    /// </summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Destroy all Dragon creatures.",
        "Destroy all non-Dragon creatures.",
    };

    // Both arms are board wipes — removal either way. (Which one is better
    // is a board-state call the bot's evaluator makes; the intent tag is
    // Removal for both.)
    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Removal, // Dragons.
        BotIntent.Removal, // Non-Dragons.
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Crux of Fate. No targets,
    /// no X; the "choose one" mode steers a symmetric board sweep, so the
    /// choice is made inside the resolve body (CR 700.2 — the chosen mode
    /// determines which partition is destroyed).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(IReadOnlyList<Player> allPlayers, Player caster)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(allPlayers, caster));
    }

    /// <summary>
    /// Build the resolve effect: prompt the caster for the mode (CR 700.2),
    /// then sweep the matching partition across every supplied player's
    /// battlefield.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers, Player caster)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: choose one — destroy all Dragon creatures, or " +
                "destroy all non-Dragon creatures.",
                async ctx =>
                {
                    var mode = await PickModeAsync(caster, ctx).ConfigureAwait(false);
                    ResolveChoice(allPlayers, mode);
                }),
        };
    }

    /// <summary>
    /// Resolve the "choose one" mode. Consults the registered agent's
    /// <see cref="IPlayerAgent.ChooseModeAsync"/> when available; falls back
    /// to <see cref="DestroyDragons"/> (the deterministic default) when no
    /// agent is registered or the agent throws — same posture as
    /// <see cref="AbundantHarvestFactory"/>.
    /// </summary>
    private static async ValueTask<int> PickModeAsync(Player caster, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(caster);
        if (agent == null) return DestroyDragons;

        try
        {
            var pick = await agent.ChooseModeAsync(ctx.Game!, modes: Modes, modeIntents: ModeIntents)
                .ConfigureAwait(false);

            if (pick >= 0 && pick < Modes.Count) return pick;
        }
        catch
        {
            // Agent hard-requires a non-null context or throws — fall back to
            // the deterministic default (same posture as AbundantHarvestFactory).
        }

        return DestroyDragons;
    }

    /// <summary>
    /// Sweep the chosen partition on every supplied player's battlefield:
    /// <list type="bullet">
    ///   <item><see cref="DestroyDragons"/> → destroy every creature with the
    ///     Dragon subtype (CR 702).</item>
    ///   <item><see cref="DestroyNonDragons"/> → destroy every creature
    ///     LACKING the Dragon subtype.</item>
    /// </list>
    /// Symmetric (CR 109.5 — no controller restriction). Each matching
    /// creature is routed to its owner's graveyard via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7 — plain
    /// Destroy; Indestructible CR 702.12 cancels and regeneration shields
    /// CR 701.15 are consumed normally). Exposed for direct invocation by
    /// tests / bots without driving the full resolution pipeline.
    /// </summary>
    public static void ResolveChoice(IReadOnlyList<Player> allPlayers, int mode)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        var dragonsOnly = mode == DestroyDragons;

        foreach (var pl in allPlayers)
        {
            if (pl == null) continue;

            // Snapshot — MoveToGraveyard mutates the source battlefield in
            // place.
            var creatures = pl.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .Where(c => c.HasSubtype(CardSubtype.Dragon) == dragonsOnly)
                .ToList();

            foreach (var c in creatures)
            {
                // CR 701.7 — destroy. No "can't be regenerated" rider, so the
                // default Destroy reason: indestructible (CR 702.12) and
                // regeneration shields (CR 701.15) gate normally at the binder.
                OracleSpellBinder.MoveToGraveyard(
                    c, Majik.Core.Zones.ZoneMoveReason.Destroy);
            }
        }
    }
}
