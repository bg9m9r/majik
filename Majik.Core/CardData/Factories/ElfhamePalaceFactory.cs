using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Elfhame Palace (Invasion). Oracle text:
///   "This land enters tapped.
///    {T}: Add {G} or {W}."
///
/// <para>
/// Mirrors <see cref="SelesnyaGuildgateFactory"/> — the same G/W tapland —
/// but Elfhame Palace carries no land subtype (it predates the Gate cycle).
/// The Land shell plus the two mana abilities {G}/{W} (CR 605.1 — mana
/// abilities don't use the stack) are declared declaratively in
/// <c>Majik.Core/CardData/Cards/elfhame-palace.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed
/// oracle text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the
/// two-arg <see cref="Create(Player, ReplacementBus?)"/> path also registers
/// an <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>, mirroring <see cref="SelesnyaGuildgateFactory"/>.
/// The shape-only single-arg path skips the registration (no bus available).
/// </para>
/// </summary>
[CardName("Elfhame Palace")]
public static class ElfhamePalaceFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("elfhame-palace");

    /// <summary>Construct Elfhame Palace owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against).</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Elfhame Palace with optional replacement-bus wiring
    /// so the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as SelesnyaGuildgateFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
