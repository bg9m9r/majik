using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice two Foods" — activated-ability cost (CR 117 / CR 701.16).
/// Picks two Foods the controller controls, removes them from the
/// battlefield, and puts them into their owners' graveyards.
///
/// A Food is an artifact with the Food subtype (CR 205.3 — Food is an
/// artifact subtype). Sibling shape to <see cref="SacrificeTwoArtifactsCost"/>
/// (same fixed-count, pay-at-payment-time picker) but narrowed to the Food
/// filter. Used by Asmoranomardicadaistinaculdacar's
/// "Sacrifice two Foods: Target creature deals 6 damage to itself."
///
/// <para>CRITICAL — payment-time evaluation: <see cref="CanPay"/> reads the
/// LIVE battlefield, so the cost is correctly payable for Foods that entered
/// AFTER the ability's source was created (the prior implementation snapshotted
/// the two Foods at <c>Create</c> time, when typically zero Foods existed, and
/// so the cost was either empty / free or stale). CR 117.3 — costs must be
/// paid in full; this cost rejects payment (via <see cref="CanPay"/>) when
/// fewer than two Foods are on the controller's battlefield, making the
/// activation illegal (CR 602.5e).</para>
///
/// ## Deferred (v1 gaps)
/// - <see cref="Targets"/> may be set by the agent before <see cref="Pay"/>
///   is called; otherwise the first two eligible Foods are chosen
///   deterministically. Full agent-driven sacrifice prompting is the next
///   step — same gap as <see cref="SacrificeTwoArtifactsCost"/> /
///   <see cref="SacrificeFilteredCost"/>.
/// </summary>
public sealed class SacrificeTwoFoodsCost : ICost, IBusAwareCost
{
    /// <summary>CR 701.16 — fixed count of Foods to sacrifice.</summary>
    public const int Count = 2;

    private readonly IEventBus? _eventBus;

    /// <summary>
    /// Optionally set by the agent to indicate which two Foods to sacrifice.
    /// When null the cost falls back to the first two eligible Foods on the
    /// controller's battlefield (deterministic v1 behaviour). After
    /// <see cref="Pay"/> returns successfully, this reflects the Foods
    /// actually sacrificed so downstream effects can reference them.
    /// </summary>
    public IReadOnlyList<Permanent>? Targets { get; set; }

    /// <summary>
    /// Construct a "sacrifice two Foods" cost.
    /// </summary>
    /// <param name="eventBus">Optional event bus — publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) per sacrifice so
    /// aristocrat payoffs fire. Null preserves the legacy publish-nothing
    /// posture.</param>
    public SacrificeTwoFoodsCost(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <inheritdoc/>
    public string Description => "sacrifice two Foods";

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return EligibleFoods(player).Take(Count).Count() == Count;
    }

    /// <inheritdoc/>
    public void Pay(Player player) => Pay(player, _eventBus);

    /// <inheritdoc/>
    public void Pay(Player player, IEventBus? eventBus)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var picks = Targets?.Where(p => IsEligible(p, player)).Take(Count).ToList()
                    ?? EligibleFoods(player).Take(Count).ToList();

        if (picks.Count < Count)
        {
            throw new InvalidOperationException(
                $"Cannot pay {Description}: only {picks.Count} eligible Food(s) available.");
        }

        foreach (var pick in picks)
        {
            SacrificeCostHelper.Sacrifice(player, pick, eventBus);
        }

        Targets = picks;
    }

    private static bool IsEligible(Permanent p, Player player) =>
        p.Zone == ZoneType.Battlefield
        && ReferenceEquals(p.Controller, player)
        && p.HasType(CardType.Artifact)
        && p.HasSubtype(CardSubtype.Food);

    private static IEnumerable<Permanent> EligibleFoods(Player player) =>
        player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.HasType(CardType.Artifact) && p.HasSubtype(CardSubtype.Food));
}
