using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ice Tunnel (Kaldheim — the U/B member of the snow
/// "tapped dual land" cycle). Land.
///
/// Type line: Snow Land — Island Swamp.
///
/// Oracle text (verified against Scryfall 2026-06-14):
///   "({T}: Add {U} or {B}.)
///    This land enters tapped."
///
/// Same shape and posture as <see cref="NomadOutpostFactory"/> /
/// Snowfield Sinkhole (the W/B sibling in the same cycle): a nonbasic Snow
/// Land (CR 205.4d) with the printed Island and Swamp land subtypes
/// (CR 205.3i) and two vanilla <see cref="ManaAbility"/> instances — one
/// producing {U}, one producing {B} (CR 605.1 — mana abilities don't use the
/// stack). The intrinsic Island/Swamp mana abilities are spelled out
/// explicitly in the JSON because this is a nonbasic land (the
/// AttachBasicLandMana helper only fires for Basic lands).
///
/// The base shape (name, Snow Land type, Island/Swamp subtypes, the two
/// colour mana abilities) is materialised from the embedded JSON definition
/// (<c>ice-tunnel.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB-tapped rider
/// (CR 614.1c — "This land enters tapped.") is layered on below when a
/// <see cref="ReplacementBus"/> is supplied; the current JSON
/// <c>AbilityDefinition</c> schema does not express it. On the production
/// load path the unconditional enters-tapped clause is also matched by
/// <see cref="EntersTappedBinder"/> off the oracle text.
/// </summary>
[CardName("Ice Tunnel")]
public static class IceTunnelFactory
{
    public const string CardName = "Ice Tunnel";
    public const string Slug = "ice-tunnel";

    /// <summary>
    /// Construct Ice Tunnel with no <see cref="ReplacementBus"/> wired. The
    /// two mana abilities (from JSON) are attached so the card surface is
    /// complete; the ETB-tapped replacement is omitted (shape-only path).
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, replacements: null);

    /// <summary>
    /// Construct Ice Tunnel with optional replacement-bus wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">Optional replacement bus the unconditional
    /// enters-tapped restriction (CR 614.1c) is registered against. When null
    /// the registration is skipped (shape-only path); on the production load
    /// path the tapped clause is also matched by the oracle-text binder.</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Snow Land type,
        // Island/Swamp subtypes, {T}: Add {U} / {T}: Add {B} mana abilities).
        // The ETB-tapped rider is layered on below — it is not expressible in
        // the current JSON AbilityDefinition schema.
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
