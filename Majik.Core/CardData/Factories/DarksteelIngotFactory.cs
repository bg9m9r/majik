using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Darksteel Ingot (Darksteel, {3}).
///
/// Artifact. Oracle text:
///   "Indestructible (Effects that say \"destroy\" don't destroy this artifact.)
///    {T}: Add one mana of any color."
///
/// ## Implementation (v1)
/// - Card identity: Artifact with mana cost {3}, owner / controller wiring.
/// - <b>Indestructible</b> (CR 702.12) — a <see cref="KeywordAbility"/>
///   marker, the same shape as <see cref="TheOneRingFactory"/> and Hammer
///   of Nazahn's Indestructible grant. The destroy gate in
///   <see cref="Majik.Core.CardData.OracleSpellBinder"/> reads this marker
///   for non-creature permanents (CR 702.12b: a "destroy" effect can't
///   destroy an indestructible permanent), so no extra wiring is needed.
/// - <b>{T}: Add one mana of any color</b> — modeled as five
///   <see cref="ManaAbility"/> instances (one per WUBRG), the same shape
///   as <see cref="MoxOpalFactory"/> (minus its Metalcraft gate), City of
///   Brass, etc. CR 605.1 — mana abilities don't use the stack.
///
/// ## Deferred (v1 gaps)
/// - "Mana of any color" is bound as five separate ManaAbility instances;
///   the bot's source-picker selects the right colour at payment time. A
///   single modal-colour ManaAbility (one ability, choose colour at
///   activation) is not in the engine yet — same posture as Mox Opal /
///   City of Brass / Delighted Halfling.
/// </summary>
[CardName("Darksteel Ingot")]
public static class DarksteelIngotFactory
{
    public const string CardName = "Darksteel Ingot";
    public const string PrintedManaCost = "{3}";

    /// <summary>
    /// Construct Darksteel Ingot owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var ingot = new Artifact(CardName, PrintedManaCost);
        ingot.SetOwner(owner);
        ingot.SetController(owner);

        // ----------------------------------------------------------------
        // Indestructible (CR 702.12). Marker only — the destroy gate in
        // OracleSpellBinder.MoveToGraveyard reads the printed
        // KeywordAbility("Indestructible") for non-creature permanents.
        // ----------------------------------------------------------------
        ingot.AddAbility(new KeywordAbility("Indestructible", ingot, owner));

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color.
        // Five ManaAbility instances (one per WUBRG). CR 605.1 — mana
        // abilities don't use the stack; the bot's source picker chooses
        // the colour needed at payment time. Same shape as Mox Opal.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            ingot.AddAbility(new ManaAbility(ingot, owner, ManaCost.Parse(color)));
        }

        return ingot;
    }
}
