using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thornglint Bridge (Tarkir: Dragonstorm — the GW
/// member of the artifact "Bridge" tapland cycle).
///
/// Type line: <c>Artifact Land</c>. Oracle text (verified against Scryfall):
/// <code>
/// This land enters tapped.
/// Indestructible
/// {T}: Add {G} or {W}.
/// </code>
///
/// Hand-rolled (not JSON-loaded) for the same reason as its cycle-mate
/// <see cref="RazortideBridgeFactory"/>: the printed <b>Indestructible</b>
/// keyword has no representation in the data-only
/// <see cref="Majik.Core.CardData.Definitions.CardDefinition"/> schema
/// (<c>CardDefinitionFactory</c> supports only mana/activated/triggered
/// ability shapes, never a static keyword marker). Identical oracle shape to
/// <see cref="RazortideBridgeFactory"/>; only the produced colours differ
/// (G/W vs W/U).
///
/// ## Implemented
/// - <b>Artifact Land</b> (CR 301.1 / 305.1) — concrete <see cref="Land"/>
///   with <see cref="CardType.Artifact"/> additively flagged via
///   <see cref="Card.AddCardType"/> (mirrors Darksteel Citadel). Lands have
///   no mana cost (CR 305.1). Not Basic; no land subtype.
/// - <b>Indestructible</b> (CR 702.12) — <see cref="KeywordAbility"/> marker,
///   read by the non-creature destroy gate (mirrors Razortide Bridge).
/// - <b>Enters-tapped</b> (CR 614.1c) — unconditional "This land enters
///   tapped." registered via <see cref="EntersTappedReplacement"/> on the
///   supplied <see cref="ReplacementBus"/>. The shape-only path (null bus)
///   skips registration. On the production load path the tapped clause is
///   also matched by <see cref="Majik.Core.CardData.EntersTappedBinder"/>
///   off the oracle text.
/// - <b>{T}: Add {G} or {W}</b> (CR 605.1 — mana abilities don't use the
///   stack) — two vanilla <see cref="ManaAbility"/> instances, one per
///   produced colour.
/// </summary>
[CardName("Thornglint Bridge")]
public static class ThornglintBridgeFactory
{
    public const string CardName = "Thornglint Bridge";

    /// <summary>
    /// Construct Thornglint Bridge owned and controlled by
    /// <paramref name="owner"/>. Single-arg path — no bus wiring (shape
    /// observability only; the enters-tapped replacement is omitted).
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Thornglint Bridge with an optional <see cref="ReplacementBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "This land enters tapped." replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);

        // CR 301.1 / 305.1 — Thornglint Bridge is an Artifact Land. The base
        // Land constructor only registers CardType.Land, so additively flag
        // the Artifact type (mirrors Darksteel Citadel). This is the gate the
        // affinity-for-artifacts / metalcraft accounting keys on.
        land.AddCardType(CardType.Artifact);

        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Indestructible (CR 702.12). Marker only — destroy gates read
        // KeywordAbility off Permanent.
        // ----------------------------------------------------------------
        land.AddAbility(new KeywordAbility("Indestructible", land, owner));

        // ----------------------------------------------------------------
        // Enters-tapped — CR 614.1c. Unconditional "This land enters tapped."
        // Shape-only path (no ReplacementBus) skips registration; same
        // posture as RazortideBridgeFactory.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new EntersTappedReplacement(land));
        }

        // ----------------------------------------------------------------
        // {T}: Add {G} or {W}. CR 605.1 — two mana abilities (no stack),
        // one per produced colour.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        return land;
    }
}
