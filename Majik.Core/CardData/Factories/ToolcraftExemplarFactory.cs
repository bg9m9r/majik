using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Toolcraft Exemplar (Kaladesh, {W}).
///
/// Creature — Dwarf Artificer 1/1. Oracle text (verified against Scryfall
/// 2026-06-01):
///   "At the beginning of combat on your turn, if you control an artifact,
///    this creature gets +2/+1 until end of turn. If you control three or
///    more artifacts, it also gains first strike until end of turn."
///
/// A white aggressive one-drop that swings as a 3/2 (and, with three
/// artifacts, a 3/2 first striker) whenever you've got an artifact in play.
/// Base shape (name, Creature, Dwarf + Artificer subtypes, {W}, 1/1) is
/// materialised from the embedded JSON definition
/// (<c>toolcraft-exemplar.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the begin-combat self-pump
/// trigger is layered on top (the JSON <c>AbilityDefinition</c> schema does
/// not express begin-combat triggers — same posture as
/// <see cref="LegionWarbossFactory"/> / <see cref="PlatedGeopedeFactory"/>).
///
/// ## Implemented (v1)
/// - 1/1 Creature — Dwarf Artificer, mana cost {W}, owner / controller wired.
/// - <b>Begin-combat self-pump</b> (CR 508.1 — "At the beginning of combat
///   on your turn") wired as a <see cref="TriggeredAbility"/> over
///   <see cref="StepStartedEvent"/> for
///   <see cref="PhaseStateType.BeginningOfCombat"/> restricted to the
///   controller's own turns (<see cref="Triggers.OnStepBegin"/>).
/// - <b>Intervening-if (CR 603.4)</b> — "if you control an artifact". The
///   condition is re-checked on resolution: the +2/+1 only registers when
///   the controller controls at least one artifact (mirrors the begin-combat
///   intervening-if gate posture; artifact count read via the shared
///   <see cref="MasterOfEtheriumFactory.CountArtifactsControlled"/> helper,
///   CR 109.5 "you control").
/// - <b>Resolve — +2/+1 until end of turn</b>: registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2, +1) on the Exemplar's own
///   <see cref="Creature.ActiveEffects"/> (Layer 7c CR 613.1g; expiry
///   CR 514.2). When <see cref="Creature.ActiveEffects"/> is null (shape-only
///   tests with no live
///   <see cref="Majik.Core.Services.ContinuousEffectsService"/>) the
///   registration is a no-op — mirrors <see cref="PlatedGeopedeFactory"/>.
/// - <b>Conditional first strike</b> — "If you control three or more
///   artifacts, it also gains first strike until end of turn." (CR 702.7).
///   Read as a SEPARATE conditional within the same resolving ability, not a
///   second intervening-if: when the artifact count is &gt;= 3 a parallel
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>("First strike", Layer 6
///   CR 613.1c) is registered on the same ActiveEffects. The three-or-more
///   set is a superset of the one-or-more set, so first strike is only ever
///   granted alongside the +2/+1.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the trigger for inspection but does not register it with a
///   <see cref="TriggerManager"/>. Use the
///   <see cref="Create(Player, TriggerManager)"/> overload for live firing.
/// - <b>Begin-combat intervening-if "not in" check</b>: real MTG checks the
///   intervening-if both as the trigger would-be-put-on-the-stack AND on
///   resolution (CR 603.4). v1 collapses trigger-on-stack timing the way the
///   begin-combat trigger family does (the trigger condition itself does not
///   gate on artifact control — the gate is the resolution-time recheck),
///   which is observationally equivalent here because the pump is a no-op
///   when the controller has zero artifacts at resolution.
/// </summary>
[CardName("Toolcraft Exemplar")]
public static class ToolcraftExemplarFactory
{
    public const string CardName = "Toolcraft Exemplar";
    public const string Slug = "toolcraft-exemplar";

    /// <summary>Layer 7c +P/+T magnitude granted when the controller controls
    /// at least one artifact (CR 613.1g).</summary>
    public const int PumpPower = 2;
    public const int PumpToughness = 1;

    /// <summary>Artifact-count threshold for the first-strike grant.</summary>
    public const int FirstStrikeThreshold = 3;

    /// <summary>
    /// Construct Toolcraft Exemplar with no live <see cref="TriggerManager"/>
    /// wiring. The begin-combat self-pump trigger is attached for shape
    /// inspection but is not registered with a bus. Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Toolcraft Exemplar. When <paramref name="triggers"/> is
    /// supplied the begin-combat self-pump trigger is registered so a
    /// <see cref="StepStartedEvent"/> for the beginning of combat on the
    /// controller's turn automatically queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Dwarf + Artificer subtypes, {W}, 1/1). The JSON carries no
        // abilities — the begin-combat self-pump is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "At the beginning of combat on your turn, if you control an
        //  artifact, this creature gets +2/+1 until end of turn. If you
        //  control three or more artifacts, it also gains first strike
        //  until end of turn." (CR 508.1 begin-combat trigger; CR 603.4
        //  intervening-if rechecked at resolution.)
        //
        // Restricted to the controller's own turns via
        // Triggers.OnStepBegin(owner, BeginningOfCombat). The artifact-
        // control gate is enforced at resolution (the pump is self-targeted,
        // no TargetRequest).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: at beginning of combat, if you control an artifact, "
                + $"gets +{PumpPower}/+{PumpToughness}; with 3+ artifacts also gains first strike (until end of turn)",
            () =>
            {
                // ActiveEffects is null in shape-only tests (no live
                // ContinuousEffectsService) — no-op, mirroring Plated Geopede.
                if (card.ActiveEffects == null) return;

                // CR 603.4 — intervening-if rechecked on resolution. Count
                // artifacts the controller controls (CR 109.5). Self does NOT
                // count: Toolcraft Exemplar is a creature, not an artifact.
                var controller = card.Controller ?? owner;
                var artifacts = MasterOfEtheriumFactory.CountArtifactsControlled(controller);

                // "if you control an artifact" — at least one.
                if (artifacts < 1) return;

                // "this creature gets +2/+1 until end of turn" (Layer 7c,
                // CR 613.1g; expiry CR 514.2).
                card.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(card, PumpPower, PumpToughness));

                // "If you control three or more artifacts, it also gains
                // first strike until end of turn." (CR 702.7 — Layer 6 keyword
                // grant, CR 613.1c.)
                if (artifacts >= FirstStrikeThreshold)
                {
                    card.ActiveEffects.Register(
                        new GrantKeywordUntilEndOfTurnEffect(card, "First strike"));
                }
            });

        var beginCombatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, PhaseStateType.BeginningOfCombat),
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(beginCombatTrigger);
        triggers?.RegisterTriggeredAbility(beginCombatTrigger);

        return card;
    }
}
