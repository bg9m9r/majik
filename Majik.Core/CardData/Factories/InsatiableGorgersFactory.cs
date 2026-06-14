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
/// - <b>"Attacks each combat if able" (CR 508.1c — attacks-each-combat
///   restriction)</b>: shipped as a <see cref="KeywordAbility"/>
///   ("AttacksEachCombat") marker, identical to the posture
///   <see cref="UlamogsCrusherFactory"/> uses for the same printed line. The
///   must-attack combat-restriction primitive isn't wired into the live combat
///   step yet; the marker keeps the printed restriction discoverable on the bot
///   / keyword-scan rail until that primitive lands.
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

        // CR 508.1c — "attacks each combat if able" combat restriction. Shipped
        // as a marker only — the must-attack primitive isn't wired into the live
        // combat step yet (same posture as Ulamog's Crusher's identical line).
        card.AddAbility(new KeywordAbility("AttacksEachCombat", card, owner));

        return card;
    }
}
