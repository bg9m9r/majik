using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Baron, Airship Kingdom (Final Fantasy). Oracle text:
///   "This land enters tapped.
///    {T}: Add {U} or {R}."
///
/// <para>
/// A U/R dual tapland sharing the Guildgate shape (mechanically identical to
/// <see cref="IzzetGuildgateFactory"/> — same colours, same enters-tapped
/// posture; only the printed land subtype differs: <c>Town</c> here vs
/// <c>Gate</c> there, CR 205.3m). The Land shell — the <c>Town</c> subtype
/// plus the two mana abilities {U}/{R} (CR 605.1 — mana abilities don't use
/// the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/baron-airship-kingdom.json</c> and
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
/// <see cref="ReplacementBus"/>, mirroring <see cref="TrenoDarkCityFactory"/>.
/// The shape-only single-arg path skips the registration (no bus available).
/// </para>
/// </summary>
[CardName("Baron, Airship Kingdom")]
public static class BaronAirshipKingdomFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("baron-airship-kingdom");

    /// <summary>Construct Baron, Airship Kingdom owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Baron, Airship Kingdom with optional replacement-bus
    /// wiring so the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as TrenoDarkCityFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
