using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boreal Shelf (Coldsnap — the white/blue "plain"
/// snow enters-tapped land). Type line: <c>Snow Land</c>. Oracle text
/// (verified against Scryfall 2026-06-14):
///   "This land enters tapped.
///    {T}: Add {W} or {U}."
///
/// <para>
/// Same oracle shape as <see cref="CinderBarrensFactory"/> — two
/// single-colour mana abilities {W}/{U} (CR 605.1 — mana abilities don't use
/// the stack) plus an unconditional enters-tapped restriction (CR 614.1c) —
/// but it carries the <c>Snow</c> supertype (CR 205.4d) and, unlike the
/// Kaldheim snow duals (e.g. <see cref="TangledIsletFactory"/>), it has NO
/// printed basic land subtypes — it is simply <c>Snow Land</c>. There is no
/// rider (no life gain, no scry, no cycling) and no triggered ability.
/// </para>
///
/// <para>
/// The full card surface — name, Land type, the Snow supertype, and the two
/// mana abilities — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/boreal-shelf.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture of
/// <see cref="CinderBarrensFactory"/>.
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
/// <see cref="CinderBarrensFactory"/>.
/// </para>
/// </summary>
[CardName("Boreal Shelf")]
public static class BorealShelfFactory
{
    public const string Slug = "boreal-shelf";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Boreal Shelf owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Boreal Shelf with optional replacement-bus wiring so
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
