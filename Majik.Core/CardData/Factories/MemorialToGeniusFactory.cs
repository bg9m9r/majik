using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Memorial to Genius (Ixalan "Memorial"
/// sacrifice-for-value utility land cycle — blue member).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {U}.
///    {4}{U}, {T}, Sacrifice this land: Draw two cards."
///
/// <para>
/// A sibling of <see cref="BondersEnclaveFactory"/> (mana + activated draw
/// utility land) whose value ability sacrifices itself for cards — the exact
/// <c>{mana},{T},Sacrifice this land</c> cost stack of Dreamstone Hedron's
/// "{3},{T},Sacrifice: Draw three cards" (a stock JSON cost composition:
/// <c>mana</c> + <c>tap_self</c> + <c>sacrifice_self</c>). The full card
/// surface — name, Land type, the {T}: Add {U} mana ability, and the
/// {4}{U},{T},Sacrifice this land: Draw two cards activated ability — is
/// declared declaratively in
/// <c>Majik.Core/CardData/Cards/memorial-to-genius.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, mirroring the JSON-driven posture
/// of <see cref="AkoumRefugeFactory"/>.
/// </para>
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain nonbasic Land, no supertype, no subtype
///   (from JSON).
/// - <b>{T}: Add {U}</b> — vanilla <see cref="Majik.Core.Abilities.ManaAbility"/>
///   (CR 605.1 — mana abilities don't use the stack), from JSON.
/// - <b>{4}{U}, {T}, Sacrifice this land: Draw two cards</b> — an
///   <see cref="Majik.Core.Abilities.ActivatedAbility"/> whose cost stack is
///   ManaCostCost({4}{U}) + tap-self + sacrifice-self
///   (<c>AdditionalCostType.Tap</c> / <c>AdditionalCostType.Sacrifice</c>),
///   resolving the standard two-card <c>draw_card</c> effect (CR 120), from
///   JSON. Same cost+effect shape the engine already supports for Dreamstone
///   Hedron.
/// - <b>Enters-tapped</b> — "This land enters tapped." (CR 614.1c) is an
///   unconditional enters-tapped replacement. On the production load path it
///   is matched off the printed oracle text by
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/>; the two-arg
///   <see cref="Create(Player, ReplacementBus?)"/> path also registers an
///   <see cref="EntersTappedReplacement"/> directly on a supplied
///   <see cref="ReplacementBus"/>. The shape-only single-arg path skips the
///   registration (no bus available) — same posture as
///   <see cref="AkoumRefugeFactory"/>.
/// </summary>
[CardName("Memorial to Genius")]
public static class MemorialToGeniusFactory
{
    public const string Slug = "memorial-to-genius";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Construct Memorial to Genius owned and controlled by
    /// <paramref name="owner"/> (shape-only path — enters-tapped is omitted,
    /// no <see cref="ReplacementBus"/> to register against). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Memorial to Genius with optional replacement-bus
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
