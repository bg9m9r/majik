using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Altar (Invasion, {3}).
///
/// Artifact. Oracle text:
///   "Sacrifice a creature: Add one mana of any color."
///
/// ## Implementation (v1)
/// - Artifact at {3}, owner/controller assigned.
/// - "Add one mana of any color" is modeled as five
///   <see cref="ManaAbility"/> instances (one per WUBRG) — same shape as
///   <see cref="MoxOpalFactory"/> / <see cref="ChromaticStarFactory"/> /
///   Delighted Halfling / City of Brass. Each ability uses the
///   no-tap-as-cost <c>ManaAbility</c> constructor (the printed cost is
///   "Sacrifice a creature" — there is no {T}) and pays the sacrifice via
///   the additional-cost payer. CR 605.1 — mana abilities don't use the
///   stack; the sacrifice happens as part of activation alongside the
///   mana production.
/// - The single shared <see cref="SacrificeAnotherCreatureCost"/> is
///   exposed as <see cref="PhyrexianAltarManaAbility.SacrificeChoice"/> on
///   each ability so a caller (test / bot) can pre-set
///   <c>SacrificeChoice.Target</c> before activation. Because Phyrexian
///   Altar is not itself a creature, "another" is vacuously satisfied —
///   same correctness posture as Goblin Bombardment.
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour mana ability</b>: five separate
///   ManaAbility instances (one per WUBRG) — the bot's source-picker
///   selects the right colour at payment time. A single ManaAbility with
///   "choose colour at activation" surface is not yet in the engine
///   (same pattern as Mox Opal / City of Brass).
/// - <b>Sacrifice target prompt</b>:
///   <see cref="SacrificeAnotherCreatureCost.Target"/> must be set by the
///   agent; v1 falls back to the first eligible creature (deterministic).
/// </summary>
[CardName("Phyrexian Altar")]
public static class PhyrexianAltarFactory
{
    public const string CardName = "Phyrexian Altar";
    public const string Cost = "{3}";

    /// <summary>
    /// Construct Phyrexian Altar owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var altar = new Artifact(CardName, Cost);
        altar.SetOwner(owner);
        altar.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice a creature: Add one mana of any color.
        // CR 605.1 — five tapless ManaAbility instances, each gated on the
        // controller having another creature available to sacrifice. The
        // SacrificeAnotherCreatureCost instance is shared across all five
        // abilities (one sacrifice produces one mana of the chosen colour
        // — only one of the five can be activated per payment).
        // ----------------------------------------------------------------
        var sacrificeCost = new SacrificeAnotherCreatureCost(altar);

        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            altar.AddAbility(new PhyrexianAltarManaAbility(
                source: altar,
                controller: owner,
                color: color,
                sacrificeCost: sacrificeCost));
        }

        return altar;
    }
}

/// <summary>
/// One of Phyrexian Altar's five mana abilities — pays the shared
/// <see cref="SacrificeAnotherCreatureCost"/> as an additional cost (no
/// tap) and produces one mana of a single colour. Subclasses
/// <see cref="ManaAbility"/> so the sacrifice cost is reachable from
/// outside for test / bot target-setting.
/// </summary>
public sealed class PhyrexianAltarManaAbility : ManaAbility
{
    /// <summary>
    /// The shared sacrifice cost paid as part of activating this ability.
    /// Set <see cref="SacrificeAnotherCreatureCost.Target"/> before
    /// activation to pick a specific creature; otherwise the cost falls
    /// back to its deterministic first-eligible pick.
    /// </summary>
    public SacrificeAnotherCreatureCost SacrificeChoice { get; }

    internal PhyrexianAltarManaAbility(
        Artifact source,
        Player controller,
        string color,
        SacrificeAnotherCreatureCost sacrificeCost)
        : base(
            source: source,
            controller: controller,
            manaGenerated: ManaCost.Parse(color),
            canActivateCheck: () => sacrificeCost.CanPay(controller),
            additionalCostPayer: p => sacrificeCost.Pay(p),
            tapsAsCost: false)
    {
        SacrificeChoice = sacrificeCost;
    }
}
