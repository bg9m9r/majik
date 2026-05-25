using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Etched Champion (Mirrodin Besieged, {3}).
///
/// Artifact Creature — Soldier 2/2. Oracle text:
///   "Metalcraft — As long as you control three or more artifacts,
///    Etched Champion has protection from all colors."
///
/// ## Implemented (v1)
///
/// - 2/2 Artifact Creature — Soldier at {3}. The Artifact type is
///   additively flagged via <see cref="Card.AddCardType"/> so HasType
///   lookups see both types (same shape as
///   <see cref="FrogmiteFactory"/> / <see cref="ArcboundRavagerFactory"/>).
/// - <b>Metalcraft conditional protection (CR 702.95 / CR 702.16)</b>:
///   wired as a single <see cref="ProtectionAbility"/>("all colors")
///   carrying an <see cref="ProtectionAbility.IsActive"/> closure gated
///   on <see cref="MetalcraftActive"/>. While Etched Champion's
///   controller controls three or more artifacts on the battlefield,
///   the protection is active and
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>
///   answers true for every colour (the "all colors" string match in
///   <see cref="Majik.Core.Rules.Protection"/> covers W/U/B/R/G via
///   the OR clause). When the controller has fewer than three
///   artifacts, the IsActive gate filters the ability out of the
///   <see cref="Majik.Core.Rules.Protection"/> qualities scan so
///   damage / target / block gates behave as if it weren't there.
/// - Etched Champion itself counts toward Metalcraft (it is an
///   artifact). With Etched Champion plus two other artifacts the
///   threshold is met. The opponent's artifacts do not contribute —
///   the scan is scoped to the controller's battlefield zone (same
///   convention as <see cref="MoxOpalFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Activation-style gate</b>: Mox Opal models Metalcraft as a
///   <c>canActivateCheck</c> on its mana ability. Etched Champion's
///   Metalcraft is a static modifier (no activation surface), so the
///   gate is woven into the ProtectionAbility itself rather than a
///   parallel activation predicate.
/// - <b>Layered "has protection" effects</b>: a future layer-6
///   continuous-effect surface for "gains protection from X" could
///   replace the IsActive predicate with a proper layer-applied
///   <see cref="StaticAbility"/>; the read shape would still go
///   through the same Protection helpers, so callers won't change.
/// </summary>
[CardName("Etched Champion")]
public static class EtchedChampionFactory
{
    public const string CardName = "Etched Champion";
    public const string PrintedManaCost = "{3}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int MetalcraftThreshold = 3;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Soldier });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types (mirrors Frogmite / Arcbound Ravager / Walking Ballista).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Metalcraft — As long as you control three or more artifacts,
        // Etched Champion has protection from all colors.
        // CR 702.95 + CR 702.16.
        //
        // Single ProtectionAbility("all colors") gated on MetalcraftActive.
        // Rules.Protection.HasProtectionFromColor reads the OR clause
        // (quality == colour-name || quality == "all colors") so a single
        // marker covers W/U/B/R/G simultaneously. The IsActive closure is
        // re-evaluated on every Qualities() read, so the protection turns
        // on and off live as artifacts enter and leave the controller's
        // battlefield.
        // ----------------------------------------------------------------
        card.AddAbility(new ProtectionAbility(
            quality: "all colors",
            spellPredicate: null,
            isActive: () => MetalcraftActive(card)));

        return card;
    }

    /// <summary>
    /// CR 702.95 — Metalcraft is active for an object's controller when
    /// they control three or more artifacts. Counts every artifact-type
    /// permanent on the controller's battlefield (Etched Champion itself
    /// included when it is on the battlefield, same convention as
    /// <see cref="MoxOpalFactory"/>).
    /// </summary>
    public static bool MetalcraftActive(Creature etchedChampion)
    {
        var controller = etchedChampion.Controller;
        if (controller == null) return false;

        var count = 0;
        foreach (var c in controller.Zones.Battlefield.GetCards())
        {
            if (c.HasType(CardType.Artifact))
            {
                count++;
                if (count >= MetalcraftThreshold) return true;
            }
        }
        return count >= MetalcraftThreshold;
    }
}
