using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Insatiable Gorgers (Eldritch Moon, {2}{R}{R}).
///
/// Creature — Vampire Berserker 5/3. Oracle text (verified against Scryfall
/// 2026-06-14):
///   "This creature attacks each combat if able.
///    Madness {3}{R}"
///
/// ## Implemented (v1)
/// - <b>5/3 Creature — Vampire Berserker at {2}{R}{R}.</b>
/// - <b>"Attacks each combat if able" (CR 508.1a / 702.43 — the must-attack
///   combat restriction)</b>: shipped as a <see cref="KeywordAbility"/>
///   ("AttacksEachCombat") marker that <see cref="Majik.Core.Combat.CombatFlow"/>
///   now ENFORCES at declare-attackers: an eligible creature carrying this
///   marker is force-declared into combat (CR 508.1a — "if able") even when its
///   controller's agent omits it, mirroring the must-block enforcement in
///   <see cref="Majik.Core.Combat.CombatValidator"/>. Same marker
///   <see cref="UlamogsCrusherFactory"/> uses for the identical printed line.
///
/// ## Madness (NOT wired here — intrinsic)
/// Madness {3}{R} works intrinsically for every catalogued card (CR 702.35) via
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> consulted by the central
/// discard funnel <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>; "Insatiable
/// Gorgers" is catalogued at {3}{R}, so the madness line needs no factory code.
///
/// A bespoke <see cref="CardName"/> factory (rather than a fileless JSON body)
/// is required because the JSON <c>CardDef</c> schema does not express the
/// AttacksEachCombat keyword marker — same reason
/// <see cref="UlamogsCrusherFactory"/> is a hand-written factory.
/// </summary>
[CardName("Insatiable Gorgers")]
public static class InsatiableGorgersFactory
{
    public const string CardName = "Insatiable Gorgers";
    public const string PrintedManaCost = "{2}{R}{R}";
    public const int Power = 5;
    public const int Toughness = 3;

    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire, CardSubtype.Berserker });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 508.1a / 702.43 — "attacks each combat if able". The marker is now
        // ENFORCED by CombatFlow: this creature is force-declared as an attacker
        // whenever it can legally attack (same posture as Ulamog's Crusher).
        card.AddAbility(new KeywordAbility("AttacksEachCombat", card, owner));

        return card;
    }
}
