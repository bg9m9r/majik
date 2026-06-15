using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tranquil Expanse (Modern Horizons "Karoo"/plain
/// enters-tapped dual). Green/white member. Oracle text (verified against
/// Scryfall):
///   "This land enters tapped.
///    {T}: Add {G} or {W}."
///
/// <para>
/// Same oracle shape as the Guildgate cycle minus the printed land subtype:
/// an unconditional enters-tapped restriction (CR 614.1c) plus two
/// single-colour mana abilities {G}/{W} (CR 605.1 — mana abilities don't use
/// the stack). Tranquil Expanse is a nonbasic, non-typed land (no printed land
/// subtype), so the def carries only the two mana abilities — the same posture
/// as <see cref="AncientAmphitheaterFactory"/>, but without the conditional
/// tribal-reveal clause.
/// </para>
///
/// <para>
/// The full card surface — name, Land type, and the two mana abilities — is
/// declared declaratively in
/// <c>Majik.Core/CardData/Cards/tranquil-expanse.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture of
/// <see cref="BlossomingSandsFactory"/>.
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
/// <see cref="BlossomingSandsFactory"/>.
/// </para>
/// </summary>
[CardName("Tranquil Expanse")]
public static class TranquilExpanseFactory
{
    public const string Slug = "tranquil-expanse";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Tranquil Expanse owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Tranquil Expanse with optional replacement-bus
    /// wiring so the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as BlossomingSandsFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
