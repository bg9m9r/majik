using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sharlayan, Nation of Scholars (Final Fantasy). Oracle text:
///   "This land enters tapped.
///    {T}: Add {W} or {U}."
///
/// <para>
/// A W/U dual tapland sharing the Town shape (mechanically identical to
/// <see cref="BaronAirshipKingdomFactory"/> — same enters-tapped posture and
/// printed <c>Town</c> subtype; only the produced colours differ: {W}/{U}
/// here vs {U}/{R} there). The Land shell — the <c>Town</c> subtype
/// (CR 205.3m) plus the two mana abilities {W}/{U} (CR 605.1 — mana abilities
/// don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/sharlayan-nation-of-scholars.json</c> and
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
/// <see cref="ReplacementBus"/>, mirroring <see cref="BaronAirshipKingdomFactory"/>.
/// The shape-only single-arg path skips the registration (no bus available).
/// </para>
/// </summary>
[CardName("Sharlayan, Nation of Scholars")]
public static class SharlayanNationOfScholarsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sharlayan-nation-of-scholars");

    /// <summary>Construct Sharlayan, Nation of Scholars owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Sharlayan, Nation of Scholars with optional
    /// replacement-bus wiring so the unconditional enters-tapped restriction
    /// (CR 614.1c) is registered against <paramref name="replacements"/>.</summary>
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
