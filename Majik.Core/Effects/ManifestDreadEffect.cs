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

        // Wrap in a face-down 2/2 creature permanent.
        var wrapper = new ManifestedCreature(toManifest);
        wrapper.SetOwner(controller);
        wrapper.SetController(controller);
        // CR 302.1 — creatures entering the battlefield have summoning
        // sickness; wrapper inherits this from Permanent's ctor default.

        // If the underlying card is a creature, grant the "turn face up
        // for its mana cost" activated ability (CR 701.59c / CR 708.6).
        if (toManifest is Creature creatureUnderneath)
        {
            var manaCost = creatureUnderneath.ManaCost;
            var cost = string.IsNullOrWhiteSpace(manaCost)
                ? new ManaCostCost(Majik.Core.ValueObjects.ManaCost.Zero)
                : new ManaCostCost(manaCost);

            ManifestedCreature wrapperRef = wrapper;
            ZoneService? zonesRef = zones;
            var flipEffect = new Effect(
                $"Turn face up: restore {creatureUnderneath.Name}",
                () => wrapperRef.TryTurnFaceUp(zonesRef));

            var turnFaceUp = new FaceDownActivatedAbility(
                source: wrapper,
                controller: controller,
                costs: new ICost[] { cost },
                effects: new IEffect[] { flipEffect });

            wrapper.AddAbility(turnFaceUp);
        }

        // Underlying card is no longer in the library; place it in a
        // "limbo" exile slot under the wrapper until the wrapper flips
        // face-up. CR 708.2c — the face-down spell/permanent represents
        // the underlying card; the underlying card object is what we
        // swap onto the battlefield on flip. Until then we leave the
        // underlying card's Zone set to Exile (a sentinel — it isn't
        // actually publicly in exile, it's stashed under the wrapper)
        // so a stray SBA pass doesn't find it lingering in the library.
        toManifest.SetZone(ZoneType.Exile);

        // Put the wrapper onto the controller's battlefield.
        if (zones is not null)
        {
            // ZoneService.MoveCard expects the card to be in `fromZone`;
            // the wrapper isn't anywhere yet, so we sentinel it through
            // the library exactly like TokenFactory does for tokens.
            wrapper.SetZone(ZoneType.Library);
            controller.Zones.Library.AddCard(wrapper);
            zones.MoveCardTo(wrapper, ZoneType.Battlefield, controller);
        }
        else
        {
            wrapper.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(wrapper);
        }

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
