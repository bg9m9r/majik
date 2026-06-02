using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Salt Marsh (Apocalypse "Tap Land" cycle). Oracle
/// text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {U} or {B}."
///
/// <para>
/// Mirrors <see cref="IzzetGuildgateFactory"/>: a plain Land shell with the
/// two mana abilities {U}/{B} (CR 605.1 — mana abilities don't use the stack)
/// is declared declaratively in
/// <c>Majik.Core/CardData/Cards/salt-marsh.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. Unlike the Guildgates, Salt Marsh
/// carries no land subtype (no Gate / basic-land type) — it is a bare Land.
/// There is no cycling clause.
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
[CardName("Salt Marsh")]
public static class SaltMarshFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("salt-marsh");

    /// <summary>Construct Salt Marsh owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Salt Marsh with optional replacement-bus wiring so
    /// the unconditional enters-tapped restriction (CR 614.1c) is registered
    /// against <paramref name="replacements"/>.</summary>
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
