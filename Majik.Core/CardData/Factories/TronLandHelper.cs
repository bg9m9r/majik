using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Shared helper for the Urza "Tron" land cycle (Urza's Mine, Urza's
/// Tower, Urza's Power-Plant — Antiquities). Each card has the
/// identical mana ability:
///
///   "{T}: Add {C}. If you control an Urza's Mine, an Urza's
///    Power-Plant, and an Urza's Tower, add {2} instead."
///
/// CR 605 — this is a mana ability (no stack, no targets). The "if you
/// control all three" check runs at activation time against the
/// controller's battlefield zone. The amount added is dynamic, so it's
/// wired via the <see cref="Majik.Core.Abilities.ManaAbility"/>
/// <c>Func&lt;ManaCost&gt;</c> overload — at activation we compute the
/// amount from the live battlefield state rather than baking a fixed
/// value into the ability.
///
/// "Urza's land" in the conditional is shorthand for the printed
/// subtype combination — an Urza's land has subtype <see cref="CardSubtype.Urzas"/>
/// plus exactly one of {Mine, Tower, PowerPlant}. The check tests for
/// the three secondary subtypes on permanents the controller controls.
/// </summary>
public static class TronLandHelper
{
    /// <summary>
    /// Compute the mana amount the Tron lands' tap ability adds at the
    /// moment of activation. Returns {2} (two generic) if the
    /// controller's battlefield contains at least one permanent with
    /// each of <see cref="CardSubtype.Mine"/>, <see cref="CardSubtype.Tower"/>,
    /// and <see cref="CardSubtype.PowerPlant"/> subtypes — otherwise
    /// returns {C} (one colourless, which the engine buckets as +1
    /// generic per CR 107.4c).
    ///
    /// Controller-only: opposing Tron pieces never count. Matches CR
    /// 109.5 "you / control" wording on the printed oracle text.
    /// </summary>
    public static ManaCost ComputeManaAddition(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var battlefield = controller.Zones.Battlefield.GetCards();
        var hasMine = false;
        var hasTower = false;
        var hasPowerPlant = false;
        foreach (var card in battlefield)
        {
            if (!hasMine && card.HasSubtype(CardSubtype.Mine)) hasMine = true;
            if (!hasTower && card.HasSubtype(CardSubtype.Tower)) hasTower = true;
            if (!hasPowerPlant && card.HasSubtype(CardSubtype.PowerPlant)) hasPowerPlant = true;
            if (hasMine && hasTower && hasPowerPlant) break;
        }

        return (hasMine && hasTower && hasPowerPlant)
            ? ManaCost.Parse("2")
            : ManaCost.Parse("C");
    }
}
