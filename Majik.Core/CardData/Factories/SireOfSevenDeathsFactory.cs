using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sire of Seven Deaths (Modern Horizons 3, {7}).
///
/// Creature — Eldrazi 7/7. Oracle text (Scryfall, verified):
///   "First strike, vigilance
///    Menace, trample
///    Reach, lifelink
///    Ward—Pay 7 life."
///
/// Direct-construction factory mirroring <see cref="RealitySmasherFactory"/>
/// (another Eldrazi with a pile of combat keyword markers + a non-mana Ward
/// rider). The current <see cref="CardDefinition"/> JSON schema cannot
/// express <see cref="KeywordAbility"/> markers, so — like Reality Smasher —
/// this card is built in C# and attaches its keywords directly.
///
/// ## Implemented (v1)
/// - 7/7 Creature — Eldrazi at {7}.
/// - Six evergreen combat keywords shipped as <see cref="KeywordAbility"/>
///   markers consumed by CombatValidator / CombatAbilities:
///     First Strike (CR 702.7), Vigilance (CR 702.20), Menace (CR 702.111),
///     Trample (CR 702.19), Reach (CR 702.17), Lifelink (CR 702.15).
/// - <b>Printed "Pay 7 life" Ward variant (CR 702.21)</b>: shipped as a
///   <see cref="KeywordAbility"/>("Ward") marker so the discovery surface
///   stays uniform with Reality Smasher / Kappa Cannoneer / other Ward
///   carriers, plus a bound <see cref="WardEffect"/> via
///   <see cref="BuildWardEffect"/> whose payment is a real
///   <see cref="PayLifeCost"/>(7) (non-mana ward, CR 702.21c).
///   <see cref="WardEffect.Resolve"/> counters an opponent's targeting
///   spell/ability unless they pay 7 life — the life rider is now
///   functional, not structural-only.
/// </summary>
[CardName("Sire of Seven Deaths")]
public static class SireOfSevenDeathsFactory
{
    public const string CardName = "Sire of Seven Deaths";
    public const string PrintedManaCost = "{7}";
    public const int Power = 7;
    public const int Toughness = 7;

    /// <summary>Printed Ward cost — non-mana (Pay 7 life). Carried as a
    /// documentation constant; see class xmldoc for the deferred wiring
    /// gap.</summary>
    public const string WardLifeCost = "Pay 7 life";

    /// <summary>The amount of life an opponent pays for Sire's Ward.</summary>
    public const int WardLifeAmount = 7;

    /// <summary>
    /// CR 702.21 — Sire of Seven Deaths' printed Ward effect, bound to the
    /// supplied <paramref name="card"/>. The ward cost is the non-mana
    /// "Pay 7 life" rider (see <see cref="WardLifeCost"/>), modelled via
    /// <see cref="PayLifeCost"/>; the mana portion is
    /// <see cref="ManaCost.Zero"/>. <see cref="WardEffect.Resolve"/> charges
    /// the 7-life payment when an opponent's spell/ability targets Sire (same
    /// posture as <see cref="RealitySmasherFactory.BuildWardEffect"/>).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, new PayLifeCost(WardLifeAmount));

    /// <summary>
    /// Construct Sire of Seven Deaths. All six combat keyword markers plus
    /// the Ward marker attached; the Ward trigger / non-mana life-payment
    /// rider is structural-only (see class xmldoc).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.7 — First strike. CR 702.20 — Vigilance. CR 702.111 —
        // Menace. CR 702.19 — Trample. CR 702.17 — Reach. CR 702.15 —
        // Lifelink. CR 702.21 — Ward (printed: "Pay 7 life"). All shipped
        // as keyword markers consumed by CombatValidator / CombatAbilities
        // / the future Ward-trigger primitive. Same wiring posture as
        // Reality Smasher (combat keyword markers + standalone WardEffect
        // helper).
        card.AddAbility(new KeywordAbility("First Strike", card, owner));
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));
        card.AddAbility(new KeywordAbility("Menace", card, owner));
        card.AddAbility(new KeywordAbility("Trample", card, owner));
        card.AddAbility(new KeywordAbility("Reach", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        return card;
    }
}
