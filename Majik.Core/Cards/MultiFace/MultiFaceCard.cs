using Majik.Core.Cards;

namespace Majik.Core.Cards.MultiFace;

/// <summary>
/// Wrapper that carries one or more <see cref="IFaceTransform"/> plug-ins
/// for a single card. Exactly one transform may be active at a time
/// (CR 711.1, generalised — a card occupies one face). Attached to the
/// underlying card by the card's factory; the cast / activation pipeline
/// looks it up to drive Apply / Revert.
///
/// <para>
/// This is an opt-in surface: cards that need NO bistate-face mechanic
/// don't get one. Cards that need ONE attach a single-transform list.
/// Cards that conceivably need multiple (e.g. a hypothetical
/// "MDFC Adventure" — not printed at time of writing) can list several,
/// with the invariant that <see cref="ActiveTransform"/> is at most one.
/// </para>
/// </summary>
public sealed class MultiFaceCard
{
    private readonly List<IFaceTransform> _transforms;

    public ICard Card { get; }
    public IReadOnlyList<IFaceTransform> AvailableTransforms => _transforms;
    public IFaceTransform? ActiveTransform { get; private set; }

    public MultiFaceCard(ICard card, IEnumerable<IFaceTransform> transforms)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(transforms);
        Card = card;
        _transforms = new List<IFaceTransform>(transforms);
    }

    /// <summary>
    /// Apply <paramref name="t"/>. Must be one of the registered
    /// <see cref="AvailableTransforms"/>. If another transform is
    /// currently active, it is reverted first.
    /// </summary>
    public void Transform(IFaceTransform t, FaceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(ctx);
        if (!_transforms.Contains(t))
            throw new InvalidOperationException(
                $"Transform '{t.Name}' is not registered on card '{Card.Name}'.");

        if (ActiveTransform != null && !ReferenceEquals(ActiveTransform, t))
        {
            ActiveTransform.Revert(Card, ctx);
        }

        t.Apply(Card, ctx);
        ActiveTransform = t;
    }

    /// <summary>
    /// Revert the currently-active transform (if any) back to the
    /// printed face. No-op if no transform is active.
    /// </summary>
    public void Untransform(FaceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ActiveTransform == null) return;
        ActiveTransform.Revert(Card, ctx);
        ActiveTransform = null;
    }
}
