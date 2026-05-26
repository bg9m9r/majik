using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice two artifacts" — activated-ability cost (CR 117 / CR 701.16).
/// Picks two artifacts the controller controls, removes them from the
/// battlefield, and puts them into their owners' graveyards.
///
/// Implements <see cref="ICost"/> so it slots directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list — sibling
/// shape to <see cref="SacrificeAnArtifactCost"/>. Used by Sai, Master
/// Thopterist's draw-a-card activation ({2}, Sacrifice two artifacts:
/// Draw a card.).
///
/// CR 117.3 — costs must be paid in full; this cost rejects payment
/// (via <see cref="CanPay"/>) when fewer than two artifacts are on the
/// controller's battlefield (excluding the optional source).
///
/// ## Deferred (v1 gaps)
/// - <see cref="Targets"/> may be set by the agent before <see cref="Pay"/>
///   is called; otherwise the first two eligible artifacts are chosen
///   deterministically. Full agent-driven target prompting is the next
///   step — same gap as <see cref="SacrificeAnArtifactCost"/>.
/// - <b>Source exclusion</b>: callers that want a "sacrifice two artifacts
///   other than ~" shape should pass an <paramref name="excludeSource"/>
///   reference. Sai itself is NOT an artifact, so the default null is
///   correct — Sai cannot be picked even if the printed wording were
///   "any artifact you control".
/// </summary>
public sealed class SacrificeTwoArtifactsCost : ICost
{
    /// <summary>CR 701.16 — fixed count of artifacts to sacrifice.</summary>
    public const int Count = 2;

    private readonly Permanent? _excludeSource;

    /// <summary>
    /// Optionally set by the agent to indicate which two artifacts to
    /// sacrifice. When null the cost falls back to the first two eligible
    /// artifacts on the controller's battlefield (deterministic v1
    /// behaviour). After <see cref="Pay"/> returns successfully, this
    /// reflects the artifacts actually sacrificed so downstream effects
    /// can reference them.
    /// </summary>
    public IReadOnlyList<Permanent>? Targets { get; set; }

    /// <summary>
    /// Construct a "sacrifice two artifacts" cost. When
    /// <paramref name="excludeSource"/> is supplied, that permanent is
    /// excluded from the picker (use this when the ability text says
    /// "sacrifice two artifacts other than ~"). Default null — the
    /// source is eligible if it is itself an artifact.
    /// </summary>
    public SacrificeTwoArtifactsCost(Permanent? excludeSource = null)
    {
        _excludeSource = excludeSource;
    }

    public string Description =>
        _excludeSource == null
            ? "sacrifice two artifacts"
            : $"sacrifice two artifacts other than {_excludeSource.Name}";

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return EligibleArtifacts(player).Take(Count).Count() == Count;
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var picks = Targets?.ToList() ?? EligibleArtifacts(player).Take(Count).ToList();

        if (picks.Count < Count)
        {
            throw new InvalidOperationException(
                $"Cannot pay {Description}: only {picks.Count} eligible artifact(s) available.");
        }

        foreach (var pick in picks)
        {
            player.Zones.Battlefield.RemoveCard(pick);
            player.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
        }

        Targets = picks;
    }

    private IEnumerable<Permanent> EligibleArtifacts(Player player) =>
        player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.HasType(CardType.Artifact)
                     && (_excludeSource == null || !ReferenceEquals(p, _excludeSource)));
}
