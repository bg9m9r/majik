using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crumbling Necropolis (Shards of Alara — the
/// "tapped tri-land" cycle, a.k.a. the Vivid-less Shard taplands). Grixis
/// (U/B/R) member — sibling of Arcane Sanctum, Jungle Shrine, Savage Lands,
/// and Seaside Citadel. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {U}, {B}, or {R}."
///
/// <para>
/// Unlike the Triome cycle (<see cref="SavaiTriomeFactory"/> et al.) this
/// land has <b>no basic land subtypes</b> and <b>no cycling</b> — the surface
/// is just an unconditional enters-tapped restriction (CR 614.1c) plus three
/// single-colour mana abilities {U}/{B}/{R} (CR 605.1 — mana abilities don't
/// use the stack). It is therefore the plain-tapland analogue of the
/// gain-land cycle (<see cref="BlossomingSandsFactory"/>) minus the ETB
/// life-gain trigger.
/// </para>
///
/// <para>
/// The full card surface — name, Land type, and the three mana abilities —
/// is declared declaratively in
/// <c>Majik.Core/CardData/Cards/crumbling-necropolis.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, mirroring the
/// JSON-driven posture of <see cref="BlossomingSandsFactory"/>.
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
[CardName("Crumbling Necropolis")]
public static class CrumblingNecropolisFactory
{
    public const string Slug = "crumbling-necropolis";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Crumbling Necropolis owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Crumbling Necropolis with optional replacement-bus
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
