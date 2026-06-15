using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vector, Imperial Capital (Edge of Eternities). Oracle
/// text:
///   "This land enters tapped.
///    {T}: Add {B} or {R}."
///
/// <para>
/// Mechanically identical to <see cref="RakdosGuildgateFactory"/> — an
/// enters-tapped {B}/{R} dual tapland — differing only in its printed land
/// subtype: <c>Town</c> (CR 205.3i) rather than <c>Gate</c>. The Land shell —
/// the <c>Town</c> subtype plus the two mana abilities {B}/{R} (CR 605.1 —
/// mana abilities don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/vector-imperial-capital.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>. There is no cycling
/// clause.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed
/// oracle text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the
/// two-arg <see cref="Create(Player, ReplacementBus?)"/> path also registers
/// an <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>, mirroring <see cref="RakdosGuildgateFactory"/>.
/// The shape-only single-arg path skips the registration (no bus available).
/// </para>
/// </summary>
[CardName("Vector, Imperial Capital")]
public static class VectorImperialCapitalFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("vector-imperial-capital");

    /// <summary>Construct Vector, Imperial Capital owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Vector, Imperial Capital with optional replacement-bus
    /// wiring so the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as RakdosGuildgateFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
