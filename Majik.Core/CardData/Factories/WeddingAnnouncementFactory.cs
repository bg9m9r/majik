using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wedding Announcement // Wedding Festivity
/// (Innistrad: Crimson Vow, {2}{W}) — a transforming double-faced
/// Enchantment (CR 712 — transforming DFC; Scryfall layout "transform").
///
/// Front face — Wedding Announcement. Enchantment. Oracle text (verified
/// against Scryfall 2026-06-02):
///   "At the beginning of your end step, put an invitation counter on this
///    enchantment. If you attacked with two or more creatures this turn,
///    draw a card. Otherwise, create a 1/1 white Human creature token.
///    Then if this enchantment has three or more invitation counters on it,
///    transform it."
///
/// Back face — Wedding Festivity. Enchantment. Oracle text:
///   "Creatures you control get +1/+1."
///
/// The base shape (name, single Enchantment card type, {2}{W}) is
/// materialised from the embedded JSON definition
/// (<c>wedding-announcement.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="IntangibleVirtueFactory"/> / <see cref="HonorOfThePureFactory"/>.
/// The end-step trigger, the invitation-counter / transform sequencing, and
/// the back-face anthem are layered on here because the JSON
/// <c>AbilityDefinition</c> schema expresses none of them.
///
/// ## Implemented (v1)
/// - Card identity: Enchantment, mana cost {2}{W}, owner / controller wiring.
/// - <see cref="MdfcState"/> attached (front = "Wedding Announcement",
///   back = "Wedding Festivity") so callers / tests inspect
///   <see cref="MdfcState.IsBackFace"/> to read the active face (CR 712).
/// - <b>End-step trigger (CR 603.1 / CR 500.4)</b> scoped to the controller's
///   own end step via <see cref="Triggers.OnStepBegin"/> with
///   <see cref="Majik.Core.StateMachine.StepStateType.End"/>. On resolution
///   (front face only — the trigger no-ops once transformed since the back
///   face has no end-step ability):
///     1. Put an invitation counter on this enchantment (CR 122 — a generic
///        named counter; <see cref="CounterType"/> "Invitation").
///     2. If the controller attacked with two or more creatures this turn,
///        draw a card; otherwise create a 1/1 white Human creature token
///        (CR 111). The "attacked with two or more creatures this turn" fact
///        is tracked the same way <see cref="BloodsoakedChampionFactory"/>
///        tracks its Raid clause: a live <see cref="CreatureAttacksEvent"/>
///        listener records each DISTINCT creature the controller declared as
///        an attacker this turn (a <see cref="HashSet{T}"/>), reset on the
///        controller's <see cref="TurnStartedEvent"/>.
///     3. Then if this enchantment has three or more invitation counters on
///        it, transform it (CR 701.28 / CR 712.4). On transform the back-face
///        anthem ("Creatures you control get +1/+1") is registered against
///        the supplied <see cref="ContinuousEffectsService"/> as a
///        <see cref="ControllerCreatureAnthemEffect"/> (requiredColor: null —
///        all creatures), gated active only while back-face up.
/// - <b>Back-face anthem (CR 613.7c)</b>: "Creatures you control get +1/+1."
///   Modelled as a <see cref="ControllerCreatureAnthemEffect"/> whose
///   <see cref="ContinuousEffect.IsActive"/> gate additionally requires the
///   <see cref="MdfcState"/> to be back-face up — so it contributes nothing
///   while the front (Wedding Announcement) face is showing, and lifts if the
///   enchantment leaves the battlefield (CR 614). Same Layer-7c anthem shape
///   as <see cref="HonorOfThePureFactory"/> / Glorious Anthem, minus the
///   colour filter.
///
/// ## Deferred (v1 gaps — identical posture to the cited analogues)
/// - <b>Live trigger registration</b>: the single-/two-arg overloads attach
///   the end-step trigger to the card for shape / dispatcher tests; the
///   (owner, ces, zones, bus, triggers) overload registers it with a
///   <see cref="TriggerManager"/> so an End <see cref="StepStartedEvent"/>
///   for the controller queues it on the stack (same posture as
///   <see cref="DelverOfSecretsFactory"/>).
/// - <b>"Two or more creatures" tracking without a bus</b>: when no event bus
///   is supplied the attacker count is always zero, so the end-step branch
///   falls through to the token (the "you may not have attacked" default).
/// - <b>LTB / control-change re-evaluation of the anthem</b>: the registered
///   effect stays on the CES across zone changes; its IsActive gate flips it
///   off when the source is off the battlefield or front-face up. Same caveat
///   posture as the other anthem factories.
/// </summary>
[CardName("Wedding Announcement // Wedding Festivity")]
public static class WeddingAnnouncementFactory
{
    public const string CardName = "Wedding Announcement // Wedding Festivity";
    public const string FrontName = "Wedding Announcement";
    public const string BackName = "Wedding Festivity";
    public const string Slug = "wedding-announcement";

    /// <summary>CR 122 — the "invitation counter" tracked on the enchantment;
    /// the transform threshold reads its count. Three or more flips the
    /// front face to Wedding Festivity.</summary>
    public static readonly CounterType InvitationCounter = new("Invitation");

    private const int TransformThreshold = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Wedding Announcement with no live wiring. The end-step
    /// trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>, and the back-face anthem is not
    /// registered — suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, continuousEffects: null, zones: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Wedding Announcement with a continuous-effects service (so the
    /// back-face anthem can register on transform) but no live trigger / bus
    /// wiring. The end-step trigger is attached for shape; fire it manually in
    /// tests via <see cref="TriggeredAbility"/>.
    /// </summary>
    public static Enchantment Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, zones: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Wedding Announcement.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service the back-face anthem
    /// registers against on transform. May be null — no live anthem.</param>
    /// <param name="zones">Zone service routing token creation so ETB triggers
    /// fire (CR 603.6a). May be null — raw zone move.</param>
    /// <param name="eventBus">When supplied, a <see cref="CreatureAttacksEvent"/>
    /// listener counts the controller's distinct attackers this turn and a
    /// <see cref="TurnStartedEvent"/> listener resets the count.</param>
    /// <param name="triggers">When supplied, the end-step trigger is registered
    /// so an End <see cref="StepStartedEvent"/> for the controller queues it.</param>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zones,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Enchantment, {2}{W}) from the embedded JSON def.
        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 712 — attach the DFC face tracker so callers can observe the
        // active face. Starts front-face up (Wedding Announcement); the
        // end-step trigger flips it once the invitation-counter threshold is
        // met. Back-face P/T is N/A (both faces are non-creature Enchantments),
        // so no BackFaceCharacteristics carrier is needed — the anthem is the
        // back face's only effect and is gated on IsBackFace.
        card.MdfcState = new MdfcState(FrontName, BackName);

        // ----------------------------------------------------------------
        // "Attacked with two or more creatures this turn" tracking
        // (CR 508.1 — declare attackers). A live CreatureAttacksEvent
        // listener records each DISTINCT creature the controller declared as
        // an attacker this turn; a TurnStartedEvent for the controller resets
        // the set ("this turn" — CR 500.1 / 514). Same shape as Bloodsoaked
        // Champion's Raid tracking, but counting distinct attackers rather
        // than a single boolean flag.
        // ----------------------------------------------------------------
        var attackersThisTurn = new HashSet<Creature>();
        if (eventBus != null)
        {
            eventBus.Subscribe<CreatureAttacksEvent>(e =>
            {
                if (ReferenceEquals(e.Attacker.Controller, card.Controller ?? owner))
                    attackersThisTurn.Add(e.Attacker);
            });
            eventBus.Subscribe<TurnStartedEvent>(e =>
            {
                if (ReferenceEquals(e.Player, card.Controller ?? owner))
                    attackersThisTurn.Clear();
            });
        }

        // ----------------------------------------------------------------
        // Back-face anthem (CR 613.7c) — "Creatures you control get +1/+1."
        // Registered eagerly against the CES so the live P/T memoization sees
        // it, but its IsActive gate additionally requires the MdfcState to be
        // back-face up, so it contributes nothing until the transform. Layer
        // 7c, no colour filter (all the controller's creatures).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new BackFaceAnthemEffect(
                source: card,
                power: 1,
                toughness: 1));
        }

        // ----------------------------------------------------------------
        // End-step trigger — CR 603.1, CR 500.4.
        //   "At the beginning of your end step, put an invitation counter on
        //    this enchantment. If you attacked with two or more creatures this
        //    turn, draw a card. Otherwise, create a 1/1 white Human creature
        //    token. Then if this enchantment has three or more invitation
        //    counters on it, transform it."
        // Fires only on the controller's own end step. The trigger no-ops once
        // the card is back-face up (Wedding Festivity has no end-step ability).
        // ----------------------------------------------------------------
        var endStepEffect = new Effect(
            $"{FrontName}: invitation counter; draw-or-token; transform at 3+",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (card.MdfcState == null || card.MdfcState.IsBackFace) return;

                var controller = card.Controller ?? owner;

                // 1. Put an invitation counter on this enchantment (CR 122).
                card.Counters.Add(InvitationCounter);

                // 2. Attacked with two or more creatures this turn?
                if (attackersThisTurn.Count >= 2)
                {
                    // Draw a card (CR 120). Empty-library underflow flags the
                    // SBA-loss condition (CR 104.3c / 704.5c).
                    var top = controller.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        controller.MarkTriedToDrawFromEmptyLibrary();
                    }
                    else
                    {
                        controller.Zones.Library.RemoveCard(top);
                        controller.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    }
                }
                else
                {
                    // Otherwise create a 1/1 white Human creature token (CR 111).
                    TokenFactory.CreateOnBattlefield(
                        new TokenFactory.TokenSpec(
                            Name: "Human",
                            Power: 1,
                            Toughness: 1,
                            Subtypes: new[] { CardSubtype.Human },
                            Keywords: null,
                            Colors: new[] { ManaColor.White }),
                        controller,
                        zones);
                }

                // 3. Then if this enchantment has three or more invitation
                //    counters on it, transform it (CR 701.28 / 712.4). The
                //    back-face anthem's IsActive gate flips live on the flip.
                if (card.Counters.Count(InvitationCounter) >= TransformThreshold)
                {
                    card.MdfcState!.Transform();
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.StepStateType.End),
            effects: new IEffect[] { endStepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 613.7c — Wedding Festivity's "Creatures you control get +1/+1"
    /// anthem, gated on the source being BACK-face up. A plain Layer-7c
    /// controller anthem (no colour filter — same shape as Glorious Anthem),
    /// whose <see cref="ContinuousEffect.IsActive"/> additionally requires the
    /// source's <see cref="MdfcState"/> to read back-face — so the anthem
    /// contributes nothing while the Wedding Announcement front face is
    /// showing and lifts on LTB (CR 614).
    ///
    /// <para>Subclasses <see cref="ContinuousEffect"/> directly rather than
    /// <see cref="ControllerCreatureAnthemEffect"/> (which is sealed); the
    /// AppliesTo / Apply logic mirrors that anthem's all-creatures branch.</para>
    /// </summary>
    private sealed class BackFaceAnthemEffect : ContinuousEffect
    {
        private readonly Permanent _src;
        private readonly int _power;
        private readonly int _toughness;

        public BackFaceAnthemEffect(Permanent source, int power, int toughness)
        {
            _src = source ?? throw new ArgumentNullException(nameof(source));
            _power = power;
            _toughness = toughness;
        }

        public override Layer Layer => Layer.PT_Modify;

        public override Permanent? Source => _src;

        // Active only while on the battlefield AND showing the Wedding
        // Festivity (back) face. Front-face up = no anthem.
        public override bool IsActive() =>
            _src.Zone == ZoneType.Battlefield
            && _src.MdfcState is { IsBackFace: true };

        public override bool AppliesTo(Creature creature)
        {
            if (creature.Zone != ZoneType.Battlefield) return false;
            // "Creatures you control" — the source's controller, all colours.
            return ReferenceEquals(creature.Controller, _src.Controller);
        }

        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += _power;
            chars.Toughness += _toughness;
        }

        /// <summary>
        /// Sim-only: reconstruct an identical <see cref="BackFaceAnthemEffect"/>
        /// bound to <paramref name="clonedSource"/> for the search-sandbox clone.
        /// The MdfcState and controller are read live from clonedSource (correctly
        /// remapped by Pass 2c of the cloner).
        /// preserves: _power, _toughness; source → clonedSource.
        /// </summary>
        internal override ContinuousEffect? CloneForSim(
            Majik.Core.Cards.Permanent clonedSource,
            System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
            => new BackFaceAnthemEffect(clonedSource, _power, _toughness);
    }
}
