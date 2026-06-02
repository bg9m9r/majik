using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Woodland Stream (common tapped-dual "Lake" cycle,
/// green/blue member). Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {G} or {U}."
///
/// <para>
/// Same oracle shape as <see cref="HighlandLakeFactory"/> — a plain tapped
/// dual with two single-colour mana abilities and no Gate subtype — only the
/// produced colours differ ({G}/{U} here). The two mana abilities {G}/{U}
/// (CR 605.1 — mana abilities don't use the stack) plus the Land shell are
/// declared declaratively in
/// <c>Majik.Core/CardData/Cards/woodland-stream.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>.
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
/// <see cref="HighlandLakeFactory"/>.
/// </para>
/// </summary>
[CardName("Woodland Stream")]
public static class WoodlandStreamFactory
{
    public const string Slug = "woodland-stream";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Woodland Stream owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Woodland Stream with optional replacement-bus wiring so
    /// the unconditional enters-tapped restriction (CR 614.1c) is registered
    /// against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as HighlandLakeFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
