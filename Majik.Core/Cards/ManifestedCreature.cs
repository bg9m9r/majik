using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// CR 701.31 (Manifest) / CR 701.59 (Manifest dread) / CR 708.2 —
/// face-down 2/2 creature permanent that wraps an underlying card.
///
/// <para>
/// When a card is manifested, it goes onto the battlefield face-down. A
/// face-down permanent is a 2/2 colourless creature with no name, mana
/// cost, card types other than creature, or abilities (CR 708.2). The
/// underlying card's printed identity is hidden from the engine while
/// the wrapper is on the battlefield. Turn face up (CR 708.6) is
/// modelled as a battlefield <em>swap</em> from this wrapper to the
/// <see cref="UnderlyingCard"/> — preserves the simple "card identity
/// is immutable" invariant on <see cref="Card"/> while still surfacing
/// the underlying creature's printed name / mana cost / abilities once
/// the player has paid to turn it face-up.
/// </para>
///
/// <para>
/// <b>Turn face up (CR 708.6).</b> Granted as a
/// <see cref="Majik.Core.Abilities.FaceDownActivatedAbility"/> only when
/// the underlying card is a creature (CR 701.59c —
/// "you may turn it face up any time for its mana cost if it's a
/// creature card"). Non-creature underlying cards have no face-up
/// ability granted; they remain face-down 2/2 creatures.
/// </para>
/// </summary>
public sealed class ManifestedCreature : Creature
{
    /// <summary>
    /// The card that was manifested. While the wrapper is face-down on
    /// the battlefield, the engine treats this wrapper as the visible
    /// permanent and ignores <see cref="UnderlyingCard"/>;
    /// <see cref="TryTurnFaceUp"/> swaps this wrapper out for the
    /// underlying card via <see cref="ZoneService"/> on resolution.
    /// </summary>
    public ICard UnderlyingCard { get; }

    /// <summary>
    /// Construct a face-down 2/2 manifested creature wrapping
    /// <paramref name="underlying"/>. The wrapper starts face-down.
    /// Owner/controller/zone wiring is the caller's responsibility
    /// (typically <see cref="Majik.Core.Effects.ManifestDreadEffect"/>).
    /// </summary>
    /// <summary>
    /// Sentinel name surfaced by face-down manifested permanents. CR
    /// 708.2 — the permanent has no name; this is an internal label
    /// so the engine's name-required <see cref="Card"/> ctor accepts
    /// the wrapper. Tribal / name-matters effects MUST NOT match
    /// against this sentinel — the convention is to skip face-down
    /// permanents from name-matters lookups.
    /// </summary>
    public const string FaceDownName = "(face-down manifested creature)";

    public ManifestedCreature(ICard underlying)
        : base(
            name: FaceDownName,
            manaCost: string.Empty,
            power: 2,
            toughness: 2)
    {
        UnderlyingCard = underlying ?? throw new ArgumentNullException(nameof(underlying));
        IsToken = false;
        MarkFaceDown();
    }

    /// <summary>
    /// CR 708.6 — turn this manifested permanent face-up. Legal only
    /// when the underlying card is a <see cref="Creature"/>
    /// (CR 701.59c). On success, removes this wrapper from the
    /// controller's battlefield and places the underlying creature in
    /// its slot, transferring controller. Returns the underlying
    /// creature now on the battlefield on success, or null on no-op
    /// (already face-up / underlying is not a creature).
    /// </summary>
    /// <param name="zones">
    /// Optional <see cref="ZoneService"/>; when supplied the swap is
    /// routed through it so ETB / LTB events fire and replacement
    /// effects can rewrite the move. Otherwise a raw-zone fallback is
    /// used (no events).
    /// </param>
    public Creature? TryTurnFaceUp(ZoneService? zones = null)
    {
        if (!IsFaceDown) return null;
        if (UnderlyingCard is not Creature creatureUnderneath) return null;

        var controller = Controller ?? Owner;
        if (controller is null) return null;

        // Mark flag off first so any zone-event listeners that consult
        // IsFaceDown see the face-up state during the swap.
        TurnFaceUp();

        if (zones is not null)
        {
            zones.MoveCard(this, ZoneType.Battlefield, ZoneType.Exile, Owner ?? controller);
            // Place underlying creature on the controller's battlefield.
            creatureUnderneath.SetOwner(Owner ?? controller);
            creatureUnderneath.SetController(controller);
            controller.Zones.Battlefield.AddCard(creatureUnderneath);
            creatureUnderneath.SetZone(ZoneType.Battlefield);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(this);
            this.SetZone(ZoneType.Exile);
            creatureUnderneath.SetOwner(Owner ?? controller);
            creatureUnderneath.SetController(controller);
            controller.Zones.Battlefield.AddCard(creatureUnderneath);
            creatureUnderneath.SetZone(ZoneType.Battlefield);
        }

        return creatureUnderneath;
    }
}
