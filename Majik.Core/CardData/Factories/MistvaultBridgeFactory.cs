using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mistvault Bridge (March of the Machine: The
/// Aftermath — the UB member of the artifact "Bridge" tapland cycle, the
/// direct sibling of <see cref="RazortideBridgeFactory"/> (WU)).
///
/// Type line: <c>Artifact Land</c>. Oracle text (verified against Scryfall):
/// <code>
/// This land enters tapped.
/// Indestructible
/// {T}: Add {U} or {B}.
/// </code>
///
/// Combines the same two shipped analogues as Razortide Bridge:
/// <list type="bullet">
///   <item><see cref="DarksteelCitadelFactory"/> — Artifact Land typing
///     (additive <see cref="CardType.Artifact"/> flag) + printed
///     Indestructible keyword marker.</item>
///   <item><see cref="SavaiTriomeFactory"/> — unconditional enters-tapped
///     replacement + one <see cref="ManaAbility"/> per produced colour.</item>
/// </list>
///
/// ## Implemented
/// - <b>Artifact Land</b> (CR 301.1 / 305.1) — concrete <see cref="Land"/>
///   with <see cref="CardType.Artifact"/> additively flagged via
///   <see cref="Card.AddCardType"/> (mirrors Darksteel Citadel). Lands have
///   no mana cost (CR 305.1). Not Basic; no land subtype.
/// - <b>Indestructible</b> (CR 702.12) — <see cref="KeywordAbility"/> marker,
///   read by the non-creature destroy gate (mirrors Darksteel Citadel).
/// - <b>Enters-tapped</b> (CR 614.1c) — unconditional "This land enters
///   tapped." registered via <see cref="EntersTappedReplacement"/> on the
///   supplied <see cref="ReplacementBus"/>. The shape-only path (null bus)
///   skips registration, mirroring <see cref="RazortideBridgeFactory"/>. On
///   the production load path the tapped clause is also matched by
///   <see cref="Majik.Core.CardData.EntersTappedBinder"/> off the oracle text.
/// - <b>{T}: Add {U} or {B}</b> (CR 605.1 — mana abilities don't use the
///   stack) — two vanilla <see cref="ManaAbility"/> instances, one per
///   produced colour.
/// </summary>
[CardName("Mistvault Bridge")]
public static class MistvaultBridgeFactory
{
    public const string CardName = "Mistvault Bridge";

    /// <summary>
    /// Construct Mistvault Bridge owned and controlled by
    /// <paramref name="owner"/>. Single-arg path — no bus wiring (shape
    /// observability only; the enters-tapped replacement is omitted).
    /// </summary>
    public static Land Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Mistvault Bridge with an optional <see cref="ReplacementBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "This land enters tapped." replacement is registered (CR 614.1c).</param>
    public static Land Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);

        // CR 301.1 / 305.1 — Mistvault Bridge is an Artifact Land. The base
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
        // {T}: Add {U} or {B}. CR 605.1 — two mana abilities (no stack),
        // one per produced colour.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        return land;
    }
}
