using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Trinisphere
/// (Darksteel — Artifact {3}).
///
/// Oracle text:
///   "As long as Trinisphere is untapped, each spell that would cost less
///    than three mana to cast costs three mana to cast. (Spells with mana
///    cost less than three with any colored mana symbols in their mana
///    costs cost three mana to cast.)"
///
/// ## Implementation
///
/// ### Cost floor at three (CR 117.7 / CR 601.2f)
/// Wired as a <see cref="SpellCostIncreaseAbility"/> on the card. Predicate
/// matches every spell whose printed total mana value is &lt; 3; the
/// per-cast delta is <c>3 - printedTotal</c> generic, which raises the
/// effective cost to exactly three. The parenthetical reminder text is
/// captured naturally — <see cref="ManaCost.TotalValue"/> already counts
/// coloured pips and generic pips, so {U}{U} (TotalValue = 2) is tagged
/// less-than-three and floored to {1}{U}{U} (TotalValue = 3). Symmetric —
/// applies to both players' spells. <see cref="CostReduction.GetEffectiveCost(
/// ICard, Player, IEnumerable{Player}?)"/> scans every player's battlefield
/// for these riders, so opposing copies of Trinisphere also raise the
/// floor.
///
/// ### Untapped gate (CR 110.5 / printed conditional)
/// The cost rider is gated on Trinisphere being untapped at evaluation
/// time. The factory captures the constructed <see cref="Artifact"/> in the
/// rider's closure and reads <see cref="Permanent.IsTapped"/> on each
/// invocation — when Trinisphere is tapped, <see cref="SpellCostIncreaseAbility.ExtraGeneric"/>
/// returns 0 and no rider applies. Tapping Trinisphere to disable its
/// effect (the classic combo with Voltaic Key etc.) is therefore captured.
///
/// ## Caveats / Deferred
/// - Printed total vs. effective total — the rider compares against the
///   printed <see cref="ManaCost.TotalValue"/> rather than the in-flight
///   reduced cost. Interactions with cost-reduction effects (Goblin
///   Electromancer reducing a {2} spell to {1}, then Trinisphere flooring
///   to {3}) are not modelled — the spell pays its reduced cost without
///   the Trinisphere floor kicking back in. The CR-strict reading would
///   walk the reduced effective cost, but layering Trinisphere through
///   <see cref="CostReduction.GetEffectiveCost"/> requires the rider to
///   read its own siblings' output, which is structurally tricky in the
///   current additive-after-reduction pipeline. Tracked as a follow-up.
/// - X spells — when <see cref="ManaCost.HasX"/> is set the printed total
///   doesn't include the chosen X value, so we skip the floor for X spells
///   to avoid prematurely flooring {X}{R} (value 1 printed, chosen X may
///   already exceed three). Strict CR would compute after X is chosen at
///   cast time — same plumbing follow-up as above.
/// - <see cref="CostReduction.GetEffectiveCost"/> call sites
///   (<see cref="Majik.Core.Game.SpellCastFlow"/>,
///   <see cref="Majik.Core.Game.TurnDriver"/>,
///   <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>) currently
///   call the two-arg overload; they need to forward the all-players list
///   for the cost rider to apply in live play. Same follow-up tracked for
///   Damping Sphere / Thalia / Sphere of Resistance.
/// </summary>
[CardName("Trinisphere")]
public static class TrinisphereFactory
{
    public const string CardName = "Trinisphere";
    public const string PrintedManaCost = "{3}";

    /// <summary>
    /// Construct Trinisphere with the correct card shape — an Artifact {3}
    /// with the untapped-gated cost-floor rider attached as static metadata.
    /// Suitable for shape / dispatcher tests and for production use (no
    /// live continuous-effects registration needed for the cost rider).
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 / CR 601.2f — "As long as Trinisphere is untapped, each
        // spell that would cost less than three mana to cast costs three
        // mana to cast." The closure on `card` reads IsTapped at evaluation
        // time so tapping Trinisphere disables the floor for that cast.
        // The delta is `3 - printedTotal` generic so the floored cost is
        // exactly three. X spells (HasX) are skipped — see class XML for
        // the deferred note on chosen-X cost calc.
        var self = card;
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: spell =>
            {
                if (self.IsTapped) return false;
                var printed = ManaCost.Parse(spell.ManaCost ?? "");
                if (printed.HasX) return false;
                return printed.TotalValue < 3;
            },
            extraGeneric: (spell, _) =>
            {
                if (self.IsTapped) return 0;
                var printed = ManaCost.Parse(spell.ManaCost ?? "");
                if (printed.HasX) return 0;
                var total = printed.TotalValue;
                return total < 3 ? 3 - total : 0;
            },
            description: "As long as Trinisphere is untapped, each spell that would cost less than three mana to cast costs three mana to cast."));

        return card;
    }
}
