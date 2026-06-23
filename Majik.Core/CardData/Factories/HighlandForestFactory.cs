using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Highland Forest (Kaldheim snow dual-tapland cycle,
/// red/green member). Type line: <c>Snow Land — Mountain Forest</c>. Oracle
/// text (verified against Scryfall 2026-06-23):
///   "({T}: Add {R} or {G}.)
///    This land enters tapped."
///
/// <para>
/// Same oracle shape as <see cref="WoodlandChasmFactory"/> — a nonbasic snow
/// tapped dual with the two printed basic land subtypes and two single-colour
/// mana abilities — only the produced colours (and subtypes) differ
/// (Mountain/Forest, {R}/{G} here). The parenthesised mana line is reminder
/// text; the two mana abilities {R}/{G} (CR 605.1 — mana abilities don't use
/// the stack), the Snow supertype (CR 205.4d), the printed Mountain/Forest
/// subtypes (CR 205.3i), and the Land shell are declared declaratively in
/// <c>Majik.Core/CardData/Cards/highland-forest.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. Highland Forest is the canonical
/// Kaldheim basic-subtype snow-dual that <see cref="ArcticFlatsFactory"/>'s
/// xmldoc contrasts against (the Coldsnap snow duals have NO basic subtypes).
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
/// <see cref="WoodlandChasmFactory"/>.
/// </para>
/// </summary>
[CardName("Highland Forest")]
public static class HighlandForestFactory
{
    public const string Slug = "highland-forest";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Highland Forest owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Highland Forest with optional replacement-bus wiring so
    /// the unconditional enters-tapped restriction (CR 614.1c) is registered
    /// against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as WoodlandChasmFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
