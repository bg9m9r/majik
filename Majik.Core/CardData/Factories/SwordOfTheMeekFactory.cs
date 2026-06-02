using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sword of the Meek (Future Sight, {2}).
///
/// Artifact — Equipment. Oracle text (verified against Scryfall 2026-06-02):
///   "Equipped creature gets +1/+2.
///    Equip {2}
///    Whenever a 1/1 creature you control enters, you may return this card
///    from your graveyard to the battlefield, then attach it to that
///    creature."
///
/// ## Implementation
///
/// - <b>Base shape</b> (name / Artifact / Equipment subtype / {2}) is
///   materialised from the embedded JSON definition (<c>sword-of-the-meek.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="ArdentPleaFactory"/>; the JSON <c>AbilityDefinition</c> schema
///   does not yet express the static boost / equip / graveyard-return shapes,
///   so those are layered on here.
/// - <b>Static "equipped creature gets +1/+2"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR 613
///   Layer 7c). The effect reads <see cref="Permanent.AttachedTo"/>
///   dynamically, so re-equipping transfers the boost without re-registration.
///   Identical shape to <see cref="BonesplitterFactory"/> /
///   <see cref="SwordOfFireAndIceFactory"/>, differing only in magnitude
///   (+1/+2). Gated on the Sword being on the battlefield AND attached
///   (<see cref="AttachedBoostEffect.IsActive"/>).
/// - <b>Equip {2}</b> — activated ability (CR 702.6) via the
///   <see cref="EquipActivatedAbility"/> primitive. Sorcery-speed gate
///   (CR 117.1a / 307.5), "creature you control" target gathering
///   (CR 702.6b), attach resolution, and the Puresteel Paladin zero-equip
///   cost-provider hook are all encapsulated.
/// - <b>Graveyard-resident return-and-attach trigger (CR 603.6d)</b>:
///   "Whenever a 1/1 creature you control enters, you may return this card
///   from your graveyard to the battlefield, then attach it to that
///   creature." Active only while the Sword is in its owner's Graveyard
///   (<c>activeZones = {Graveyard}</c>). Watches <see cref="CardMovedEvent"/>;
///   fires when a creature with base printed power 1 and toughness 1 enters
///   the battlefield under the Sword's owner's control. Same graveyard-trigger
///   shape as <see cref="BloodghastFactory"/>'s landfall return; on resolve
///   it additionally calls <see cref="Permanent.AttachTo"/> to attach the
///   returned Sword onto the triggering creature (CR 701.3 — "then attach it
///   to that creature" is part of the same instruction, no targeting / Equip
///   cost). When a <see cref="ZoneService"/> is wired the return uses
///   <see cref="ZoneService.MoveCard"/> so the Sword's own ETB triggers fire
///   (CR 603.6a); otherwise a raw zone move is performed. "You may" is
///   auto-accepted unless an <see cref="IPlayerAgent"/> is supplied
///   (same posture as <see cref="BloodghastFactory"/>).
///
/// ## P/T snapshot semantics (CR 603.6e / 603.10)
///
/// The trigger keys on the entering creature being a <em>1/1</em>. Because
/// "enters" triggers look back in time at the entering object's state as it
/// entered (CR 603.10), this reads the entering creature's live
/// <see cref="Creature.GetPower"/> / <see cref="Creature.GetToughness"/> at
/// trigger-evaluation time — which, for a creature that has just entered,
/// is its as-entered P/T (modulo any same-timestamp static layers already
/// applied). This is the standard v1 approximation used across the engine's
/// "a [X/Y] creature enters" triggers.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only — the +1/+2 boost is not
/// registered against any <see cref="ContinuousEffectsService"/> and the
/// graveyard trigger is attached for structural inspection but not enrolled
/// with a <see cref="TriggerManager"/>. Use the full-wiring overload to wire
/// runtime services.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks the first
///   controller-side creature deterministically (inherited from
///   <see cref="EquipActivatedAbility"/>).
/// </summary>
[CardName("Sword of the Meek")]
public static class SwordOfTheMeekFactory
{
    public const string CardName = "Sword of the Meek";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "sword-of-the-meek";

    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Sword of the Meek with no live runtime wiring (the shape /
    /// dispatcher path). The +1/+2 boost is not registered against any
    /// service; the graveyard-return trigger is attached to the card but not
    /// registered with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, zoneService: null, triggers: null, agent: null);

    /// <summary>
    /// Constructs Sword of the Meek with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Continuous-effects service for the
    /// static +1/+2 boost (Layer 7c). May be null — boost not registered.</param>
    /// <param name="zoneService">Zone service used by the graveyard-return
    /// trigger to move the Sword from graveyard to battlefield so its own ETB
    /// triggers fire (CR 603.6a). May be null — raw zone move performed.</param>
    /// <param name="triggers">Trigger manager for graveyard-resident trigger
    /// registration (CR 603.6d). May be null — trigger attached for shape
    /// only.</param>
    /// <param name="agent">Optional agent for the "you may" return decision
    /// (<see cref="BotIntent.Reanimate"/>). Null preserves the auto-accept
    /// v1 posture.</param>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Artifact / Equipment / {2}) from the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Artifact card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Artifact but got "
                + $"'{built.GetType().Name}'.");
        }

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +1/+2."
        // CR 613 Layer 7c. Gates on the source being on the battlefield
        // AND attached (see AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 1, toughness: 2));
        }

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6) via the
        // EquipActivatedAbility primitive.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        // --------------------------------------------------------------
        // Graveyard-resident return-and-attach trigger — CR 603.1 / 603.6d.
        //   "Whenever a 1/1 creature you control enters, you may return this
        //    card from your graveyard to the battlefield, then attach it to
        //    that creature."
        // Active only while the Sword is in its owner's Graveyard. Fires on
        // a CardMovedEvent filtered to:
        //   - ToZone == Battlefield
        //   - the entering card is a Creature with power 1 and toughness 1
        //     (CR 603.10 — "enters" trigger reads as-entered state)
        //   - the entering creature's Controller is the Sword's owner
        //     ("a 1/1 creature you control")
        // On resolve: return the Sword from graveyard to battlefield, then
        // attach it to the triggering creature (CR 701.3 — same instruction,
        // no Equip cost, no targeting).
        // --------------------------------------------------------------
        Creature? pendingTrigger = null;

        var returnAndAttachEffect = new Effect(
            $"{CardName}: return from graveyard to battlefield and attach (1/1-enters trigger)",
            async _ =>
            {
                var enteringCreature = pendingTrigger;
                pendingTrigger = null;
                if (enteringCreature == null) return;

                // CR 603.6d — re-check zone at resolution.
                if (card.Zone != ZoneType.Graveyard) return;
                if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

                // "You may" — consult the agent when wired; else auto-accept.
                if (agent != null)
                {
                    var yes = await agent.ChooseYesNoAsync(
                        "Return Sword of the Meek from graveyard and attach it?",
                        BotIntent.Reanimate).ConfigureAwait(false);
                    if (!yes) return;
                }

                if (zoneService != null)
                {
                    // ZoneService.MoveCard fires the Sword's own ETB triggers
                    // + replacements (CR 603.6a).
                    zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, owner);
                }
                else
                {
                    // Raw zone move — no ETB event published.
                    owner.Zones.Graveyard.RemoveCard(card);
                    owner.Zones.Battlefield.AddCard(card);
                    card.SetZone(ZoneType.Battlefield);
                    card.SetController(owner);
                }

                // "then attach it to that creature" — CR 701.3. Only attach
                // if the creature is still on the battlefield (CR 608.2c — do
                // as much as possible if it has since left).
                if (enteringCreature.Zone == ZoneType.Battlefield)
                {
                    card.AttachTo(enteringCreature);
                }
            });

        var returnTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                if (e.ToZone != ZoneType.Battlefield) return false;
                if (e.Card is not Creature entering) return false;
                // "a 1/1 creature" — CR 603.10 reads as-entered P/T.
                if (entering.GetPower() != 1 || entering.GetToughness() != 1) return false;
                // "you control" — entering creature's controller must be the
                // Sword's owner at event time (CR 614.6 — controller assessed
                // on the live battlefield after the move completes).
                if (!ReferenceEquals(entering.Controller, owner)) return false;

                // Latch the triggering creature for the resolution effect.
                pendingTrigger = entering;
                return true;
            }),
            effects: new IEffect[] { returnAndAttachEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(returnTrigger);
        triggers?.RegisterTriggeredAbility(returnTrigger);

        return card;
    }
}
