using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seaside Citadel (Conflux — the Bant member of the
/// original "tapped tri-land" cycle; reprinted in Commander products). Land.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "This land enters tapped.
///    {T}: Add {G}, {W}, or {U}."
///
/// Shape is the Savai Triome / tri-land posture minus the cycling clause and
/// minus the printed basic land subtypes: a plain nonbasic Land with three
/// vanilla <see cref="ManaAbility"/> instances (one per produced colour,
/// CR 605.1 — mana abilities don't use the stack) and an unconditional
/// enters-tapped restriction (CR 614.1c).
///
/// The base shape (name, Land type, the three colour mana abilities) is
/// materialised from the embedded JSON definition (<c>seaside-citadel.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB-tapped rider is layered
/// on here because the JSON <c>AbilityDefinition</c> schema does not express
/// it yet (same posture as <see cref="RestlessBivouacFactory"/> /
/// <see cref="RestlessSpireFactory"/>). On the production load path the
/// unconditional "This land enters tapped." clause is also matched by
/// <see cref="EntersTappedBinder"/> off the oracle text.
/// </summary>
[CardName("Seaside Citadel")]
public static class SeasideCitadelFactory
{
    public const string CardName = "Seaside Citadel";
    public const string Slug = "seaside-citadel";

    /// <summary>
    /// Construct Seaside Citadel with no <see cref="ReplacementBus"/> wired.
    /// The three mana abilities (from JSON) are attached so the card surface
    /// is complete; the ETB-tapped replacement is omitted (shape-only path).
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Seaside Citadel with optional replacement-bus wiring.
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
        // {T}: Add {G} / {T}: Add {W} / {T}: Add {U} mana abilities). The
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
