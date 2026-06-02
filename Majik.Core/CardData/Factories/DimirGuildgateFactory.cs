using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dimir Guildgate (Return to Ravnica / Guilds of
/// Ravnica gate cycle). Oracle text:
///   "This land enters tapped.
///    {T}: Add {U} or {B}."
///
/// <para>
/// Mirrors <see cref="IzzetGuildgateFactory"/> exactly (identical Gate
/// enters-tapped dual tapland posture, only the produced colours differ —
/// {U}/{B} here vs {U}/{R} there): the Land shell — the <c>Gate</c> subtype
/// (CR 205.3m / CR 702 gate cards carry the Gate land subtype) plus the two
/// mana abilities {U}/{B} (CR 605.1 — mana abilities don't use the stack) —
/// is declared declaratively in
/// <c>Majik.Core/CardData/Cards/dimir-guildgate.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. There is no cycling clause.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed
/// oracle text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the
/// two-arg <see cref="Create(Player, ReplacementBus?)"/> path also registers
/// an <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>, mirroring <see cref="IzzetGuildgateFactory"/>.
/// The shape-only single-arg path skips the registration (no bus available).
/// </para>
/// </summary>
[CardName("Dimir Guildgate")]
public static class DimirGuildgateFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("dimir-guildgate");

    /// <summary>Construct Dimir Guildgate owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Dimir Guildgate with optional replacement-bus
    /// wiring so the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as IzzetGuildgateFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
