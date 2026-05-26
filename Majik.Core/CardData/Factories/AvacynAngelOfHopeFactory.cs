using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Avacyn, Angel of Hope (Avacyn Restored,
/// {5}{W}{W}{W}).
///
/// Legendary Creature — Angel 8/8. Oracle text (Scryfall, verified):
///   "Flying, vigilance
///    Indestructible
///    Other permanents you control have indestructible."
///
/// ## Implemented (v1)
/// - 8/8 Legendary Creature — Angel at {5}{W}{W}{W}, owner / controller
///   wired.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/>("Flying")
///   marker — combat-side reads via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>.
/// - <b>Vigilance (CR 702.20)</b>: <see cref="KeywordAbility"/>
///   ("Vigilance") marker — read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/> to
///   skip the tap-on-attack step.
/// - <b>Indestructible (CR 702.12)</b>: <see cref="KeywordAbility"/>
///   ("Indestructible") marker — SBA 704.5g + the destroy /
///   regeneration pipeline read it via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/>
///   (creature path through the layer system when wired; non-creature
///   permanent path falls back to the marker directly). Identical
///   wiring to Heliod, Sun-Crowned / The One Ring / Ulamog, the
///   Ceaseless Hunger.
///
/// ## Deferred (v1 gap)
/// - <b>"Other permanents you control have indestructible." (CR 702.12 /
///   CR 613 anthem-style static)</b>: the printed anthem rider grants
///   Indestructible to every OTHER permanent on the controller's
///   battlefield — creatures, artifacts, enchantments, planeswalkers,
///   lands. <see cref="Majik.Core.Effects.LordStaticEffect"/> is the
///   closest existing primitive but is scoped to <c>Creature</c> +
///   filtered by <see cref="CardSubtype"/>, so it can't widen to
///   "any permanent type" today. Wiring the printed rider cleanly
///   requires either (a) a new <c>ControllerPermanentAnthemEffect</c>
///   primitive that walks the controller's battlefield without a
///   creature / subtype filter and stamps Indestructible at Layer 6
///   on creatures + maintains a parallel marker surface on
///   non-creature permanents (matching the dual path
///   <see cref="Majik.Core.CardData.OracleSpellBinder"/>'s
///   <c>HasIndestructible</c> already takes for non-creature permanents),
///   or (b) a sync-driven lifecycle that registers / removes
///   <see cref="KeywordAbility"/>("Indestructible") markers on the
///   controller's other permanents while Avacyn is on the battlefield.
///   Tracked as a follow-up; v1 ships Avacyn's own evergreens so
///   dispatch / shape / her personal Indestructible (SBA recheck) light
///   up, and the anthem-recipient surface comes online in the
///   primitive PR.
///
/// CR rule references: 205.2 (Legendary), 205.3m (Angel subtype),
/// 702.9 (Flying), 702.12 (Indestructible), 702.20 (Vigilance).
/// </summary>
[CardName("Avacyn, Angel of Hope")]
public static class AvacynAngelOfHopeFactory
{
    public const string CardName = "Avacyn, Angel of Hope";
    public const string PrintedManaCost = "{5}{W}{W}{W}";
    public const int Power = 8;
    public const int Toughness = 8;

    /// <summary>
    /// Construct Avacyn, Angel of Hope. Personal evergreens (Flying,
    /// Vigilance, Indestructible) are attached as
    /// <see cref="KeywordAbility"/> markers; the "other permanents you
    /// control have indestructible" anthem rider is DEFERRED (see class
    /// summary). No extra service wiring required today.
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

        // CR 702.9 — Flying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.20 — Vigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // CR 702.12 — Indestructible. Marker only; SBA 704.5g + the
        // destroy pipeline read via CombatAbilities.HasIndestructible
        // (creature) or the printed-marker fallback in
        // OracleSpellBinder.HasIndestructible (non-creature permanents).
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

        return card;
    }
}
