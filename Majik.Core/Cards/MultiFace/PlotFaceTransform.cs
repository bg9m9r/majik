using System.Collections.Concurrent;
using Majik.Core.Cards;

namespace Majik.Core.Cards.MultiFace;

/// <summary>
/// Reference stub for <b>Plot</b> (CR 718) — demonstrates the
/// <see cref="IFaceTransform"/> plug-in shape with ~50 lines of state +
/// lifecycle. Intentionally <em>not</em> wired into the cast pipeline or
/// any factory in this PR; that's the follow-up.
///
/// <para>
/// Mechanic outline:
/// </para>
/// <list type="bullet">
/// <item><b>Apply</b> — caller has paid the plot cost out of hand; the
/// transform moves the card to exile (via <see cref="FaceContext"/> in
/// the real implementation) and stamps the plot marker. Idempotent.</item>
/// <item><b>Active state</b> — while the marker is set, the cast
/// pipeline grants the cast-from-exile-as-sorcery alt-cost (cost = 0)
/// on subsequent turns (the "may cast it on a later turn" gate is the
/// cast-flow's responsibility, not the transform's).</item>
/// <item><b>Revert</b> — clears the plot marker when the card resolves
/// or is otherwise removed. Idempotent.</item>
/// </list>
///
/// <para>
/// The marker is held in a side-table keyed by <see cref="ICard.InstanceId"/>
/// rather than a property on <see cref="Card"/> so this stub doesn't
/// require any churn on the Card aggregate. Real follow-up PRs will
/// promote the marker to a typed property when wiring the cast flow.
/// </para>
/// </summary>
public sealed class PlotFaceTransform : IFaceTransform
{
    private static readonly ConcurrentDictionary<Guid, bool> Plotted = new();

    public string Name => "Plot";

    public void Apply(ICard card, FaceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(card);
        // Idempotent: a card already plotted re-plots to itself.
        Plotted[card.InstanceId] = true;
        // Real implementation will:
        //   1. ctx.ZoneService.MoveToZone(card, exile, ctx.ActingPlayer)
        //   2. card.GrantRuntimeExileCast(...)  // sorcery-speed, zero-cost
        //   3. ctx.Game.EventBus.Publish(new CardPlottedEvent(card, ctx.ActingPlayer))
    }

    public void Revert(ICard card, FaceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(card);
        Plotted.TryRemove(card.InstanceId, out _);
        // Real implementation will clear the runtime exile-cast permission
        // and publish a CardUnplottedEvent.
    }

    public bool IsActive(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return Plotted.TryGetValue(card.InstanceId, out var v) && v;
    }
}
