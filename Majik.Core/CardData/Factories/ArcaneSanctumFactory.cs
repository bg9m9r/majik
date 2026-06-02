using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arcane Sanctum (Shards of Alara — the Esper
/// tapped tri-land). Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {W}, {U}, or {B}."
///
/// <para>
/// Same oracle shape as the Triome cycle (<see cref="SavaiTriomeFactory"/>)
/// stripped of cycling and of the basic-land subtypes — a plain nonbasic
/// land with an unconditional enters-tapped restriction (CR 614.1c) and a
/// three-colour {T} mana ability. Modelled as three single-colour
/// <see cref="Majik.Core.Abilities.ManaAbility"/> instances {W}/{U}/{B}
/// (CR 605.1a — mana abilities don't use the stack), mirroring the W/U/B
/// posture of <see cref="BlossomingSandsFactory"/> minus the ETB life-gain.
/// </para>
///
/// <para>
/// The full card surface — name, Land type, and the three mana abilities —
/// is declared declaratively in
/// <c>Majik.Core/CardData/Cards/arcane-sanctum.json</c> and materialized via
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
[CardName("Arcane Sanctum")]
public static class ArcaneSanctumFactory
{
    public const string Slug = "arcane-sanctum";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Arcane Sanctum owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Arcane Sanctum with optional replacement-bus wiring
    /// so the unconditional enters-tapped restriction (CR 614.1c) is
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
