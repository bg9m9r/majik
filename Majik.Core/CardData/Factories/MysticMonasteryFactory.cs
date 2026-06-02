using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mystic Monastery (Khans of Tarkir — the Jeskai
/// member of the "Wedge tapland" / Khans tri-land cycle). Land.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "This land enters tapped.
///    {T}: Add {U}, {R}, or {W}."
///
/// Shape mirrors <see cref="SeasideCitadelFactory"/>: a plain nonbasic Land
/// (no printed basic land subtypes, no cycling clause) with three vanilla
/// <see cref="ManaAbility"/> instances (one per produced colour, CR 605.1 —
/// mana abilities don't use the stack) and an unconditional enters-tapped
/// restriction (CR 614.1c).
///
/// The base shape (name, Land type, the three colour mana abilities) is
/// materialised from the embedded JSON definition (<c>mystic-monastery.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB-tapped rider is layered
/// on here because the JSON <c>AbilityDefinition</c> schema does not express it
/// yet. On the production load path the unconditional "This land enters
/// tapped." clause is also matched by <see cref="EntersTappedBinder"/> off the
/// oracle text.
/// </summary>
[CardName("Mystic Monastery")]
public static class MysticMonasteryFactory
{
    public const string CardName = "Mystic Monastery";
    public const string Slug = "mystic-monastery";

    /// <summary>
    /// Construct Mystic Monastery with no <see cref="ReplacementBus"/> wired.
    /// The three mana abilities (from JSON) are attached so the card surface is
    /// complete; the ETB-tapped replacement is omitted (shape-only path). This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Mystic Monastery with optional replacement-bus wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">Optional replacement bus the unconditional
    /// enters-tapped restriction (CR 614.1c) is registered against. When null
    /// the registration is skipped (shape-only path); on the production load
    /// path the tapped clause is also matched by the oracle-text binder.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type,
        // {T}: Add {U} / {T}: Add {R} / {T}: Add {W} mana abilities). The
        // ETB-tapped rider is layered on below — it is not expressible in the
        // current JSON AbilityDefinition schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB-tapped restriction (CR 614.1c) — "This land enters tapped."
        // Unconditional; no gate.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        return land;
    }
}
