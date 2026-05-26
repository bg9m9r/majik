using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sigarda, Host of Herons (Avacyn Restored,
/// {2}{G}{W}).
///
/// Legendary Creature — Angel 5/5. Oracle text (Scryfall, verified):
///   "Flying
///    Hexproof (This creature can't be the target of spells or abilities
///    your opponents control.)
///    Spells and abilities your opponents control can't cause you to
///    sacrifice permanents."
///
/// ## Implemented (v1)
/// - 5/5 Legendary Creature — Angel at {2}{G}{W}, owner / controller
///   wired.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/>("Flying")
///   marker — combat code reads via
///   <see cref="Majik.Core.Combat.CombatAbilities"/>, same wiring shape
///   as every other named factory.
/// - <b>Hexproof (CR 702.11)</b>: <see cref="KeywordAbility"/>("Hexproof")
///   marker — consumed by the targeting validator
///   (<c>Majik.Core.Targeting.TargetLegality</c>) to deny opponent-
///   controlled spells / abilities from selecting Sigarda as a target.
///   Same wiring shape as <see cref="StripedRiverwinderFactory"/>'s
///   personal Hexproof.
///
/// ## Deferred (v1 gap)
/// - <b>"Spells and abilities your opponents control can't cause you to
///   sacrifice permanents." (CR 701.16 / CR 800-series sacrifice rider)</b>:
///   no <c>SacrificeRestriction</c> primitive exists today — the costs /
///   effects pipeline routes "you sacrifice a permanent" through
///   <see cref="Majik.Core.Costs.SacrificeAnArtifactCost"/> /
///   <see cref="Majik.Core.Costs.SacrificeAnotherCreatureCost"/> /
///   <see cref="Majik.Core.Costs.AdditionalCost.Sacrifice"/> +
///   <see cref="Majik.Core.Effects.DestroyIntent"/>-style sac resolves,
///   none of which currently consult a "may this player be forced to
///   sacrifice by source X" gate. Wiring the printed rider would require
///   (a) a <c>SacrificeRestriction</c> primitive analogous to
///   <see cref="Majik.Core.Rules.PlayerStaticAbilities"/>'s hexproof
///   surface, threaded through every sacrifice cost / sacrifice effect's
///   source-controller / source-spell check, and (b) lifecycle
///   subscription on Sigarda's ETB / LTB to add / remove the entry.
///   Tracked as a follow-up; v1 ships the shape + the two keyword
///   markers so dispatch / shape / Hexproof targeting tests light up.
///
/// CR rule references: 205.2 (Legendary), 205.3m (Angel subtype),
/// 702.9 (Flying), 702.11 (Hexproof).
/// </summary>
[CardName("Sigarda, Host of Herons")]
public static class SigardaHostOfHeronsFactory
{
    public const string CardName = "Sigarda, Host of Herons";
    public const string PrintedManaCost = "{2}{G}{W}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Sigarda, Host of Herons. Keyword markers (Flying +
    /// Hexproof) are attached; the printed sacrifice-protection rider is
    /// DEFERRED (see class summary) — no extra service wiring is needed
    /// today.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Angel });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. KeywordAbility marker; combat-side reads
        // via CombatAbilities.HasFlying / CanBlockFlying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.11 — Hexproof. KeywordAbility marker; targeting
        // validator (Majik.Core.Targeting.TargetLegality) denies
        // opponent-controlled spells / abilities from selecting Sigarda
        // as a target.
        card.AddAbility(new KeywordAbility("Hexproof", card, owner));

        return card;
    }
}
