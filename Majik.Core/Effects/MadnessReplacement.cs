using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.35 — Madness. "If you discard this card, discard it into exile.
/// When you do, you may cast it by paying its madness cost rather than putting
/// it into your graveyard." (CR 702.35a–c.)
///
/// <para>The discard → exile half is a replacement effect: when THIS card would
/// move from hand to graveyard (the engine's discard funnel — there is no
/// dedicated DiscardEvent in v1, so a Hand → Graveyard
/// <see cref="ZoneMoveIntent"/> IS a discard, the same funnel Necropotence's
/// "exile discarded cards" rides), rewrite the destination to
/// <see cref="ZoneType.Exile"/>. The subsequent "you may cast it for its
/// madness cost, else put it into the graveyard" window is driven by
/// <see cref="Keywords.MadnessHelper"/> after the move commits.</para>
///
/// <para>Self-scoped: this fires only for the card it was constructed with, so
/// each Madness card carries its own replacement. <c>OneShot = false</c> — the
/// card can be discarded again after returning to hand on a later turn.</para>
/// </summary>
public sealed class MadnessReplacement : IReplacementEffect<ZoneMoveIntent>
{
    private readonly ICard _card;

    public MadnessReplacement(ICard card)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Card, _card)
        && intent.FromZone == ZoneType.Hand
        && intent.ToZone == ZoneType.Graveyard;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        // CR 702.35b — discarded into exile instead of the graveyard.
        intent with { ToZone = ZoneType.Exile };
}
