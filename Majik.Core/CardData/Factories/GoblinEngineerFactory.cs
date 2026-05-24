using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Engineer (Modern Horizons, {1}{R}).
///
/// Creature — Goblin Artificer 1/2. Oracle text:
///   "When Goblin Engineer enters, you may search your library for an
///    artifact card, then put that card into your graveyard. If you do,
///    shuffle."
///   "{R}, {T}, Sacrifice an artifact: Return target artifact card from
///    your graveyard to the battlefield."
///
/// ## Implemented (v1)
/// - 1/2 Goblin Artificer with mana cost {1}{R}.
/// - <b>ETB tutor (CR 603.1 / CR 701.19a)</b>: When Goblin Engineer enters,
///   the controller's library is scanned deterministically for the first
///   artifact card; if found, it is moved Library → Graveyard. Mirrors
///   <see cref="TrinketMageFactory"/>'s ETB-tutor shape but the destination
///   is the graveyard rather than the hand. The "may" is auto-taken when
///   an eligible card exists.
/// - <b>Activated ability (CR 113.3b)</b>: <c>{R}, {T}, Sacrifice an
///   artifact</c>. Mana cost is a <see cref="ManaCostCost"/>; tap is
///   <see cref="AdditionalCost.Tap"/>. "Sacrifice an artifact" is a
///   generic permanent-class cost (not the source), so it is NOT
///   represented as a structural <see cref="AdditionalCost.Sacrifice"/>
///   bound to a specific permanent at construction time — the engine
///   does not yet have a "sacrifice any one of N permanents" cost class.
///   Instead the sacrifice is performed at resolution time by the
///   effect body (deterministic first-match — same trick used by
///   EngineeredExplosives / MishrasBauble / PerniciousDeed where the
///   AdditionalCost.Sacrifice Pay is a TODO stub).
/// - <b>Resolution (reanimate, CR 608)</b>: returns the first artifact
///   card in the controller's graveyard to the battlefield under the
///   controller's control. Movement is routed through
///   <see cref="ZoneService.MoveCard"/> when a service is supplied so
///   reanimation ETB triggers fire (CR 603.6a); raw zone manipulation
///   otherwise.
///
/// ## Deferred (v1 gaps)
/// - <b>Target selection prompt</b>: both the sacrificed artifact and
///   the reanimated graveyard artifact are auto-picked (first
///   deterministic match). Real "Sacrifice an artifact" + "target
///   artifact card from your graveyard" prompts ship with the
///   agent-driven targeting MVP.
/// - <b>Library shuffle</b> (CR 701.19c): the ETB tutor skips the
///   shuffle (no IZone.Shuffle entry point yet; same rationale as
///   TrinketMage / GoblinMatron / StoneforgeMystic).
/// - <b>Reveal event</b>: the ETB tutor moves the artifact Library →
///   Graveyard without publishing a reveal event. Same gap as the
///   rest of the tutor surface.
/// - <b>"You may" opt-out</b>: the ETB tutor auto-takes when an
///   eligible artifact exists. A full implementation would prompt
///   the controller including the decline option.
/// - <b>Activate-only-as-a-sorcery</b>: Goblin Engineer's activated
///   ability does NOT have sorcery-speed restriction in printed
///   oracle text — it is an unrestricted activation. No deferral
///   needed here.
/// </summary>
[CardName("Goblin Engineer")]
public static class GoblinEngineerFactory
{
    /// <summary>
    /// Construct Goblin Engineer with no live runtime services (the
    /// shape / dispatcher path). The ETB trigger is attached but not
    /// registered; the activated ability is attached with its
    /// {R}, {T}, Sacrifice-an-artifact cost shape. The effect bodies
    /// use raw zone moves (no ZoneService) and the default first-match
    /// selectors for the sacrificed artifact and the reanimated
    /// graveyard artifact.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Goblin Engineer with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a <see cref="CardMovedEvent"/> to the battlefield
    /// places it on the stack automatically. When
    /// <paramref name="zoneService"/> is supplied, the activated
    /// ability's sacrificed-artifact move and the reanimation move
    /// are routed through <see cref="ZoneService.MoveCard"/> so ETB /
    /// dies triggers fire (CR 603.6a).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Goblin Engineer",
            manaCost: "{1}{R}",
            power: 1,
            toughness: 2,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Artificer });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1 / CR 701.19a.
        //   "When Goblin Engineer enters, you may search your library for
        //    an artifact card, then put that card into your graveyard. If
        //    you do, shuffle."
        // v1: deterministic — take the first artifact card in the library;
        // shuffle and reveal-event emission deferred (see class xmldoc).
        // Destination is graveyard (NOT hand) — distinguishes from
        // TrinketMageFactory / GoblinMatronFactory.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Goblin Engineer: tutor an artifact card from library to graveyard",
            () =>
            {
                var pick = owner.Zones.Library.GetCards()
                    .FirstOrDefault(c => c.HasType(CardType.Artifact));
                if (pick == null) return; // CR 701.19a — decline / no candidate.

                owner.Zones.Library.RemoveCard(pick);
                owner.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
                // CR 701.19c shuffle deferred — see class xmldoc.
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability — {R}, {T}, Sacrifice an artifact:
        //   Return target artifact card from your graveyard to the
        //   battlefield. CR 113.3b / 608.
        //
        // The sacrifice cost is recorded for shape inspection via
        // AdditionalCost.Sacrifice(picked) on a deterministically-chosen
        // battlefield artifact (first artifact the controller controls
        // that is not the source). Because AdditionalCost.Sacrifice's
        // Pay() is a TODO stub, the effect body also performs the
        // sacrificed-artifact zone move (mirrors EngineeredExplosives /
        // MishrasBauble / PerniciousDeed).
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            "Goblin Engineer: sacrifice an artifact, reanimate target artifact card from your graveyard",
            () =>
            {
                // Re-resolve the sacrifice pick at resolution time so the
                // closure observes current battlefield state (CR 608.2b
                // re-check). The activated-ability path doesn't yet plumb
                // the picked permanent through; sample fresh here.
                var sacArtifact = owner.Zones.Battlefield.GetCards()
                    .OfType<Permanent>()
                    .FirstOrDefault(c =>
                        !ReferenceEquals(c, card)
                        && (c is Artifact || c.HasType(CardType.Artifact))
                        && ReferenceEquals(c.Controller, owner));

                // No artifact to sacrifice → the activation should never
                // have been legal. Bail without reanimating (the cost
                // wasn't paid).
                if (sacArtifact == null) return;

                // Reanimation pick — first artifact card in the
                // controller's graveyard. Permanent-only is not required
                // here; CR 110.4 — every artifact card is a permanent
                // card.
                var graveyardArtifact = owner.Zones.Graveyard.GetCards()
                    .FirstOrDefault(c => c is Artifact || c.HasType(CardType.Artifact));
                if (graveyardArtifact == null) return; // CR 117.x — no-op.

                var sacOwner = sacArtifact.Owner ?? owner;
                if (zoneService != null)
                {
                    zoneService.MoveCard(
                        sacArtifact,
                        ZoneType.Battlefield,
                        ZoneType.Graveyard,
                        sacOwner);
                }
                else
                {
                    owner.Zones.Battlefield.RemoveCard(sacArtifact);
                    sacOwner.Zones.Graveyard.AddCard(sacArtifact);
                    sacArtifact.SetZone(ZoneType.Graveyard);
                }

                if (zoneService != null)
                {
                    zoneService.MoveCard(
                        graveyardArtifact,
                        ZoneType.Graveyard,
                        ZoneType.Battlefield,
                        owner);
                }
                else
                {
                    owner.Zones.Graveyard.RemoveCard(graveyardArtifact);
                    owner.Zones.Battlefield.AddCard(graveyardArtifact);
                    graveyardArtifact.SetZone(ZoneType.Battlefield);
                    graveyardArtifact.SetController(owner);
                }
            });

        // Structural costs: {R} mana + {T} tap. The "Sacrifice an
        // artifact" cost is not represented as a structural cost — see
        // class xmldoc; the sacrifice is performed by the effect body.
        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{R}"),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }
}
