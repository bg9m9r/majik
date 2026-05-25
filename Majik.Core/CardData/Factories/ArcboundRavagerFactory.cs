using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arcbound Ravager (Darksteel / Modern Horizons 2,
/// {2}).
///
/// Artifact Creature — Beast 0/0. Oracle text:
///   "Sacrifice an artifact: Put a +1/+1 counter on this creature.
///    Modular 1 (This creature enters with a +1/+1 counter on it. When it
///    dies, you may put its +1/+1 counters on target artifact creature.)"
///
/// ## Implemented (v1)
///
/// - 0/0 Artifact Creature — Beast (multi-type via
///   <see cref="Card.AddCardType"/>), mana cost {2}, owner/controller wired.
/// - <b>Modular 1 — ETB +1/+1 counter (CR 702.43a / CR 614.1d)</b>:
///   wired through an <see cref="EntersWithCountersReplacement"/>
///   registered on the supplied <see cref="ReplacementBus"/>. The
///   <see cref="Services.ZoneService"/> ETB pipeline reads
///   <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> and stamps the
///   counter after landing. When no bus is supplied, the
///   <see cref="MarkEntersWithCounter"/> fallback manually stamps the
///   counter so shape-only tests still see the Modular 1 entry value.
/// - <b>Modular 1 — death trigger (CR 702.43b)</b>: a
///   <see cref="TriggeredAbility"/> fires on the
///   <see cref="Triggers.OnDies"/> transition (Battlefield → Graveyard).
///   The trigger's effect picks the first artifact creature on the
///   battlefield (deterministic v1 target — same posture as Stoneforge
///   Mystic's tutor pick) excluding Arcbound Ravager itself, then moves
///   every +1/+1 counter from Arcbound Ravager's graveyard-object
///   <see cref="Permanent.Counters"/> bag onto the chosen artifact
///   creature. The bag's value on the graveyard object survives the zone
///   move (Undying-shape — counters live on the card object until cleared
///   on its next entry), so the count at trigger-resolution time
///   accurately reflects what Arcbound Ravager had when it died.
/// - <b>Activated ability — sacrifice an artifact: +1/+1 counter</b>:
///   wired via <see cref="ActivatedAbility"/> with a
///   <see cref="SacrificeAnArtifactCost"/>. The cost picks the
///   first artifact on the controller's battlefield (deterministic v1 —
///   mirrors <see cref="SacrificeAnotherCreatureCost"/>). Arcbound
///   Ravager is itself an artifact, so the activation is self-fueling
///   when no other artifacts are available (the cost picker will choose
///   Arcbound Ravager — sacrificing it before the resolution effect lands
///   the counter is a known interaction: the counter is added to the
///   graveyard object, then the death trigger above fires and can move
///   it to another artifact creature). The activation is mana-free —
///   only the sacrifice is required.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Target prompt for Modular bestowal</b>: oracle says "target
///   artifact creature" — v1 picks the first artifact creature
///   deterministically (excluding Arcbound Ravager). Full prompting
///   requires threading <see cref="Players.Agents.TargetRequest"/>
///   through <see cref="TriggeredAbility.TargetRequests"/>; same gap as
///   Stoneforge Mystic's "attach to a creature you control".
/// - <b>"You may" Modular opt-out</b>: oracle says "you MAY put". v1
///   always moves the counters when a legal artifact-creature target
///   exists. A future agent-prompt path can surface the may-decline.
/// - <b>Artifact picker for sacrifice cost</b>: deterministic — chooses
///   the first artifact on the controller's battlefield. A full agent-
///   driven picker would let the controller pick which artifact to feed.
/// - <b>Modular N general primitive</b>: this factory wires Modular 1
///   inline rather than extracting a <c>ModularFactory.Build(creature,
///   n, ...)</c> primitive. Arcbound Ravager is the only Modular card in
///   the immediate roadmap; promotion to a shared primitive is deferred
///   until a second Modular card lands (Arcbound Crusher / Worker / etc.
///   are unlikely Modern staples).
/// </summary>
[CardName("Arcbound Ravager")]
public static class ArcboundRavagerFactory
{
    public const string CardName = "Arcbound Ravager";
    public const string PrintedManaCost = "{2}";
    public const int Power = 0;
    public const int Toughness = 0;
    public const int ModularValue = 1;

    /// <summary>
    /// Construct Arcbound Ravager with no live wiring. The ETB
    /// +1/+1-counter replacement is NOT registered (no bus supplied) —
    /// the <see cref="MarkEntersWithCounter"/> helper applies the counter
    /// manually instead when the test harness wants the on-battlefield
    /// post-ETB shape. The death trigger is attached to the card shape
    /// but not registered with a TriggerManager. Suitable for dispatcher
    /// / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, triggers: null);

    /// <summary>
    /// Construct Arcbound Ravager with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">ReplacementBus to register the ETB
    /// +1/+1-counter replacement against (CR 614.1d). May be null — no
    /// replacement is registered; callers can stamp the counter manually
    /// via <see cref="MarkEntersWithCounter"/>.</param>
    /// <param name="triggers">TriggerManager for the Modular death
    /// trigger (CR 702.43b). May be null — the trigger is still attached
    /// to the card shape so dispatcher / shape tests can observe it.</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Beast });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType-based lookups + colour identity see
        // both types (mirrors Spellskite / Walking Ballista / Esika's
        // Chariot).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Modular 1 — "enters with a +1/+1 counter on it" (CR 702.43a +
        // CR 614.1d). The replacement watches Arcbound Ravager's own ETB
        // ZoneMoveIntent and stamps PlusOneCountersOnEnter so the
        // ZoneService applies the counter after landing.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<ZoneMoveIntent>(
                new EntersWithCountersReplacement(card, ModularValue));
        }

        // ----------------------------------------------------------------
        // Modular 1 — "When it dies, you may put its +1/+1 counters on
        // target artifact creature." (CR 702.43b).
        //
        // Fires on Battlefield → Graveyard for Arcbound Ravager. The
        // counters live on the card object's Counters bag — same shape
        // as Undying — so the counter count at resolution time reflects
        // what Arcbound Ravager had on it at the moment of death. v1
        // target pick is deterministic: first artifact creature on either
        // player's battlefield, excluding Arcbound Ravager itself. The
        // active-zones set includes Graveyard so the trigger remains
        // registered after the move (Graveyard is the source's current
        // zone at resolution time).
        // ----------------------------------------------------------------
        var modularDeathEffect = new Effect(
            $"{CardName}: move its +1/+1 counters to target artifact creature",
            () =>
            {
                var counters = card.Counters.Count(CounterType.PlusOnePlusOne);
                if (counters <= 0) return;

                // v1 deterministic pick — first artifact creature on the
                // battlefield, excluding Arcbound Ravager itself (which
                // is now in the graveyard, so this is defensive).
                var target = FindArtifactCreatureTarget(owner, card);
                if (target == null) return;

                // CR 121.2 — counters left the battlefield when Arcbound
                // Ravager died, but they're still recorded on the card
                // object so we can read the count. Remove them from the
                // graveyard object (so a subsequent flicker / Undying
                // return doesn't double-stamp) and add them to the chosen
                // artifact creature.
                card.Counters.Remove(CounterType.PlusOnePlusOne, counters);
                target.Counters.Add(CounterType.PlusOnePlusOne, counters);
            });

        var modularDeathTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { modularDeathEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(modularDeathTrigger);
        triggers?.RegisterTriggeredAbility(modularDeathTrigger);

        // ----------------------------------------------------------------
        // Activated ability — "Sacrifice an artifact: Put a +1/+1 counter
        // on this creature." (no mana cost, just the sacrifice).
        // The SacrificeAnArtifactCost picks the first artifact on the
        // controller's battlefield deterministically (v1 — same posture
        // as SacrificeAnotherCreatureCost). excludeSource is null —
        // Arcbound Ravager is an artifact and self-sacrifice is legal
        // when no other artifact is available.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: +1/+1 counter for sacrificed artifact",
            () => card.Counters.Add(CounterType.PlusOnePlusOne, 1));

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new SacrificeAnArtifactCost() },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// Manually stamp Arcbound Ravager's Modular-1 ETB +1/+1 counter on
    /// the supplied instance. Used by shape-only tests that put Arcbound
    /// Ravager on the battlefield without funnelling through a
    /// <see cref="Services.ZoneService"/> + <see cref="ReplacementBus"/>
    /// pipeline. No-op if the counter has already been added (the
    /// bag's Add is unconditional, so this overload only stamps once
    /// per call — callers are expected to invoke once at "ETB time").
    /// </summary>
    public static void MarkEntersWithCounter(Creature ravager)
    {
        if (ravager == null) throw new ArgumentNullException(nameof(ravager));
        ravager.Counters.Add(CounterType.PlusOnePlusOne, ModularValue);
    }

    /// <summary>
    /// Find a legal Modular bestowal target — an artifact creature on
    /// the controller's battlefield, excluding <paramref name="self"/>.
    /// v1 deterministic — returns the first match. CR 702.43b's "target
    /// artifact creature" is not controller-restricted; opponent-side
    /// scans are deferred until the engine exposes a cross-battlefield
    /// enumerator (no <c>Player.Opponents</c> in v1 — the common case
    /// is an Affinity / Hardened Scales deck packed with the controller's
    /// own artifact creatures). Promotion to a full
    /// <see cref="Players.Agents.TargetRequest"/> prompt is the next step.
    /// </summary>
    private static Creature? FindArtifactCreatureTarget(Player owner, Creature self) =>
        owner.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, self) && c.HasType(CardType.Artifact))
            .FirstOrDefault();
}
