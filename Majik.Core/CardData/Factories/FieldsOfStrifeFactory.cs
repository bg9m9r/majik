using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fields of Strife (Tempest). Oracle text
/// (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {R} or {W}.
///    {2}{R}{W}, {T}: Surveil 1."
///
/// Scryfall type line: Land (no basic supertype, no subtypes). Colourless
/// (no mana cost, no colour indicator).
///
/// <para>
/// The full card surface — name, Land type, the two single-colour mana
/// abilities {R}/{W} (CR 605.1 — mana abilities don't use the stack), and the
/// <b>{2}{R}{W}, {T}: Surveil 1</b> activated ability — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/fields-of-strife.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>. The activated
/// ability's cost stack is a ManaCostCost({2}{R}{W}) + a tap-self additional
/// cost, resolving the standard <c>surveil_self</c> effect (CR 701.42): when an
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> is registered the
/// controller decides which peeked cards go to the graveyard; otherwise the
/// pre-agent default sends all peeked cards to the graveyard. Same JSON-driven
/// posture as <see cref="CastleVantressFactory"/> (activated peek ability) and
/// <see cref="TranquilCoveFactory"/> (unconditional enters-tapped wiring).
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
/// <see cref="TranquilCoveFactory"/>.
/// </para>
/// </summary>
[CardName("Fields of Strife")]
public static class FieldsOfStrifeFactory
{
    public const string Slug = "fields-of-strife";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Fields of Strife owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Fields of Strife with optional replacement-bus wiring
    /// so the unconditional enters-tapped restriction (CR 614.1c) is registered
    /// against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {R}/{W} mana abilities and the {2}{R}{W},{T}:
        // Surveil 1 activated ability all come from JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as TranquilCoveFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
