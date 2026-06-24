using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Undying Malice (Modern Horizons 3, {B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Until end of turn, target creature gains 'When this creature dies,
///    return it to the battlefield tapped under its owner's control with a
///    +1/+1 counter on it.'"
///
/// ## Relationship to intrinsic Undying (CR 702.93b)
/// The granted ability is a one-shot, turn-scoped variant of Undying
/// (<see cref="Majik.Core.Keywords.UndyingFactory"/> — Young Wolf). Two
/// deliberate deltas from the printed keyword:
///   - the creature returns <b>tapped</b> (intrinsic Undying returns it
///     untapped);
///   - there is <b>no</b> "if it had no +1/+1 counters on it" intervening-if
///     — Undying Malice returns the creature unconditionally on the first
///     death while the grant is live.
/// Because of those deltas this factory builds the death-trigger inline
/// rather than reusing <see cref="Majik.Core.Keywords.UndyingFactory.Build"/>.
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {B}. Card shape comes from the
///   embedded JSON (<c>undying-malice.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same load path as
///   <see cref="BlossomingDefenseFactory"/>).
/// - <see cref="BuildDefinition"/> wires a single 1..1 "target creature"
///   <see cref="TargetRequest"/>. On resolve (CR 608.2b illegal-target guard
///   first), <see cref="Resolve(object)"/> registers a self-sourced
///   <see cref="GrantAbilityEffect"/> (CR 613.1f Layer-6 ability grant) that
///   adds the death-trigger to the target, expiring at end of turn
///   (CR 514.2). Self-sourced (source == the granted creature) so the grant
///   survives the spell card leaving the stack — same posture as
///   <see cref="BraveTheElementsFactory.Resolve"/>.
/// - The granted death-trigger fires on a Battlefield → Graveyard
///   <see cref="CardMovedEvent"/> for the bearer (CR 700.4 "dies"); on
///   resolution it raw-moves the creature graveyard → battlefield under its
///   owner's control, clears the counter bag (CR 121.2 — counters do not
///   persist across zone changes), adds exactly one
///   <see cref="CounterType.PlusOnePlusOne"/> counter, and taps it. The
///   live <see cref="TriggerManager"/> auto-binds the granted trigger off the
///   death <see cref="CardMovedEvent"/> (see
///   <see cref="TriggerManager"/>'s OnAnyEvent auto-bind), so no explicit
///   registration of the granted ability is required.
/// </summary>
[CardName("Undying Malice")]
public static class UndyingMaliceFactory
{
    public const string CardName = "Undying Malice";
    public const string Slug = "undying-malice";
    public const string PrintedManaCost = "{B}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve <see cref="SpellDefinition"/>. Single 1..1 "target
    /// creature" request, no X. On resolution the targeted creature gains the
    /// Undying-Malice death-trigger until end of turn (CR 514.2); a no-longer-
    /// legal target no-ops (CR 608.2b).
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Undying Malice — target creature gains a dies → return-tapped-with-+1/+1 trigger until end of turn",
                        () => Resolve(raw)),
                };
            });

    /// <summary>
    /// Grant the Undying-Malice death-trigger to <paramref name="raw"/> until
    /// end of turn (CR 613.1f Layer-6 grant, CR 514.2 EOT expiry). Exposed for
    /// direct invocation by tests / bots without driving the full cast flow.
    /// </summary>
    public static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        // CR 613.1f — Layer-6 ability grant. Self-sourced (the granted creature
        // is the effect source) so the grant outlives the spell card leaving
        // the stack; expires at end of turn (CR 514.2).
        var grant = new GrantAbilityEffect(
            source: target,
            target: target,
            ability: BuildDeathTrigger(target),
            expiresAtEndOfTurn: true);
        target.ActiveEffects.Register(grant);
        // Materialise the grant on the same priority window so the death-trigger
        // is attached immediately (CR 700.2a).
        grant.Sync();
    }

    /// <summary>
    /// Build the granted death-trigger: "When this creature dies, return it to
    /// the battlefield tapped under its owner's control with a +1/+1 counter on
    /// it." Modelled on <see cref="Majik.Core.Keywords.UndyingFactory.Build"/>
    /// but (a) returns the creature TAPPED and (b) carries NO intervening-if —
    /// the return is unconditional on the first death while the grant is live.
    /// </summary>
    private static TriggeredAbility BuildDeathTrigger(Creature source)
    {
        // CR 700.4 — "dies" = Battlefield → Graveyard for this creature.
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            ReferenceEquals(e.Card, source)
            && e.FromZone == ZoneType.Battlefield
            && e.ToZone == ZoneType.Graveyard);

        var effect = new Effect(
            "Undying Malice — return to battlefield tapped with a +1/+1 counter",
            () =>
            {
                // Guard: creature must still be in the graveyard (a replacement
                // could have moved it first; rare).
                if (source.Zone != ZoneType.Graveyard) return;

                var owner = source.Owner;
                if (owner == null) return;

                // Return to the battlefield under its owner's control.
                owner.Zones.Graveyard.RemoveCard(source);
                owner.Zones.Battlefield.AddCard(source);
                source.SetZone(ZoneType.Battlefield);
                source.SetController(owner);

                // CR 121.2 — counters left the battlefield when the creature
                // died. Clear the bag so the +1/+1 counter is the only one.
                foreach (var entry in source.Counters.All.ToList())
                {
                    source.Counters.Remove(entry.Key, entry.Value);
                }

                // One +1/+1 counter (the granted ability's text).
                source.Counters.Add(CounterType.PlusOnePlusOne, 1);

                // "tapped" — return it tapped. Guard against an already-tapped
                // state (a fresh graveyard object is untapped, but Tap() throws
                // if already tapped, so stay defensive).
                if (!source.IsTapped)
                {
                    source.Tap();
                }

                // ETB bookkeeping (re-stamp entry timestamp for the legend rule).
                source.MarkEnteredBattlefield();
            });

        // No intervening-if — Undying Malice returns the creature
        // unconditionally. activeZones includes Graveyard so the trigger stays
        // bound after ZoneService stamps Zone = Graveyard before publishing the
        // CardMovedEvent (mirrors UndyingFactory).
        return new TriggeredAbility(
            source,
            source.Controller ?? source.Owner
                ?? throw new InvalidOperationException("Undying Malice target must have a controller or owner"),
            condition,
            effects: new[] { effect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
    }
}
