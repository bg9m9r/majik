using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arctic Flats (Coldsnap "snow" enters-tapped dual
/// land). Oracle text (verified against Scryfall 2026-06-14):
///   "This land enters tapped.
///    {T}: Add {G} or {W}."
/// Type line: "Snow Land".
///
/// <para>
/// The green/white member of the Coldsnap snow-tapland cycle — same oracle
/// shape as <see cref="CinderBarrensFactory"/> (the "plain" {T}: Add A or B
/// enters-tapped dual) but carries the <see cref="CardSupertype.Snow"/>
/// supertype and, unlike the Kaldheim snow duals (e.g. Highland Forest), has
/// NO basic land subtypes — so its two single-colour mana abilities {G}/{W}
/// (CR 605.1 — mana abilities don't use the stack) must be declared
/// explicitly. The Land shell, the Snow supertype, and the two mana abilities
/// are declared declaratively in
/// <c>Majik.Core/CardData/Cards/arctic-flats.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring <see cref="CinderBarrensFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is an unconditional enters-tapped
/// replacement. On the production load path it is matched off the printed
/// oracle text by <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the
/// two-arg <see cref="Create(Player, ReplacementBus?)"/> path also registers
/// an <see cref="EntersTappedReplacement"/> directly on a supplied
/// <see cref="ReplacementBus"/>, mirroring <see cref="CinderBarrensFactory"/>.
/// The shape-only single-arg path skips the registration (no bus available).
/// </para>
/// </summary>
[CardName("Arctic Flats")]
public static class ArcticFlatsFactory
{
    public const string Slug = "arctic-flats";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Arctic Flats owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Arctic Flats with optional replacement-bus wiring so
    /// the unconditional enters-tapped restriction (CR 614.1c) is registered
    /// against <paramref name="replacements"/>.</summary>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as CinderBarrensFactory.
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
