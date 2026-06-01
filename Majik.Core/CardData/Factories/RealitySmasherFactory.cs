using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reality Smasher (Oath of the Gatewatch, {4}{C}).
///
/// Creature — Eldrazi 5/5. Oracle text (Scryfall, verified):
///   "Trample, haste
///    Whenever this creature becomes the target of a spell an opponent
///    controls, counter that spell unless its controller discards a card."
///
/// ## Implemented (v1)
/// - 5/5 Creature — Eldrazi at {4}{C}.
/// - Trample (CR 702.19) + Haste (CR 702.10) as <see cref="KeywordAbility"/>
///   markers — same wiring shape as Slickshot Show-Off's Flying + Haste pair.
/// - <b>Printed "discard a card" Ward variant (CR 702.21)</b>: shipped as
///   a <see cref="KeywordAbility"/>("Ward") marker so the discovery surface
///   stays uniform with Kappa Cannoneer / other Ward carriers, plus a bound
///   <see cref="WardEffect"/> via <see cref="BuildWardEffect"/> whose payment
///   is a real <see cref="DiscardACardCost"/> (non-mana ward, CR 702.21c).
///   <see cref="WardEffect.Resolve"/> counters an opponent's targeting
///   spell/ability unless they discard a card — the discard rider is now
///   functional, not structural-only.
/// </summary>
[CardName("Reality Smasher")]
public static class RealitySmasherFactory
{
    public const string CardName = "Reality Smasher";
    public const string PrintedManaCost = "{4}{C}";
    public const int Power = 5;
    public const int Toughness = 5;

    /// <summary>Printed Ward cost — non-mana (discard a card). Carried as a
    /// documentation constant; see class xmldoc for the deferred wiring
    /// gap.</summary>
    public const string WardDiscardCost = "Discard a card";

    /// <summary>
    /// CR 702.21 — Reality Smasher's printed Ward effect, bound to the
    /// supplied <paramref name="card"/>. The ward cost is the non-mana
    /// "discard a card" rider (see <see cref="WardDiscardCost"/>), modelled
    /// via <see cref="DiscardACardCost"/>; the mana portion is
    /// <see cref="ManaCost.Zero"/>. <see cref="WardEffect.Resolve"/> charges
    /// the discard when an opponent's spell/ability targets Reality Smasher
    /// (same posture as <see cref="KappaCannoneerFactory.BuildWardEffect"/>'s
    /// mana ward).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, new DiscardACardCost());

    /// <summary>
    /// Construct Reality Smasher. Trample + Haste + Ward keyword markers
    /// attached; the Ward trigger / non-mana discard rider is structural-
    /// only (see class xmldoc).
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

        // CR 702.19 — Trample. CR 702.10 — Haste. CR 702.21 — Ward
        // (printed: "discard a card"). All shipped as keyword markers
        // consumed by CombatValidator / CombatAbilities / the future
        // Ward-trigger primitive. Same wiring posture as Kappa Cannoneer
        // (Ward marker + standalone WardEffect helper).
        card.AddAbility(new KeywordAbility("Trample", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        return card;
    }
}
