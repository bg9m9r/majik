using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cinder Barrens (Commander 2015 / Magic Origins
/// "plain" enters-tapped dual land). Oracle text (verified against Scryfall
/// 2026-06-02):
///   "This land enters tapped.
///    {T}: Add {B} or {R}."
///
/// <para>
/// The black/red member of the "plain" tapland cycle — same oracle shape as
/// the Ravnica guildgates (e.g. <see cref="IzzetGuildgateFactory"/>) but
/// WITHOUT the Gate land subtype and WITHOUT any rider (no life gain, no
/// scry, no cycling). The Land shell plus the two single-colour mana
/// abilities {B}/{R} (CR 605.1 — mana abilities don't use the stack) is
/// declared declaratively in
/// <c>Majik.Core/CardData/Cards/cinder-barrens.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture of
/// <see cref="IzzetGuildgateFactory"/>.
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
[CardName("Cinder Barrens")]
public static class CinderBarrensFactory
{
    public const string Slug = "cinder-barrens";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Cinder Barrens owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Cinder Barrens with optional replacement-bus
    /// wiring so the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

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
