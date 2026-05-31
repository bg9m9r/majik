using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Master of Etherium (Shards of Alara, {2}{U}).
///
/// Artifact Creature — Vedalken Wizard. Oracle text (per Modern seed):
///   "Master of Etherium's power and toughness are each equal to the
///    number of artifacts you control.
///    Other artifact creatures you control get +1/+1."
///
/// ## Implemented (v1)
///
/// - <b>Artifact Creature — Vedalken Wizard</b> at <c>{2}{U}</c>. The base
///   <see cref="Creature"/> ctor only registers
///   <see cref="CardType.Creature"/>; the Artifact type is additively
///   stamped via <c>AddCardType(CardType.Artifact)</c> (mirrors
///   <see cref="SteelOverseerFactory"/> / <see cref="KappaCannoneerFactory"/>'s
///   multi-type shape). Master of Etherium counts as one of its own
///   artifacts for the CDA's "number of artifacts you control" tally
///   (the master is an artifact you control), but it does NOT count for
///   its own +1/+1 anthem (the anthem says "Other artifact creatures").
/// - <b>CDA P/T (CR 604.3 / 613.2 — Layer 7a)</b>: power and toughness
///   each equal the count of artifacts controlled by Master of Etherium's
///   controller. Wired via <see cref="CdaPowerToughnessEffect"/> whose
///   evaluators read the controller's battlefield artifacts live at
///   compute time (mirrors <see cref="MortivoreFactory"/>'s graveyard
///   scan). Printed P/T is */* (CR 208.2c); we seed
///   <c>BasePower=0, BaseToughness=0</c> as harmless placeholders since
///   Layer 7a overwrites them on every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/>.
/// - <b>Lord static (CR 613.7c — Layer 7c)</b>: "Other artifact creatures
///   you control get +1/+1." Implemented via a tailored
///   <see cref="MasterOfEtheriumLordEffect"/> (private nested) that
///   filters on Artifact + Creature card types AND
///   <c>controller == source.Controller</c> AND <c>!ReferenceEquals(self,
///   source)</c>. The existing <see cref="LordStaticEffect"/> is keyed on
///   a single <see cref="CardSubtype"/> (Merfolk / Spirit / Goblin /
///   etc.) — Master of Etherium needs a card-TYPE filter, so a tailored
///   variant is shipped here.
///
/// ## Lifecycle
///
/// ETB/LTB lifecycle mirrors <see cref="MortivoreFactory"/>'s CDA wiring:
/// subscribe to <see cref="CardMovedEvent"/>; register the CDA + lord
/// effects when Master of Etherium enters the battlefield, unregister
/// when it leaves. The <see cref="CdaPowerToughnessEffect.IsActive"/> +
/// <see cref="MasterOfEtheriumLordEffect.IsActive"/> battlefield gates
/// are belt-and-braces redundancies if no event bus is supplied.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. No CDA, no anthem; the
///   card is structurally correct (name, types, P/T placeholders) but
///   the layer-7 effects don't register without a continuous-effects
///   service. Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/>
///   — fully wired. CDA + lord register on ETB and unregister on LTB.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Artifacts you control" count semantics</b>: the count includes
///   token artifacts and the master itself (the printed wording covers
///   every artifact-typed permanent the controller controls). The
///   tally reads <c>Battlefield.GetCards()</c> for cards with
///   <see cref="CardType.Artifact"/> at compute time.
/// </summary>
[CardName("Master of Etherium")]
public static class MasterOfEtheriumFactory
{
    public const string CardName = "Master of Etherium";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>
    /// Construct Master of Etherium with no live wiring. CDA + lord
    /// effects are NOT attached (no continuous-effects service). Card is
    /// structurally correct (name, types, subtype, owner/controller) but
    /// the layer-7 effects don't fire. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct a fully-wired Master of Etherium. When
    /// <paramref name="effects"/> is supplied, both the
    /// <see cref="CdaPowerToughnessEffect"/> (Layer 7a, P/T = #artifacts
    /// you control) and <see cref="MasterOfEtheriumLordEffect"/> (Layer
    /// 7c, "Other artifact creatures you control get +1/+1") register
    /// against the layers service via an ETB/LTB lifecycle binder.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service to register the
    /// CDA + lord against. Pass null for shape-only.</param>
    /// <param name="eventBus">Event bus for ETB / LTB tracking. May be
    /// null — the layer-7 IsActive gates cover correctness, but no
    /// explicit unregister will fire on LTB.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Printed P/T is */* (CDA-defined per CR 208.2c). Seed 0/0
        // placeholders; Layer 7a overwrites them on every Compute.
        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 0,
            toughness: 0,
            subtypes: new[] { CardSubtype.Vedalken, CardSubtype.Wizard });

        // CR 301.1 / 302.1 — Master of Etherium is an Artifact Creature.
        // Stamp Artifact on the Creature shell so HasType lookups see it
        // (mirrors Steel Overseer / Kappa Cannoneer). The master then
        // counts as one of its own artifacts for the CDA tally — matches
        // the printed wording ("the number of artifacts you control"
        // includes the master itself).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            var lifecycle = new EtheriumLifecycle(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Count artifacts controlled by <paramref name="controller"/> on the
    /// battlefield (CR 109.5 — "you control" reads each card's current
    /// controller). Includes token artifacts and the master itself.
    /// Pure helper exposed for tests; mirrors the closure baked into the
    /// live <see cref="CdaPowerToughnessEffect"/>.
    /// </summary>
    public static int CountArtifactsControlled(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var count = 0;
        foreach (var c in controller.Zones.Battlefield.GetCards())
        {
            if (c.HasType(CardType.Artifact)) count++;
        }
        return count;
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Master of Etherium. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers the CDA + lord effects
    /// when the master enters the battlefield, unregisters when it
    /// leaves. Mirrors the structure of <c>MortivoreCdaLifecycle</c>.
    /// </summary>
    private sealed class EtheriumLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _cda;
        private MasterOfEtheriumLordEffect? _lord;
        private bool _attached;

        public EtheriumLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _eventBus?.Subscribe(_handler);
            Sync();
        }

        private void OnEvent(CardMovedEvent e)
        {
            var moved = e;
            if (!ReferenceEquals(moved.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive)
            {
                if (_cda == null)
                {
                    _cda = new CdaPowerToughnessEffect(
                        _source,
                        powerOf: src => CountArtifactsControlled(src.Controller ?? _source.Controller!),
                        toughnessOf: src => CountArtifactsControlled(src.Controller ?? _source.Controller!));
                    _effects.Register(_cda);
                }
                if (_lord == null)
                {
                    _lord = new MasterOfEtheriumLordEffect(_source);
                    _effects.Register(_lord);
                }
            }
            else
            {
                if (_cda != null)
                {
                    _effects.Unregister(_cda);
                    _cda = null;
                }
                if (_lord != null)
                {
                    _effects.Unregister(_lord);
                    _lord = null;
                }
            }
        }
    }
}

/// <summary>
/// Master of Etherium's "Other artifact creatures you control get +1/+1"
/// static (CR 613.7c — Layer 7c).
///
/// The existing <see cref="LordStaticEffect"/> filters on a single
/// <see cref="CardSubtype"/>, which doesn't fit Master of Etherium's
/// type-level filter (Artifact + Creature card types, not a creature
/// subtype). A tailored variant is shipped here.
///
/// Filter:
///   - Target is on the battlefield (CR 613.7c — continuous effects
///     apply only to permanents).
///   - Target is NOT the master itself (CR 109.5 — "Other ...").
///   - Target's controller is the master's controller (CR 109.5 — "you
///     control").
///   - Target has both <see cref="CardType.Creature"/> and
///     <see cref="CardType.Artifact"/>.
/// </summary>
public sealed class MasterOfEtheriumLordEffect : ContinuousEffect
{
    private readonly Permanent _source;

    public MasterOfEtheriumLordEffect(Permanent source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the lord permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != ZoneType.Battlefield) return false;
        // CR 109.5 — "Other" excludes the source itself.
        if (ReferenceEquals(creature, _source)) return false;
        // CR 109.5 — "you control" matches the source's controller.
        if (!ReferenceEquals(creature.Controller, _source.Controller)) return false;
        // Type filter: Artifact + Creature. Creature is implied by the
        // Creature-typed receiver; we still gate on Artifact explicitly.
        return creature.HasType(CardType.Artifact);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += 1;
        chars.Toughness += 1;
    }
}
