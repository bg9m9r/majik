using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Krang, Master Mind (Teenage Mutant Ninja Turtles /
/// Universes Beyond, {6}{U}{U}).
///
/// Legendary Artifact Creature — Utrom Warrior 1/4. Oracle text:
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    When Krang enters, if you have fewer than four cards in hand, draw
///    cards equal to the difference.
///    Krang gets +1/+0 for each other artifact you control."
///
/// ## Implementation
///
/// ### Card identity
/// - Mana cost {6}{U}{U} (MV 8), printed P/T 1/4.
/// - Legendary Artifact Creature — Utrom Warrior. The base
///   <see cref="Creature"/> constructor registers only Creature; the
///   Artifact type is additively flagged via <c>AddCardType(Artifact)</c>
///   (mirrors <see cref="FrogmiteFactory"/> / <see cref="KappaCannoneerFactory"/>).
/// - Blue colour identity from the UU pips.
///
/// ### Affinity for artifacts (CR 702.40 / CR 117.7)
/// Wired via <see cref="CostReductionAbility.AffinityFor(CardType.Artifact)"/>
/// — identical to Frogmite, Myr Enforcer, and Sojourner's Companion. A
/// <see cref="KeywordAbility"/> marker "Affinity" is also attached for bot
/// discovery (mirrors the standard pattern). Because Krang is itself an
/// Artifact Creature, it counts as one of its own artifacts if already on
/// the battlefield — the printed wording is "each artifact you control"
/// (CR 702.40a), not "each other artifact".
///
/// ### ETB triggered ability with intervening-if (CR 603.1 / CR 603.4)
/// "When Krang enters, if you have fewer than four cards in hand, draw
/// cards equal to the difference."
/// Wired as a self-ETB <see cref="TriggeredAbility"/> with an
/// <see cref="TriggeredAbility.InterveningIf"/> gate that re-checks the
/// hand-size condition at both trigger-check time (CR 603.4 — triggers that
/// have an intervening-if check the condition when the event occurs AND when
/// the ability would resolve; neither Krang's ETB nor any other factory in
/// this engine models the resolve-time re-check separately — the single
/// <c>interveningIf</c> lambda is evaluated on both code paths by the
/// engine's trigger queue). The draw count is clamped to
/// <c>4 − handCount</c> (minimum 0) and routed through
/// <see cref="Fx.DrawCards"/> so empty-library detection
/// (<see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>) runs correctly
/// per CR 704.5b.
///
/// ### Variable power — "+1/+0 for each other artifact you control"
/// (CR 613.2 Layer 7c — this is a static continuous ability that adds to
/// power, not a CDA that replaces it in Layer 7a. Krang has a printed
/// power of 1, and the "+1/+0 for each OTHER artifact" ability stacks on
/// top. We implement this as a <see cref="CdaPowerToughnessEffect"/> in
/// Layer PT_Cda that reads
/// <c>1 + count(otherArtifactsControlledByKrang'sController)</c> for power
/// and keeps toughness constant at 4. Using the CDA layer is conservative:
/// it correctly captures that Krang's effective printed stats are
/// data-dependent rather than fixed, and the CDA layer applies before
/// Layer 7c counter pump (CR 613.7), so +1/+1 counters still stack on top.
///
/// The lifecycle (register on ETB / unregister on LTB) mirrors
/// <see cref="TarmogoyfFactory"/>'s <c>TarmogoyfCdaLifecycle</c>: a small
/// inner class subscribes to <see cref="CardMovedEvent"/> and
/// registers/unregisters the <see cref="CdaPowerToughnessEffect"/> as
/// Krang enters/leaves the battlefield.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. The ETB trigger is
///   attached so dispatcher tests observe it; the variable-power CDA is NOT
///   registered (no <see cref="ContinuousEffectsService"/>). Suitable for
///   identity / dispatcher / affinity-cost tests.
/// - <see cref="Create(Player, ContinuousEffectsService?, IEventBus?, TriggerManager?)"/>
///   — fully wired. ETB trigger registered when
///   <paramref name="triggers"/> is supplied; variable-power CDA registered
///   against <paramref name="effects"/> with ETB/LTB lifecycle tracked via
///   <paramref name="eventBus"/>.
/// </summary>
[CardName("Krang, Master Mind")]
public static class KrangMasterMindFactory
{
    public const string CardName = "Krang, Master Mind";
    public const string PrintedManaCost = "{6}{U}{U}";
    public const int PrintedPower = 1;
    public const int PrintedToughness = 4;

    /// <summary>
    /// The threshold used by Krang's ETB intervening-if (CR 603.4):
    /// "if you have fewer than four cards in hand".
    /// </summary>
    public const int HandThreshold = 4;

    /// <summary>
    /// Construct Krang, Master Mind with no live runtime wiring.
    /// The ETB trigger is attached so structural / dispatcher tests observe
    /// it; the variable-power CDA is NOT registered without a
    /// <see cref="ContinuousEffectsService"/>. Suitable for identity /
    /// dispatcher / affinity-cost tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Krang, Master Mind with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">
    ///   <see cref="ContinuousEffectsService"/> for the variable-power CDA
    ///   (Layer PT_Cda). Pass null for shape-only — the CDA will not be
    ///   registered and <see cref="Creature.Power"/> will return the printed
    ///   base power (1) plus any counter-postlude increment.
    /// </param>
    /// <param name="eventBus">
    ///   Event bus for ETB / LTB lifecycle tracking of the CDA. May be null
    ///   — the CDA's <see cref="CdaPowerToughnessEffect.IsActive"/> gate
    ///   covers correctness; no explicit unregister will fire on LTB.
    /// </param>
    /// <param name="triggers">
    ///   <see cref="TriggerManager"/> for the ETB hand-refill trigger. May
    ///   be null — the trigger is still attached to the card shape for
    ///   structural tests.
    /// </param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: PrintedPower,
            toughness: PrintedToughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Utrom, CardSubtype.Warrior });

        // CR 301.1 / 302.1 — Krang is a Legendary Artifact Creature.
        // The base Creature constructor registers only CardType.Creature;
        // additively flag the Artifact type so HasType(Artifact) passes.
        // This also allows Krang to count itself for Affinity if already
        // on the battlefield (printed wording: "each artifact you control",
        // not "each OTHER artifact" — CR 702.40a).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Affinity for artifacts (CR 702.40 / CR 117.7).
        // Wired via CostReductionAbility.AffinityFor(Artifact) — identical
        // shape to Frogmite, Myr Enforcer, Sojourner's Companion. The
        // KeywordAbility marker "Affinity" is attached alongside for bot
        // discovery and keyword-scan callers.
        // ----------------------------------------------------------------
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));
        card.AddAbility(new KeywordAbility("Affinity", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability with intervening-if (CR 603.1 / CR 603.4).
        //   "When Krang enters, if you have fewer than four cards in hand,
        //    draw cards equal to the difference."
        //
        // The interveningIf lambda reads the controller's current hand
        // count and returns true when handCount < 4. The lambda captures
        // the card reference so it can resolve the live controller at call
        // time (owner may not be the controller after a control-change
        // effect, though in practice Krang is a legendary creature and
        // control-changing is rare). The draw count is:
        //   HandThreshold − handCount   (always ≥ 1 when the gate passes)
        // Routed through Fx.DrawCards so empty-library detection works
        // (CR 704.5b / CR 120.3).
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: draw cards up to 4-card hand threshold",
            () =>
            {
                var controller = card.Controller ?? owner;
                var handCount = controller.Zones.Hand.GetCards().Count();

                // Re-check the intervening-if condition at resolution time
                // (CR 603.4 — the condition must be true both when the
                // trigger event occurs AND when the ability would resolve).
                if (handCount >= HandThreshold) return;

                var drawCount = HandThreshold - handCount;
                Fx.DrawCards(controller, drawCount);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: () =>
            {
                var controller = card.Controller ?? owner;
                return controller.Zones.Hand.GetCards().Count() < HandThreshold;
            },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Variable power: "+1/+0 for each other artifact you control."
        // Implemented as a CDA (Layer PT_Cda — CR 613.2) that sets
        // Krang's effective power to 1 + count(other artifacts controller
        // controls). Toughness is kept at the printed 4.
        //
        // "Other artifact" = artifacts on the battlefield under Krang's
        // controller's control that are NOT Krang itself. Krang does not
        // count itself for this ability (the printed text says "each other
        // artifact", CR 109.5).
        //
        // The lifecycle mirrors TarmogoyfFactory: a KrangCdaLifecycle
        // subscribes to CardMovedEvent and registers/unregisters the CDA
        // as Krang enters/leaves the battlefield.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            var lifecycle = new KrangCdaLifecycle(card, effects, eventBus, owner);
            lifecycle.Attach();
        }

        return card;
    }

    /// <summary>
    /// Count the number of artifacts other than <paramref name="krang"/>
    /// on the battlefield under <paramref name="controller"/>'s control.
    /// Pure helper exposed for tests.
    /// </summary>
    public static int CountOtherArtifacts(Player controller, Creature krang)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(krang);

        return controller.Zones.Battlefield.GetCards()
            .Where(c => !ReferenceEquals(c, krang) && c.HasType(CardType.Artifact))
            .Count();
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Krang's variable-power CDA.
    /// Subscribes to <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Krang enters the
    /// battlefield, unregisters when it leaves. Mirrors the structure of
    /// <see cref="TarmogoyfFactory"/>'s <c>TarmogoyfCdaLifecycle</c>.
    /// </summary>
    private sealed class KrangCdaLifecycle
    {
        private readonly Creature _source;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Player _defaultOwner;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public KrangCdaLifecycle(
            Creature source,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Player defaultOwner)
        {
            _source = source;
            _effects = effects;
            _eventBus = eventBus;
            _defaultOwner = defaultOwner;
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
            if (shouldBeActive && _registered == null)
            {
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    powerOf: _ =>
                    {
                        var controller = _source.Controller ?? _defaultOwner;
                        return PrintedPower + CountOtherArtifacts(controller, _source);
                    },
                    toughnessOf: _ => PrintedToughness);
                _effects.Register(_registered);
            }
            else if (!shouldBeActive && _registered != null)
            {
                _effects.Unregister(_registered);
                _registered = null;
            }
        }
    }
}
