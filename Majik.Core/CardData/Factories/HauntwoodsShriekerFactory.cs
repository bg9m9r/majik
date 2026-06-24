using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hauntwoods Shrieker (Duskmourn: House of Horror,
/// {1}{G}{G}).
///
/// Creature — Beast Mutant 3/3. Oracle text (verified against the embedded
/// Modern seed, 2026-06-24):
///   "Whenever this creature attacks, manifest dread. (Look at the top two
///    cards of your library. Put one onto the battlefield face down as a 2/2
///    creature and the other into your graveyard. Turn it face up any time for
///    its mana cost if it's a creature card.)
///    {1}{G}: Reveal target face-down permanent. If it's a creature card, you
///    may turn it face up."
///
/// ## Shape source
///
/// Card identity (name, {1}{G}{G}, 3/3, Creature — Beast Mutant) is loaded from
/// <c>Majik.Core/CardData/Cards/hauntwoods-shrieker.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The attack trigger and the activated
/// reveal/turn-face-up ability are attached in code below — same posture as
/// <see cref="CogworkWrestlerFactory"/> (JSON shape + code-attached targeted
/// ability) and <see cref="AbhorrentOculusFactory"/> (manifest dread via
/// <see cref="ManifestDreadEffect.Resolve"/>).
///
/// ## Implemented (v1)
///
/// - <b>3/3 Creature — Beast Mutant at {1}{G}{G}</b> (from JSON).
/// - <b>"Whenever this creature attacks, manifest dread." (CR 508.1f attack
///   trigger / CR 701.59 manifest dread)</b>: wired via
///   <see cref="Triggers.OnAttackSelf"/>. On resolution it runs the real
///   <see cref="ManifestDreadEffect.Resolve"/> for the Shrieker's live
///   controller — look at the top two of that player's library, manifest the
///   first as a face-down 2/2 <see cref="ManifestedCreature"/> on the
///   battlefield, and put the second into the graveyard. Routes through the
///   supplied <see cref="ZoneService"/> when given so ETB / LTB triggers fire.
/// - <b>"{1}{G}: Reveal target face-down permanent. If it's a creature card,
///   you may turn it face up." (CR 602.5 activated / CR 708.6 turn face up)</b>:
///   an <see cref="ActivatedAbility"/> carrying a 1..1
///   <see cref="TargetRequest"/> for a face-down permanent. On resolution the
///   chosen target is re-validated (still on the battlefield AND still
///   face-down — CR 608.2b), "revealed" (a no-op in the headless engine — there
///   is no hidden-information surface to flip), and, if the underlying card is a
///   creature, turned face up via
///   <see cref="ManifestedCreature.TryTurnFaceUp"/> (CR 708.6). A non-creature
///   underlying card (or a non-manifested face-down permanent whose underlying
///   identity the engine can't expose) is a clean no-op — matching the printed
///   "if it's a creature card" gate. "May" is modelled as always-yes in v1
///   (no agent prompt); turning your own / an opponent's manifested creature
///   face up is strictly upside, so auto-yes is sound.
///
/// ## Deferred (v1 gaps — small)
///
/// - <b>Agent prompt for the "you may" choice:</b> v1 always turns a creature
///   target face up. A future agent-prompt hookup (mirror of other optional
///   abilities) can let the controller decline.
/// - <b>Manifest-dread pick-one-of-two:</b> inherited from
///   <see cref="ManifestDreadEffect"/> — v1 deterministically manifests the
///   top-of-library card; the second goes to the graveyard.
/// - <b>Shape-only single-arg overload:</b> the manifest-dread trigger uses a
///   raw-zone fallback when no <see cref="ZoneService"/> is supplied (same
///   posture as <see cref="AbhorrentOculusFactory"/>).
///
/// CR rule references: 205.3 (Beast / Mutant subtypes), 508.1f (attack
/// trigger), 701.59 (manifest dread), 602.5 (activated ability), 708.2 / 708.6
/// (face-down permanents + turn face up), 608.2b (illegal-target check on
/// resolution).
/// </summary>
[CardName("Hauntwoods Shrieker")]
public static class HauntwoodsShriekerFactory
{
    public const string CardName = "Hauntwoods Shrieker";
    public const string RevealCost = "{1}{G}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("hauntwoods-shrieker");

    /// <summary>
    /// Construct Hauntwoods Shrieker with no live service wiring (the shape /
    /// dispatcher path). The attack trigger is attached to the card shape; the
    /// activated reveal ability is attached unconditionally. Manifest dread
    /// uses a raw-zone fallback (no <see cref="ZoneService"/>).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Construct Hauntwoods Shrieker owned and controlled by
    /// <paramref name="owner"/>. When <paramref name="triggers"/> is supplied
    /// the attack trigger is registered with the <see cref="TriggerManager"/>
    /// so it surfaces on the stack when the Shrieker attacks. When
    /// <paramref name="zones"/> is supplied, manifest dread's zone moves route
    /// through the <see cref="ZoneService"/> (ETB / LTB triggers fire);
    /// otherwise raw-zone moves are used.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers = null,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Whenever this creature attacks, manifest dread." CR 508.1f
        // (per-attacker attack trigger) / CR 701.59 (manifest dread).
        // Capture `card` so we read the LIVE controller at resolve time
        // (handles control changes between attack + resolution); resolve
        // manifest dread for the Shrieker's controller, not the defending
        // player.
        // ----------------------------------------------------------------
        var capturedZones = zones;
        var manifestDreadEffect = new Effect(
            $"{CardName}: manifest dread (CR 701.59)",
            () => ManifestDreadEffect.Resolve(card.Controller ?? owner, capturedZones));

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { manifestDreadEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // ----------------------------------------------------------------
        // "{1}{G}: Reveal target face-down permanent. If it's a creature
        // card, you may turn it face up." CR 602.5 (activated) / CR 708.6
        // (turn face up).
        //
        // Resolution reads the chosen target, re-validates it is still on
        // the battlefield AND still face-down (CR 608.2b), then turns it
        // face up iff the underlying card is a creature — TryTurnFaceUp is
        // itself gated on "underlying is a Creature" (CR 701.59c / 708.6),
        // so a non-creature underlying (or a face-down permanent whose
        // underlying identity the engine can't expose) is a clean no-op.
        // "Reveal" has no headless-engine side effect (no hidden-info
        // surface to flip); the turn-face-up is the observable outcome.
        // ----------------------------------------------------------------
        ActivatedAbility? revealAbility = null;
        var revealEffect = new Effect(
            $"{CardName}: reveal target face-down permanent, may turn face up (CR 708.6)",
            () =>
            {
                if (revealAbility is null
                    || revealAbility.ChosenTargets.Count == 0
                    || revealAbility.ChosenTargets[0].Count == 0)
                {
                    return;
                }

                if (revealAbility.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-target check on resolution. Target
                // must still be on the battlefield AND still face-down.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.IsFaceDown) return;

                // "If it's a creature card, you may turn it face up." Only
                // manifested/cloaked wrappers expose the underlying card to
                // the engine; TryTurnFaceUp is gated on a creature underlying
                // (CR 708.6) and no-ops otherwise. "May" = always-yes in v1.
                if (target is ManifestedCreature wrapper)
                {
                    wrapper.TryTurnFaceUp(capturedZones);
                }
            });

        revealAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(RevealCost) },
            effects: new IEffect[] { revealEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target face-down permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(revealAbility);

        return card;
    }
}
