using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodfell Caves (Khans of Tarkir "life-gain dual
/// land", a.k.a. the gain-land cycle). Black/red member — sibling of the
/// white/blue Tranquil Cove. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {B} or {R}."
///
/// <para>
/// Same oracle shape as the rest of the gain-land cycle: an unconditional
/// enters-tapped restriction (CR 614.1c), an ETB self-trigger that gains the
/// controller 1 life (CR 119.3), and two single-colour mana abilities
/// {B}/{R} (CR 605.1 — mana abilities don't use the stack).
/// </para>
///
/// <para>
/// The full card surface — name, Land type, the two mana abilities, and the
/// "When this land enters, you gain 1 life" triggered ability — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/bloodfell-caves.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, mirroring the
/// JSON-driven posture of <see cref="IzzetGuildgateFactory"/>.
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
/// <see cref="IzzetGuildgateFactory"/>.
/// </para>
/// </summary>
[CardName("Bloodfell Caves")]
public static class BloodfellCavesFactory
{
    public const string Slug = "bloodfell-caves";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Bloodfell Caves owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Bloodfell Caves with optional replacement-bus
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
