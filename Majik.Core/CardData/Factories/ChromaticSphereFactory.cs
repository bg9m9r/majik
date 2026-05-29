using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Chromatic Sphere (Mirrodin / Fifth Dawn et al.,
/// {1}).
///
/// Artifact. Oracle text:
///   "{1}, {T}, Sacrifice this artifact: Add one mana of any color.
///    Draw a card."
///
/// Functionally near-identical to <see cref="ChromaticStarFactory"/>
/// ("{T}, Sacrifice this artifact: Add one mana of any color"). The two
/// differences are:
///   1. The Sphere's activation cost includes {1} (the Star is free to
///      activate).
///   2. The Sphere draws a card as part of activating its ability, whereas
///      the Star draws on a leaves-the-battlefield trigger.
///
/// <b>Why the draw lives in the activation closure, not on the stack:</b>
/// CR 605.1a — an activated ability is a mana ability if it could add mana
/// to a player's mana pool as it resolves, doesn't require a target, and
/// isn't a loyalty ability. Chromatic Sphere's ability meets all three
/// (it adds one mana of any colour, has no target, isn't loyalty), so it
/// IS a mana ability. CR 605.3 — mana abilities resolve immediately and
/// never use the stack, so the "Draw a card" rider resolves atomically
/// with the mana production. We therefore fold the draw into the same
/// activation step as the cost payment + mana generation, exactly the
/// posture the engine already uses for additional-cost mana abilities
/// (Horizon Canopy "Pay 1 life", the filter-land "{1}" cost).
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{1}, {T}, Sacrifice this: Add one mana of any color. Draw a card</b>
///   — five <see cref="ManaAbility"/> instances (one per WUBRG), same modal
///   fan-out as <see cref="ChromaticStarFactory"/> / <see cref="LotusPetalFactory"/>.
///   Each uses the (source, controller, manaGenerated, canActivateCheck,
///   additionalCostPayer) overload:
///     - <c>canActivateCheck</c> = <c>!IsTapped AND Zone == Battlefield AND
///       controller can pay {1}</c> (gates the once-only activation and the
///       {1} affordability — mirrors <see cref="FilterLandCycleFactory"/>).
///     - <c>additionalCostPayer</c> pays {1} from the pool, performs the
///       sacrifice (CR 701.16) inline, and draws a card (the cantrip).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: mirrors Chromatic Star / Lotus
///   Petal — the engine's generic <see cref="Majik.Core.Costs.AdditionalCost"/>
///   sacrifice path is a no-op stub today, so the activation closure performs
///   the zone move directly.
/// - <b>{1} auto-fixing</b>: activation requires {1} to already be in the
///   mana pool; the engine doesn't auto-tap other sources to feed the cost.
///   Same posture as the filter-land cycle and every other additional-mana-
///   cost activated ability (Mind Stone's draw cost, Springleaf Drum, …).
/// - <b>Single modal-colour mana ability</b>: "Add one mana of any color"
///   is bound as five separate <see cref="ManaAbility"/> instances — the
///   bot's source-picker selects the right colour at payment time. Same
///   posture as Chromatic Star / Lotus Petal / Mox Opal.
/// </summary>
[CardName("Chromatic Sphere")]
public static class ChromaticSphereFactory
{
    public const string CardName = "Chromatic Sphere";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Chromatic Sphere owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var sphere = new Artifact(CardName, PrintedManaCost);
        sphere.SetOwner(owner);
        sphere.SetController(owner);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice this artifact: Add one mana of any color.
        // Draw a card.
        //
        // Five ManaAbility instances (one per WUBRG) — same fan-out as
        // Chromatic Star / Lotus Petal. Each is gated on:
        //   (1) Chromatic Sphere is untapped,
        //   (2) Chromatic Sphere is still on the battlefield, AND
        //   (3) the controller can pay {1} from their mana pool.
        // The additionalCostPayer pays {1} (CR 601.2g-h cost payment),
        // sacrifices the sphere (CR 701.16), and draws a card — the
        // cantrip resolves with the mana ability (CR 605.3, no stack).
        // ----------------------------------------------------------------
        var oneGeneric = ManaCost.Parse("1");

        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            sphere.AddAbility(new ManaAbility(
                source: sphere,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !sphere.IsTapped
                                        && sphere.Zone == ZoneType.Battlefield
                                        && owner.ManaPool.CanPay(oneGeneric),
                additionalCostPayer: payer => PayActivationCostAndCantrip(sphere, owner, payer, oneGeneric)));
        }

        return sphere;
    }

    /// <summary>
    /// Pay the non-{T} portion of the activation cost and resolve the
    /// cantrip, atomically with mana generation:
    /// <list type="number">
    ///   <item>Pay {1} from the activator's mana pool (CR 601.2h).</item>
    ///   <item>Sacrifice the sphere — battlefield → owner's graveyard
    ///     (CR 701.16). Idempotent against double-execution.</item>
    ///   <item>Draw a card (CR 605.3 — the mana ability's "Draw a card"
    ///     rider resolves immediately, off-stack).</item>
    /// </list>
    /// </summary>
    private static void PayActivationCostAndCantrip(
        Artifact sphere, Player owner, Player payer, ManaCost oneGeneric)
    {
        // CR 601.2h — pay the {1} activation cost.
        payer.PayMana(oneGeneric);

        // CR 701.16 — sacrifice: controller moves the sphere from the
        // battlefield to its owner's graveyard. Idempotent (the
        // canActivateCheck gate makes a second entry unreachable in
        // practice; the guard is belt-and-braces against a sibling
        // colour-ability re-entry within the same step).
        if (sphere.Zone == ZoneType.Battlefield)
        {
            var controller = sphere.Controller ?? owner;
            controller.Zones.Battlefield.RemoveCard(sphere);
            owner.Zones.Graveyard.AddCard(sphere);
            sphere.SetZone(ZoneType.Graveyard);
        }

        // CR 605.3 — the cantrip resolves with the mana ability (no stack).
        Fx.DrawCards(owner, 1);
    }
}
