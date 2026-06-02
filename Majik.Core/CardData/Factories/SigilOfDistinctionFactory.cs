using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sigil of Distinction (Future Sight, {X}).
///
/// Artifact — Equipment. Oracle text (verified against Scryfall 2026-06-02):
///   "This Equipment enters with X charge counters on it."
///   "Equipped creature gets +1/+1 for each charge counter on this Equipment."
///   "Equip—Remove a charge counter from this Equipment."
///
/// ## Why a hand-rolled C# factory (not a pure JSON CardDefinition)
///
/// The data-driven <see cref="CardDefinitionFactory"/> only materialises the
/// effect/ability shapes enumerated in its dispatch (counters, draw,
/// scry/surveil, stub damage, …). It has NO dynamic attached-boost effect,
/// NO equip ability, and NO enters-with-X-counters primitive — a JSON-only
/// def would produce just a vanilla {X} Artifact shell. The functioning
/// equipment analogues (<see cref="CranialPlatingFactory"/>,
/// <see cref="NettlecystFactory"/>) are themselves hand-rolled for exactly
/// this reason, so Sigil of Distinction follows that established pattern: the
/// base shape (name, Artifact, Equipment subtype, {X}) is materialised from
/// the embedded JSON definition (<c>sigil-of-distinction.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the three abilities are layered
/// on here.
///
/// ## Implementation
///
/// - <b>"This Equipment enters with X charge counters on it"</b> — the engine
///   has no per-cast X ledger, so this mirrors
///   <see cref="EngineeredExplosivesFactory"/>'s Sunburst approximation: an
///   ETB-self <see cref="TriggeredAbility"/> reads a caller-supplied
///   <c>Func&lt;int&gt; xValueProvider</c> and adds that many
///   <see cref="CounterType.Charge"/> counters (CR 122.1). The strictly
///   correct rules model is "enters with N counters" as a replacement effect
///   (CR 614.13 / CR 122.6), but the engine has no enters-with-counters
///   replacement primitive yet (same deferral as Engineered Explosives /
///   Walking Ballista); the ETB-trigger approximation lands the counters on
///   the battlefield. The single-arg dispatcher / shape path provides X=0 —
///   Sigil enters with no counters there ("cast for X=0").
/// - <b>Static "+1/+1 for each charge counter on this Equipment"</b> — the
///   dynamic-N <see cref="AttachedBoostEffect"/> overload (Layer 7c, CR 613
///   Layer 7c). Both the power and toughness closures sample the SAME live
///   charge-counter count on the Sigil, so the bonus is symmetric +N/+N
///   (contrast Cranial Plating's +N/+0). Note this counts the counters on the
///   Equipment itself — NOT a controller-wide artifact scan — so paying the
///   equip cost (which removes a counter) shrinks the boost. Reads
///   <see cref="Permanent.AttachedTo"/> dynamically so re-equipping transfers
///   the boost without re-registration;
///   <see cref="AttachedBoostEffect.IsActive"/> gates on being on the
///   battlefield AND attached.
/// - <b>"Equip—Remove a charge counter from this Equipment"</b> — the equip
///   cost here is NOT mana, so the shared <see cref="EquipActivatedAbility"/>
///   primitive (which only models a mana equip cost) does not fit. Instead
///   this is a plain <see cref="ActivatedAbility"/> built like Cranial
///   Plating's hand-rolled attach ability, but with
///   <see cref="ActivatedAbility.IsSorcerySpeed"/> = true (CR 702.6e — equip
///   is a sorcery-speed activation) and a single
///   <see cref="RemoveChargeCounterCost"/> as its cost. On resolution the
///   Sigil attaches to the chosen "creature you control" (CR 702.6b) via
///   <see cref="Permanent.AttachTo"/>, which unequips-first automatically
///   (CR 701.3). v1 picker is deterministic: the first creature on the
///   controller's battlefield when no agent target is supplied — same posture
///   as the rest of the equipment cycle.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits all service
/// wiring and produces the correct card shape only (factory-shape / dispatch
/// tests). The ETB trigger is attached for shape but not registered with a
/// <see cref="TriggerManager"/>; the dynamic boost is not registered against
/// any <see cref="ContinuousEffectsService"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Enters-with-X provenance</b>: the engine does not track a spell's
///   cast-time X, so the ETB-counter count is caller-supplied (default 0) —
///   identical posture to Engineered Explosives' Sunburst X provider.
/// - <b>Equip target prompt</b>: v1 picks the first controller-side creature
///   deterministically when no agent target is supplied (same gap as the rest
///   of the equipment cycle).
/// </summary>
[CardName("Sigil of Distinction")]
public static class SigilOfDistinctionFactory
{
    public const string CardName = "Sigil of Distinction";
    public const string Slug = "sigil-of-distinction";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Constructs Sigil of Distinction with no live runtime wiring (the shape
    /// / dispatcher path). Enters with zero charge counters (the single-arg
    /// path can't know X); the dynamic boost is not registered against any
    /// <see cref="ContinuousEffectsService"/>; the ETB trigger is attached but
    /// not registered with a <see cref="TriggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, xValueProvider: null, triggers: null);

    /// <summary>
    /// Constructs Sigil of Distinction with optional continuous-effects wiring
    /// (the +N/+N boost). Convenience overload used by boost-focused tests;
    /// the ETB-counter trigger is left unwired and X defaults to 0.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, xValueProvider: null, triggers: null);

    /// <summary>
    /// Constructs Sigil of Distinction with optional runtime services. When
    /// <paramref name="continuousEffects"/> is supplied the +N/+N boost
    /// (Layer 7c, N = charge counters on the Sigil) is registered against it.
    /// When <paramref name="xValueProvider"/> is supplied the
    /// enters-with-X-charge-counters ETB effect adds that many counters when
    /// executed (callers wire this to the cast-time X value). When
    /// <paramref name="triggers"/> is supplied the ETB-counter trigger is
    /// registered for bus-driven firing.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<int>? xValueProvider,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Artifact, Equipment subtype, {X}) from the
        // embedded JSON definition.
        var card = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // "This Equipment enters with X charge counters on it."
        // CR 122.1 / CR 614.13. The engine has no enters-with-counters
        // replacement primitive and no cast-time-X ledger, so this is the
        // Engineered-Explosives Sunburst approximation: an ETB-self trigger
        // reads a caller-supplied X provider and adds that many charge
        // counters. X defaults to 0 on the shape-only path.
        // --------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enter with X charge counters",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var x = xValueProvider?.Invoke() ?? 0;
                if (x <= 0) return;
                card.Counters.Add(CounterType.Charge, x);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // --------------------------------------------------------------
        // Static "Equipped creature gets +1/+1 for each charge counter on
        // this Equipment." Dynamic-N AttachedBoostEffect samples the Sigil's
        // OWN live charge-counter count at each layer pass (CR 613 Layer 7c).
        // Both stats read the SAME count → symmetric +N/+N. Removing a
        // counter to pay the equip cost shrinks the boost.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                powerFn: () => CountChargeCounters(card),
                toughnessFn: () => CountChargeCounters(card)));
        }

        // --------------------------------------------------------------
        // "Equip—Remove a charge counter from this Equipment."
        // CR 702.6 — equip is a sorcery-speed activation (CR 702.6e). The
        // equip cost is NOT mana, so EquipActivatedAbility (mana-only) does
        // not fit; build a plain sorcery-speed ActivatedAbility with a single
        // RemoveChargeCounterCost. On resolution attach to the chosen
        // "creature you control" (CR 702.6b); Permanent.AttachTo unequips-
        // first automatically (CR 701.3). Deterministic first-creature
        // fallback when no agent target is supplied (CR 608.2b — no legal
        // bearer → no-op), matching the rest of the equipment cycle.
        // --------------------------------------------------------------
        ActivatedAbility? equipAbility = null;
        var equipEffect = new Effect(
            $"{CardName}: equip — attach to target creature you control",
            () =>
            {
                var ctrl = card.Controller ?? card.Owner ?? owner;

                Creature? bearer = null;
                if (equipAbility != null
                    && equipAbility.ChosenTargets.Count > 0
                    && equipAbility.ChosenTargets[0].Count > 0
                    && equipAbility.ChosenTargets[0][0] is Creature chosen
                    && ReferenceEquals(chosen.Controller, ctrl))
                {
                    bearer = chosen;
                }

                bearer ??= ctrl.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => ReferenceEquals(c.Controller, ctrl));

                if (bearer == null) return; // CR 608.2b — no legal target → no-op.
                card.AttachTo(bearer);
            });

        var equipTargetRequest = new TargetRequest(
            Description: "Attach to target creature you control",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            CandidateGatherer: _ =>
            {
                var ctrl = card.Controller ?? card.Owner ?? owner;
                return ctrl.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(c => ReferenceEquals(c.Controller, ctrl))
                    .Cast<object>()
                    .ToList();
            });

        equipAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new RemoveChargeCounterCost(card) },
            effects: new IEffect[] { equipEffect },
            targetRequests: new[] { equipTargetRequest },
            sorcerySpeed: true);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// Live count of charge counters on the Sigil itself (CR 613 Layer 7c
    /// source-of-truth). Defaults to 0 cleanly when the Sigil has no counters
    /// or has left the battlefield (its Counters bag persists on the object
    /// but the boost gates off-battlefield via
    /// <see cref="AttachedBoostEffect.IsActive"/>).
    /// </summary>
    public static int CountChargeCounters(Permanent equipment)
        => equipment.Counters.Count(CounterType.Charge);
}
