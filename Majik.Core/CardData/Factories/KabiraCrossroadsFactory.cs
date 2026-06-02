using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kabira Crossroads (Zendikar mono-white enters-tapped
/// gain-life land). Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 2 life.
///    {T}: Add {W}."
///
/// <para>
/// Standard gain-land shape (cf. <see cref="AkoumRefugeFactory"/>): an
/// unconditional enters-tapped restriction (CR 614.1c), an ETB self-trigger
/// that gains the controller 2 life (CR 119.3), and a single-colour {W} mana
/// ability (CR 605.1 — mana abilities don't use the stack). Differs from the
/// B/R Refuge only in colour ({W} vs {B}/{R}) and the life amount (2 vs 1).
/// </para>
///
/// <para>
/// The full card surface — name, Land type, the {W} mana ability, and the
/// "When this land enters, you gain 2 life" triggered ability — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/kabira-crossroads.json</c>
/// and materialized via <see cref="CardDefinitionFactory"/>.
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
/// <see cref="AkoumRefugeFactory"/>.
/// </para>
/// </summary>
[CardName("Kabira Crossroads")]
public static class KabiraCrossroadsFactory
{
    public const string Slug = "kabira-crossroads";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Kabira Crossroads owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Kabira Crossroads with optional replacement-bus
    /// wiring so the unconditional enters-tapped restriction (CR 614.1c) is
    /// registered against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as AkoumRefugeFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
