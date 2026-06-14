using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Highland Weald (Modern Horizons 3 snow dual land,
/// red/green member). Oracle text (verified against Scryfall 2026-06-14):
///   "This land enters tapped.
///    {T}: Add {R} or {G}."
/// Type line: <c>Snow Land</c> — a plain Snow land with NO land subtypes
/// (no Mountain/Forest), unlike <see cref="HighlandForestFactory"/>.
///
/// <para>
/// The R/G member of the "Weald" snow tapland cycle. The Snow supertype
/// (CR 205.4d) plus the two single-colour mana abilities {R}/{G}
/// (CR 605.1 — mana abilities don't use the stack) are declared declaratively
/// in <c>Majik.Core/CardData/Cards/highland-weald.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture
/// of <see cref="CinderBarrensFactory"/> / <see cref="HighlandLakeFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed
/// oracle text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the
/// two-arg <see cref="Create(Player, ReplacementBus?)"/> path also registers
/// an <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>. The shape-only single-arg path skips the
/// registration (no bus available) — same posture as
/// <see cref="CinderBarrensFactory"/>.
/// </para>
/// </summary>
[CardName("Highland Weald")]
public static class HighlandWealdFactory
{
    public const string Slug = "highland-weald";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Highland Weald owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Highland Weald with optional replacement-bus wiring
    /// so the unconditional enters-tapped restriction (CR 614.1c) is registered
    /// against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as CinderBarrensFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
