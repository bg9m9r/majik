using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614 — "would be destroyed" intent. Regeneration (CR 701.18) and
/// Totem Armor (CR 702.124) replace this. State-based actions (CR 704.5g)
/// and direct destroy effects route the permanent through
/// <see cref="ReplacementBus"/> before commit; if the intent is cancelled
/// (returns null), the destroy is replaced by the regeneration shield's
/// "tap + remove damage + remove from combat" sub-effect.
/// </summary>
public sealed record DestroyIntent(Permanent Target);
