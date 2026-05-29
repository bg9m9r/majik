using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pentad Prism (Fifth Dawn, {2}).
///
/// Artifact. Oracle text (Scryfall, verified):
///   "Sunburst (This artifact enters with a charge counter on it for each
///    color of mana spent to cast it.)"
///   "Remove a charge counter from this artifact: Add one mana of any color."
///
/// ## Implemented (v1)
/// - Artifact {2} with owner / controller wired.
/// - <b>Sunburst (CR 702.44)</b> wired via the shared
///   <see cref="SunburstFactory.Build"/> primitive. Pentad Prism is a
///   non-creature artifact, so Sunburst lands <see cref="CounterType.Charge"/>
///   counters at ETB (CR 702.44a — non-creature branch). The keyword reads
///   <see cref="Card.PendingCastColors"/> stamped by
///   <see cref="Majik.Core.Game.TurnDriver"/> after the mana resolver
///   computes "colors of mana spent" from the cross-spend pool diff. When
///   wired against the supplied <paramref name="replacements"/> bus,
///   Hardened Scales / Doubling Season bumps apply. Same posture as
///   <see cref="EtchedOracleFactory"/> / <see cref="EngineeredExplosivesFactory"/>.
/// - <b>"Remove a charge counter: Add one mana of any color"</b> (CR 605.1)
///   — five <see cref="ManaAbility"/> instances (one per WUBRG), same modal
///   colour shape as <see cref="ChromaticStarFactory"/> / Lotus Petal /
///   Mox Opal. Each uses the no-{T} mana-ability overload
///   (<c>tapsAsCost: false</c>, the Wall of Roots shape): the printed cost
///   is "remove a charge counter", NOT {T}, so the prism stays untapped
///   and can be activated as many times as it has charge counters.
///     - <c>canActivateCheck</c> = <c>Counters.Count(Charge) &gt; 0 AND
///       Zone == Battlefield</c> (the cost can only be paid while a charge
///       counter is present — CR 605.3a).
///     - <c>additionalCostPayer</c> removes one charge counter inline
///       (CR 121.5 / CR 602.1 — the cost is paid up front in the same
///       atomic step as the mana production; mana abilities don't use the
///       stack, CR 605.3b).
///
/// ## Deferred (v1 gaps)
/// - <b>Single modal-colour mana ability</b>: "Add one mana of any color"
///   is bound as five separate <see cref="ManaAbility"/> instances — the
///   bot's source-picker selects the right colour at payment time. Same
///   posture as Chromatic Star / Lotus Petal / Mox Opal / City of Brass.
/// - <b>Charge-counter-removal additional cost</b>: the removal is
///   performed by the mana ability's <c>additionalCostPayer</c> closure
///   rather than a declared <c>AdditionalCost.RemoveCounters</c> primitive
///   (which does not exist yet — same posture
///   <see cref="EtchedOracleFactory"/> and
///   <see cref="EngineeredExplosivesFactory"/> take for their counter /
///   sacrifice costs). When that primitive lands, the inline removal can
///   be hoisted to the declared cost list so cost-validation scans see it.
/// - <b>Live ETB-trigger registration</b>: the Sunburst ETB ability is
///   attached to <see cref="Card.Abilities"/> for shape inspection; the
///   shared primitive's trigger fires off the centralised ETB event when a
///   live <see cref="TriggerManager"/> observes it (same wiring path every
///   other <see cref="SunburstFactory"/> client uses).
/// </summary>
[CardName("Pentad Prism")]
public static class PentadPrismFactory
{
    public const string CardName = "Pentad Prism";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Construct Pentad Prism with no live replacement-bus wiring. Sunburst
    /// charge counters arrive via the non-creature branch when
    /// <see cref="Card.PendingCastColors"/> is set; counter placement falls
    /// through to a direct add (no Hardened Scales / Doubling Season bump).
    /// Suitable for shape / <see cref="NamedCardFactory"/> dispatch tests.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Pentad Prism. When <paramref name="replacements"/> is
    /// supplied, Sunburst's charge-counter placement routes through
    /// <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season bumps apply.
    /// </summary>
    public static Artifact Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var prism = new Artifact(CardName, PrintedManaCost);
        prism.SetOwner(owner);
        prism.SetController(owner);

        // ----------------------------------------------------------------
        // Sunburst (CR 702.44) — shared primitive. Pentad Prism is a
        // non-creature artifact, so the ETB effect reads PendingCastColors
        // at resolve time and lands ONE charge counter per distinct colour
        // of mana spent (CR 702.44a — non-creature branch). Routes through
        // CountersService.Add so Hardened Scales bumps the count.
        // ----------------------------------------------------------------
        SunburstFactory.Build(prism, replacements);

        // ----------------------------------------------------------------
        // Remove a charge counter from this artifact: Add one mana of any
        // color. (CR 605.1 — mana ability; CR 605.3b — doesn't use the
        // stack.)
        //
        // Five ManaAbility instances (one per WUBRG) — same modal-colour
        // shape as Chromatic Star / Lotus Petal / Mox Opal. The activation
        // cost is "remove a charge counter", NOT {T}, so we use the no-tap
        // overload (tapsAsCost: false, the Wall of Roots shape). Each is
        // gated on:
        //   (1) the prism is still on the battlefield, AND
        //   (2) the prism has at least one charge counter to remove
        //       (CR 605.3a — the cost must be payable).
        // The additionalCostPayer removes one charge counter inline.
        // ----------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            prism.AddAbility(new ManaAbility(
                source: prism,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => prism.Zone == ZoneType.Battlefield
                                        && prism.Counters.Count(CounterType.Charge) > 0,
                additionalCostPayer: _ => RemoveOneChargeCounter(prism),
                tapsAsCost: false));
        }

        return prism;
    }

    /// <summary>
    /// CR 121.5 / CR 602.1 — pay the activation cost by removing one charge
    /// counter from the prism. Defensive against an empty pool (the
    /// canActivateCheck gate makes that unreachable in practice).
    /// </summary>
    private static void RemoveOneChargeCounter(Artifact prism)
    {
        if (prism.Counters.Count(CounterType.Charge) <= 0) return;
        prism.Counters.Remove(CounterType.Charge, 1);
    }
}
