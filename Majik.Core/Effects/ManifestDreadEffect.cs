using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 701.59 — Manifest dread. Printed on Duskmourn cards (Abhorrent
/// Oculus, etc.):
/// <blockquote>
///   Look at the top two cards of your library. Put one onto the
///   battlefield face down as a 2/2 creature and the other into your
///   graveyard. Turn it face up any time for its mana cost if it's a
///   creature card.
/// </blockquote>
///
/// <para>
/// <b>Pick semantics.</b> v1 deterministic: the first of the two looked
/// at (top of library) is manifested; the second goes to the graveyard.
/// A future agent-prompt hookup (mirror of the Brainstorm / Ponder pick
/// loop) will let the controller's agent choose which goes where; the
/// effect surface (<see cref="Resolve"/>) is the swap point.
/// </para>
///
/// <para>
/// <b>Face-down primitive.</b> Manifested cards become
/// <see cref="ManifestedCreature"/> wrappers — 2/2 face-down creatures
/// that keep a reference to the underlying card. If the underlying
/// card is a creature, a <see cref="FaceDownActivatedAbility"/> is
/// granted on the wrapper carrying the underlying creature's printed
/// mana cost; on activation it swaps the wrapper out for the
/// underlying creature (CR 708.6).
/// </para>
///
/// <para>
/// <b>Library shape.</b> Top of library = index 0 (matches
/// <see cref="Fx.LookAtTopN"/>). One-card library still manifests that
/// one card and skips the graveyard step. Empty library is a clean
/// no-op (the effect just does nothing, mirroring how Mill / Scry
/// handle empty libraries — manifest dread doesn't stamp the
/// empty-library loss).
/// </para>
/// </summary>
public static class ManifestDreadEffect
{
    /// <summary>
    /// CR 701.59 — execute the manifest dread effect for
    /// <paramref name="controller"/>. Routes zone moves through
    /// <paramref name="zones"/> when supplied so ETB / LTB triggers and
    /// replacement effects fire; otherwise falls back to raw-zone moves.
    /// Returns the wrapper that was put onto the battlefield, or null
    /// when the controller's library was empty.
    /// </summary>
    public static ManifestedCreature? Resolve(Player controller, ZoneService? zones = null)
    {
        if (controller is null) throw new ArgumentNullException(nameof(controller));

        var top = Fx.LookAtTopN(controller, 2);
        if (top.Count == 0) return null;

        // v1: deterministic — first looked-at card is manifested.
        var toManifest = top[0];
        var toGraveyard = top.Count >= 2 ? top[1] : null;

        // Remove the manifested card from the library.
        controller.Zones.Library.RemoveCard(toManifest);

        // Wrap in a face-down 2/2 creature permanent and put it onto the
        // battlefield. Shared with plain manifest (CR 701.31) via
        // ManifestEffect.ManifestCard — same wrapper + turn-face-up shape.
        var wrapper = ManifestEffect.ManifestCard(controller, toManifest, zones);

        // Put the other looked-at card into the graveyard.
        if (toGraveyard is not null)
        {
            controller.Zones.Library.RemoveCard(toGraveyard);
            controller.Zones.Graveyard.AddCard(toGraveyard);
            toGraveyard.SetZone(ZoneType.Graveyard);
        }

        return wrapper;
    }
}
