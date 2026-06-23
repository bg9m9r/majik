using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Insomnia, Crown City (Edge of Eternities / "Town"
/// dual-tapland cycle). Oracle text:
///   "This land enters tapped.
///    {T}: Add {W} or {B}."
///
/// <para>
/// A W/B dual tapland on the "Town" land-subtype cycle — mechanically
/// identical to <see cref="BaronAirshipKingdomFactory"/> (same enters-tapped
/// posture, same printed <c>Town</c> subtype, CR 205.3m); only the produced
/// colours differ (W/B here vs U/R there). The Land shell — the <c>Town</c>
/// subtype plus the two mana abilities {W}/{B} (CR 605.1 — mana abilities
/// don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/insomnia-crown-city.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>. There is no cycling clause.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed
/// oracle text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the
/// two-arg <see cref="Create(Player, ReplacementBus?)"/> path also registers
/// an <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>, mirroring <see cref="BaronAirshipKingdomFactory"/>.
/// The shape-only single-arg path skips the registration (no bus available).
/// </para>
/// </summary>
[CardName("Insomnia, Crown City")]
public static class InsomniaCrownCityFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("insomnia-crown-city");

    /// <summary>Construct Insomnia, Crown City owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Insomnia, Crown City with optional replacement-bus
    /// wiring so the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as BaronAirshipKingdomFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
