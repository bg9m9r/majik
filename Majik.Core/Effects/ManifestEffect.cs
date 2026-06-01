using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 701.31 — Manifest. Printed on cards such as Reality Shift:
/// <blockquote>
///   That player puts the top card of their library onto the
///   battlefield face down as a 2/2 creature. If it's a creature card,
///   it can be turned face up any time for its mana cost.
/// </blockquote>
///
/// <para>
/// Manifest is the single-card sibling of manifest dread (CR 701.59,
/// <see cref="ManifestDreadEffect"/>): no "look at the top two / one to
/// the graveyard" step — the controller simply puts the top card of
/// their library onto the battlefield face down as a 2/2.
/// </para>
///
/// <para>
/// <b>Face-down primitive.</b> Manifested cards become
/// <see cref="ManifestedCreature"/> wrappers — 2/2 face-down creatures
/// that keep a reference to the underlying card. If the underlying card
/// is a creature, a <see cref="FaceDownActivatedAbility"/> is granted on
/// the wrapper carrying the underlying creature's printed mana cost; on
/// activation it swaps the wrapper out for the underlying creature
/// (CR 708.6 / CR 701.31c). This is the same wrapper + turn-face-up
/// shape manifest dread already ships — only the library-look step
/// differs, so the wrapper-construction logic is shared via
/// <see cref="ManifestCard"/>.
/// </para>
///
/// <para>
/// <b>Empty library.</b> Clean no-op — the effect just does nothing,
/// mirroring how manifest dread / Scry / Mill handle empty libraries
/// (no empty-library loss is stamped here; that is the draw-from-empty
/// SBA's job, not manifest's).
/// </para>
/// </summary>
public static class ManifestEffect
{
    /// <summary>
    /// CR 701.31 — <paramref name="controller"/> manifests the top card
    /// of their library. Routes zone moves through
    /// <paramref name="zones"/> when supplied so ETB triggers and
    /// replacement effects fire; otherwise falls back to raw-zone moves.
    /// Returns the wrapper put onto the battlefield, or null when the
    /// controller's library was empty.
    /// </summary>
    public static ManifestedCreature? Resolve(Player controller, ZoneService? zones = null)
    {
        if (controller is null) throw new ArgumentNullException(nameof(controller));

        // Top of library = index 0 (matches Fx.LookAtTopN). Peek one.
        var top = Fx.LookAtTopN(controller, 1);
        if (top.Count == 0) return null;

        var toManifest = top[0];
        controller.Zones.Library.RemoveCard(toManifest);

        return ManifestCard(controller, toManifest, zones);
    }

    /// <summary>
    /// Shared wrapper-construction for manifest (CR 701.31) and manifest
    /// dread (CR 701.59): wrap <paramref name="card"/> — which has
    /// already been removed from its previous zone — in a face-down 2/2
    /// <see cref="ManifestedCreature"/> under
    /// <paramref name="controller"/>, granting the "turn face up for its
    /// mana cost" activated ability (CR 708.6) iff the underlying card is
    /// a creature (CR 701.31c / CR 701.59c), and put the wrapper onto
    /// <paramref name="controller"/>'s battlefield.
    /// </summary>
    internal static ManifestedCreature ManifestCard(
        Player controller,
        ICard card,
        ZoneService? zones)
    {
        // Wrap in a face-down 2/2 creature permanent.
        var wrapper = new ManifestedCreature(card);
        wrapper.SetOwner(controller);
        wrapper.SetController(controller);
        // CR 302.1 — creatures entering the battlefield have summoning
        // sickness; wrapper inherits this from Permanent's ctor default.

        // If the underlying card is a creature, grant the "turn face up
        // for its mana cost" activated ability (CR 701.31c / CR 708.6).
        if (card is Creature creatureUnderneath)
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

        // Underlying card is no longer in the library; stash it under the
        // wrapper (sentinel Exile zone) until the wrapper flips face-up.
        // CR 708.2c — the face-down permanent represents the underlying
        // card; the underlying card object is what we swap onto the
        // battlefield on flip. Setting Zone to Exile keeps a stray SBA
        // pass from finding it lingering in the library.
        card.SetZone(ZoneType.Exile);

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

        return wrapper;
    }
}
