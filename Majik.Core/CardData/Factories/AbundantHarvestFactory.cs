using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Abundant Harvest (Modern Horizons 2, {G}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Choose land or nonland. Reveal cards from the top of your library
///    until you reveal a card of the chosen kind. Put that card into your
///    hand and the rest on the bottom of your library in a random order."
///
/// ## Why a named factory (no template covers it)
/// Abundant Harvest combines two distinct shapes the engine already
/// ships but no single template binds together:
/// <list type="bullet">
///   <item><b>Binary kind choice</b> — "Choose land or nonland" is a modal
///   pick (CR 700.2 is a different shape; this is a free choice made on
///   resolution, CR 608.2g). Resolved through
///   <see cref="IPlayerAgent.ChooseModeAsync"/> exactly like
///   <see cref="GlissaSunslayerFactory"/> / <see cref="TirelessProvisionerFactory"/>.</item>
///   <item><b>Reveal-until-condition + random bottom</b> — the reveal /
///   bottom loop mirrors <see cref="GoblinCharbelcherFactory.ResolveBelch"/>:
///   peel from the top of the library until the chosen kind is revealed
///   (CR 701.15 / clean stop on empty library, CR 701.15a), put that card
///   into hand, and bottom every other revealed card in a random order via
///   <see cref="GameRandomRegistry"/> + <see cref="GameRandom.Shuffle"/>
///   (CR 701.20).</item>
/// </list>
/// All primitives already exist — no new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {G}, green. Card shape comes from the embedded
///   JSON (<c>abundant-harvest.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - Resolve: pick land vs nonland (agent <see cref="IPlayerAgent.ChooseModeAsync"/>,
///   default = land when no agent / on error), then
///   <see cref="ResolveChoice"/> reveals-until-match, puts the match into
///   hand, and bottoms the rest in a seed-stable random order.
///
/// ## Rules citations
/// - CR 608.2g — "Choose land or nonland" is a free choice made as the
///   spell resolves.
/// - CR 701.15 — reveal cards from the top of the library.
/// - CR 701.20 — put cards on the bottom of the library in a random order.
/// - CR 305.1 / CR 110.1 — "land" card kind vs everything else ("nonland").
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: revealed cards aren't published on a
///   reveal bus yet (same gap as <see cref="GoblinCharbelcherFactory"/> and
///   every other reveal-until factory). No live observer cares yet.
/// </summary>
[CardName("Abundant Harvest")]
public static class AbundantHarvestFactory
{
    public const string CardName = "Abundant Harvest";
    public const string Slug = "abundant-harvest";
    public const string PrintedManaCost = "{G}";

    /// <summary>Mode index 0 — reveal until a <b>land</b> card.</summary>
    public const int ChooseLand = 0;

    /// <summary>Mode index 1 — reveal until a <b>nonland</b> card.</summary>
    public const int ChooseNonland = 1;

    /// <summary>
    /// The two kind choices, parallel to <see cref="ModeIntents"/>. Surfaced
    /// verbatim by remote-agent UIs.
    /// </summary>
    public static IReadOnlyList<string> Modes => new[] { "Land", "Nonland" };

    // Both arms tutor a card of the chosen kind into hand — card-advantage /
    // selection either way (the land arm also smooths mana).
    private static readonly IReadOnlyList<BotIntent> ModeIntents = new[]
    {
        BotIntent.Tutor, // Land — find a land (mana smoothing / ramp enabler).
        BotIntent.Tutor, // Nonland — dig to a spell.
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Abundant Harvest. No
    /// targets, no X; the land/nonland kind choice is made inside the resolve
    /// body (CR 608.2g) rather than declared as a stack-time mode, because the
    /// choice steers a reveal loop, not a branch of independent spell text.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Build the resolve effect: prompt the caster for land vs nonland
    /// (CR 608.2g), then reveal-until-match → hand, rest random-bottom.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: choose land or nonland, reveal until that kind, " +
                "put it into hand, bottom the rest in a random order.",
                async ctx =>
                {
                    var mode = await PickKindAsync(caster, ctx).ConfigureAwait(false);
                    ResolveChoice(caster, mode);
                }),
        };
    }

    /// <summary>
    /// Resolve the land/nonland kind choice. Consults the registered agent's
    /// <see cref="IPlayerAgent.ChooseModeAsync"/> when available; falls back
    /// to <see cref="ChooseLand"/> (the deterministic default) when no agent
    /// is registered or the agent throws — same posture as
    /// <see cref="GlissaSunslayerFactory"/>.
    /// </summary>
    private static async ValueTask<int> PickKindAsync(Player caster, ResolutionContext ctx)
    {
        var agent = ctx.Agent ?? AgentRegistry.Get(caster);
        if (agent == null) return ChooseLand;

        try
        {
            var pick = await agent.ChooseModeAsync(ctx.Game!, modes: Modes, modeIntents: ModeIntents)
                .ConfigureAwait(false);

            if (pick >= 0 && pick < Modes.Count) return pick;
        }
        catch
        {
            // Agent hard-requires a non-null context or throws — fall back to
            // the deterministic default (same posture as GlissaSunslayerFactory).
        }

        return ChooseLand;
    }

    /// <summary>
    /// Reveal cards from the top of <paramref name="caster"/>'s library until
    /// one matching the chosen kind (<paramref name="mode"/>:
    /// <see cref="ChooseLand"/> / <see cref="ChooseNonland"/>) is revealed, or
    /// the library runs dry (CR 701.15a clean stop). Put the matching card
    /// into hand and bottom every other revealed card in a random order
    /// (CR 701.20). Exposed for direct invocation by tests / bots without
    /// driving the full resolution pipeline.
    /// </summary>
    public static HarvestResolution ResolveChoice(Player caster, int mode)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var library = caster.Zones.Library;
        var revealed = new List<ICard>();
        ICard? matched = null;

        while (true)
        {
            var top = library.GetCards().FirstOrDefault();
            if (top == null) break; // CR 701.15a — empty library halts the reveal cleanly.

            library.RemoveCard(top);
            revealed.Add(top);

            // CR 305.1 — a land card is the "land" kind; everything else is
            // "nonland". The reveal terminates on the first card matching the
            // chosen kind.
            var isLand = top.HasType(CardType.Land);
            var matchesChosenKind = mode == ChooseLand ? isLand : !isLand;
            if (matchesChosenKind)
            {
                matched = top;
                break;
            }
        }

        // Move the matched card (if any) to hand.
        if (matched != null)
        {
            caster.Zones.Hand.AddCard(matched);
            matched.SetZone(ZoneType.Hand);
        }

        // CR 701.20 — bottom every OTHER revealed card in a random order. The
        // matched card already left for the hand; only the non-matches remain.
        var toBottom = revealed.Where(c => !ReferenceEquals(c, matched)).ToList();
        if (toBottom.Count > 0)
        {
            var random = GameRandomRegistry.Get(caster);
            random.Shuffle(toBottom);
            foreach (var card in toBottom)
            {
                library.AddCard(card); // Append == bottom.
                card.SetZone(ZoneType.Library);
            }
        }

        return new HarvestResolution(Revealed: revealed, PutInHand: matched);
    }

    /// <summary>
    /// Observation record for one Abundant Harvest resolution — every card
    /// revealed (in reveal order, including the terminating match) and the
    /// card put into hand (<see langword="null"/> when no card of the chosen
    /// kind was found before the library ran dry).
    /// </summary>
    public sealed record HarvestResolution(
        IReadOnlyList<ICard> Revealed,
        ICard? PutInHand);
}
