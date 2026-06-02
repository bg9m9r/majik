using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "Sacrifice an artifact" — activated-ability cost (CR 117 / CR 701.16).
/// Picks an artifact the controller controls, removes it from the
/// battlefield, and puts it into its owner's graveyard.
///
/// Implements <see cref="ICost"/> so it slots directly into an
/// <see cref="Majik.Core.Abilities.ActivatedAbility"/> cost list — sister
/// shape to <see cref="SacrificeAnotherCreatureCost"/>. The spell-level
/// <see cref="SacrificeAnArtifactAdditionalCost"/> covers
/// <see cref="IAdditionalCost"/> consumers (additional-cost spells like
/// Cabal Therapy's flashback rider).
///
/// ## Deferred (v1 gaps)
/// - <see cref="Target"/> may be set by the agent before <see cref="Pay"/>
///   is called; otherwise the first eligible artifact is chosen
///   deterministically. Full agent-driven target prompting is the next
///   step — same gap as <see cref="SacrificeAnotherCreatureCost"/>.
/// - <b>Self-sacrifice</b>: the picker does NOT exclude the ability's
///   source — Arcbound Ravager's "Sacrifice an artifact" can pay itself
///   when no other artifact is available (Arcbound Ravager is an
///   artifact). Callers that want a "sacrifice another artifact" shape
///   should pass an <paramref name="excludeSource"/> reference.
/// - <b>Nontoken rider</b>: pass <c>requireNontoken: true</c> to restrict
///   the picker to nontoken artifacts (CR 111.8 — Thopter Foundry's
///   "Sacrifice a nontoken artifact"). The token check reads
///   <see cref="Permanent.IsToken"/>.
/// </summary>
public sealed class SacrificeAnArtifactCost : ICost
{
    private readonly Permanent? _excludeSource;
    private readonly bool _requireNontoken;

    /// <summary>
    /// Optionally set by the agent to indicate which artifact to
    /// sacrifice. When null the cost falls back to the first eligible
    /// artifact on the controller's battlefield (deterministic v1
    /// behaviour). After <see cref="Pay"/> returns successfully, this
    /// reflects the artifact actually sacrificed so downstream effects
    /// can reference it (mana value, types, etc.).
    /// </summary>
    public Permanent? Target { get; set; }

    /// <summary>
    /// Construct a "sacrifice an artifact" cost. When
    /// <paramref name="excludeSource"/> is supplied, that permanent is
    /// excluded from the picker (use this when the ability text says
    /// "sacrifice an artifact other than ~"). Default null — the source
    /// is eligible (Arcbound Ravager — "Sacrifice an artifact" picks
    /// itself when no other artifact is available).
    /// When <paramref name="requireNontoken"/> is true, token artifacts
    /// are excluded from the picker (CR 111.8 — Thopter Foundry's
    /// "Sacrifice a nontoken artifact"); default false.
    /// </summary>
    public SacrificeAnArtifactCost(Permanent? excludeSource = null, bool requireNontoken = false)
    {
        _excludeSource = excludeSource;
        _requireNontoken = requireNontoken;
    }

    public string Description =>
        _requireNontoken
            ? (_excludeSource == null
                ? "sacrifice a nontoken artifact"
                : $"sacrifice a nontoken artifact other than {_excludeSource.Name}")
            : (_excludeSource == null
                ? "sacrifice an artifact"
                : $"sacrifice an artifact other than {_excludeSource.Name}");

    private bool IsEligible(Permanent p) =>
        p.HasType(CardType.Artifact)
        && (_excludeSource == null || !ReferenceEquals(p, _excludeSource))
        && (!_requireNontoken || !p.IsToken);

    /// <inheritdoc/>
    public bool CanPay(Player player)
    {
        if (player == null) return false;
        return player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(IsEligible);
    }

    /// <inheritdoc/>
    public void Pay(Player player)
    {
        if (player == null) throw new ArgumentNullException(nameof(player));

        var pick = Target ?? player.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(IsEligible);

        if (pick == null)
            throw new InvalidOperationException(
                $"Cannot pay {Description}: no eligible artifact to sacrifice.");

        player.Zones.Battlefield.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        Target = pick;
    }
}
