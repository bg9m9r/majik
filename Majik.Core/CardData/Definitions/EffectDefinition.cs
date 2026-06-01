using System.Text.Json.Serialization;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Discriminated-union base for activated-ability effect specs. JSON
/// discriminator: <c>type</c>. New variants register via
/// <see cref="JsonDerivedTypeAttribute"/> + a matching switch arm in
/// <see cref="CardDefRuntime.BuildJsonEffect"/> (reached via
/// <see cref="ToResolveEffect"/>).
///
/// Build semantics: each variant produces an
/// <see cref="Majik.Core.Abilities.IEffect"/> whose lambda captures the
/// live <c>card</c> + <c>controller</c> closure from the build context.
///
/// Convergence (PLAN 03): this JSON union is a serialization of the same
/// effect shapes the fluent <see cref="CardDef"/> DSL emits; both compile
/// down to the shared <see cref="Majik.Core.Primitives.Fx"/> primitive
/// vocabulary (cost shapes → <see cref="Majik.Core.Primitives.Costs"/>,
/// triggers → <see cref="Majik.Core.Abilities.Triggers"/>). New verbs
/// should reach for an existing <c>Fx.*</c> helper (or add one, additively)
/// rather than inlining fresh resolve logic here.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PutCounterEffectDef), "put_counter")]
[JsonDerivedType(typeof(DealDamageEffectDef), "deal_damage")]
[JsonDerivedType(typeof(DrawCardEffectDef), "draw_card")]
[JsonDerivedType(typeof(SurveilSelfEffectDef), "surveil_self")]
[JsonDerivedType(typeof(ScrySelfEffectDef), "scry_self")]
[JsonDerivedType(typeof(DestroyTargetEffectDef), "destroy_target")]
[JsonDerivedType(typeof(UntapTargetEffectDef), "untap_target")]
[JsonDerivedType(typeof(PreventDamageTargetEffectDef), "prevent_damage_target")]
[JsonDerivedType(typeof(GainLifeSelfEffectDef), "gain_life_self")]
[JsonDerivedType(typeof(MillThenPickFirstMatchingToHandEffectDef), "mill_then_pick_first_matching_to_hand")]
[JsonDerivedType(typeof(ConniveSelfEffectDef), "connive_self")]
[JsonDerivedType(typeof(AmassSelfEffectDef), "amass_self")]
public abstract class EffectDefinition
{
    /// <summary>
    /// Map this JSON effect onto a canonical effect-builder closure (PLAN 03
    /// S2). The closure defers to <see cref="CardDefRuntime.BuildJsonEffect"/>
    /// (which routes through the shared <see cref="Majik.Core.Primitives.Fx"/>
    /// vocabulary) against the live card / controller / replacement bus, so
    /// the produced <see cref="Majik.Core.Effects.IEffect"/> is byte-identical
    /// to the legacy direct build. Named <c>ToResolveEffect</c> per the PLAN
    /// 03 convergence vocabulary (the effect is the in-memory resolve unit).
    ///
    /// <para>
    /// PLAN 01 (Slice F) — the closure also receives a
    /// <paramref name="targetRequestIndex" />: the slot into the resolving
    /// ability's <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/>
    /// that a targeted effect reads its chosen target from. Untargeted effects
    /// ignore it; targeted effects (<see cref="DealDamageEffectDef"/> /
    /// <see cref="DestroyTargetEffectDef"/> / <see cref="UntapTargetEffectDef"/> /
    /// <see cref="PreventDamageTargetEffectDef"/>)
    /// emit a matching <see cref="ToTargetRequest"/> so the ability declares
    /// the target and the shared <see cref="Majik.Core.Targeting.TargetCollection"/>
    /// pipeline collects it.
    /// </para>
    /// </summary>
    public Func<Majik.Core.Cards.ICard, Majik.Core.Players.Player, Majik.Core.Effects.ReplacementBus?, int, Majik.Core.Abilities.IEffect> ToResolveEffect() =>
        (card, controller, replacements, targetRequestIndex) =>
            CardDefRuntime.BuildJsonEffect(this, card, controller, replacements, targetRequestIndex);

    /// <summary>
    /// PLAN 01 (Slice F) — the targeting declaration this effect needs, or
    /// <c>null</c> for an untargeted effect. Targeted effects translate their
    /// <c>TargetFilter</c> string into a <see cref="Majik.Core.Players.Agents.TargetRequest"/>
    /// (cardinality + a <see cref="Majik.Core.Players.Agents.TargetRequest.CandidateGatherer"/>
    /// over the right zone/predicate) which the runtime attaches to the
    /// owning ability's <c>TargetRequests</c>. The shared
    /// <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline then prompts
    /// the controller's agent and stores the pick on
    /// <see cref="Majik.Core.Abilities.ActivatedAbility.ChosenTargets"/> — the
    /// same path a hand-written factory uses. Default: untargeted.
    /// </summary>
    public virtual Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() => null;
}

/// <summary>Add N counters of the given type to a target permanent.
/// v1 only supports <c>"target": "self"</c>; "target creature" + chosen
/// targets plug in when the targeting system is wired.</summary>
public sealed class PutCounterEffectDef : EffectDefinition
{
    public string Counter { get; set; } = "+1/+1";
    public int Amount { get; set; } = 1;
    public string Target { get; set; } = "self";
}

/// <summary>
/// "Deals N damage to [target]" — real targeted effect (PLAN 01 Slice F,
/// replaces the former <c>deal_damage_stub</c> no-op). At resolution the
/// effect reads the chosen target off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and
/// routes <see cref="Amount"/> damage through
/// <see cref="Majik.Core.Primitives.Fx.DealDamageAny"/> (Player / Creature /
/// Planeswalker). CR 608.2b — illegal target at resolution fizzles the
/// damage. Canonical case: Walking Ballista's "Remove a +1/+1 counter:
/// It deals 1 damage to any target."
///
/// <see cref="Target"/> is the filter string (<c>"any"</c> /
/// <c>"creature"</c> / <c>"creature_or_player"</c>) the runtime translates
/// into a <see cref="Majik.Core.Players.Agents.TargetRequest"/>.
/// </summary>
public sealed class DealDamageEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
    public string Target { get; set; } = "any";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(Target, $"deal {Amount} damage");
}

/// <summary>
/// "Controller draws N cards." Default <see cref="Amount"/> is 1.
/// At resolution time the effect closure removes the top N cards from
/// the controller's library and moves them to hand. Empty library is a
/// silent no-op here; state-based-action loss handling is elsewhere.
/// </summary>
public sealed class DrawCardEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
}

/// <summary>
/// "Surveil N" — controller looks at the top N cards of their library
/// and may put any number of them into their graveyard, leaving the
/// rest in any order on top (CR 701.42). Consults the registered agent
/// for the player's decision; falls back to the all-to-graveyard
/// default when no agent is registered (matches the existing C#
/// Underground Mortuary path).
/// </summary>
public sealed class SurveilSelfEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
}

/// <summary>
/// "Scry N" — controller looks at the top N cards of their library, then
/// puts any number of them on the bottom of their library and the rest
/// back on top in any order (CR 701.20). Consults the registered agent
/// for the player's decision; falls back to the all-to-bottom default
/// when no agent is registered (matches the <see cref="ScriptedAgent"/> /
/// <see cref="Majik.Core.Players.Agents.DeterministicBotAgent"/> default
/// scry policy). Wraps the existing <see cref="Majik.Core.Keywords.ScryAction"/>
/// keyword action — exact parallel of <see cref="SurveilSelfEffectDef"/>.
/// Canonical case: the Theros scry-land cycle (e.g. Temple of Triumph,
/// "When this land enters, scry 1.").
/// </summary>
public sealed class ScrySelfEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
}

/// <summary>
/// "Destroy target [filter]" — real targeted effect (PLAN 01 Slice F,
/// replaces the former <c>destroy_target_stub</c> no-op). At resolution the
/// effect reads the chosen permanent off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and
/// destroys it via <see cref="Majik.Core.Primitives.Fx.MoveToGraveyard(Majik.Core.Cards.ICard, Majik.Core.Zones.ZoneMoveReason)"/>
/// with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (Indestructible
/// / regeneration gates apply — CR 702.12 / 701.15). CR 608.2b — illegal
/// target at resolution fizzles. Canonical case: Boseiju, Who Endures —
/// "Destroy target artifact, enchantment, or nonbasic land."
///
/// <see cref="TargetFilter"/> is the filter string the runtime translates
/// into a <see cref="Majik.Core.Players.Agents.TargetRequest"/> (e.g.
/// <c>"artifact_enchantment_nonbasic_land"</c>, <c>"nonbasic_land"</c>,
/// <c>"permanent"</c>).
/// </summary>
public sealed class DestroyTargetEffectDef : EffectDefinition
{
    public string TargetFilter { get; set; } = "permanent";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(TargetFilter, "destroy");
}

/// <summary>
/// "Untap target [filter]" — real targeted effect (PLAN 01 Slice F,
/// replaces the former <c>untap_target_stub</c> no-op). At resolution the
/// effect reads the chosen permanent off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and
/// untaps it (CR 701.21, <c>Permanent.Untap</c>). CR 608.2b — illegal target
/// at resolution fizzles. Canonical case: Minamo, School at Water's Edge —
/// <c>"{U}, {T}: Untap target legendary permanent."</c>
///
/// <see cref="TargetFilter"/> is the filter string the runtime translates
/// into a <see cref="Majik.Core.Players.Agents.TargetRequest"/> (e.g.
/// <c>"legendary_permanent"</c>, <c>"permanent"</c>).
/// </summary>
public sealed class UntapTargetEffectDef : EffectDefinition
{
    public string TargetFilter { get; set; } = "permanent";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(TargetFilter, "untap");
}

/// <summary>
/// "Prevent the next N damage that would be dealt to target [filter] this
/// turn" — real targeted effect (PLAN 01 Slice F, replaces the former
/// <c>prevent_damage_target_stub</c> no-op). At resolution the effect reads
/// the chosen creature off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and
/// registers a CR 615 prevention shield bound to that creature
/// (<see cref="Majik.Core.Effects.PreventNextNDamageToCreatureShield"/>) on
/// the controller-attached <see cref="Majik.Core.Effects.ReplacementBus"/>.
/// The shield soaks up to <see cref="Amount"/> damage points aimed at the
/// chosen creature this turn and auto-expires at cleanup (CR 514.2). CR 608.2b
/// — an illegal target at resolution fizzles (no shield registered).
///
/// Canonical case: Eiganjo Castle — <c>"{W}, {T}: Prevent the next 2 damage
/// that would be dealt to target legendary creature this turn."</c>
///
/// <see cref="TargetFilter"/> is the filter string the runtime translates into
/// a <see cref="Majik.Core.Players.Agents.TargetRequest"/> (e.g.
/// <c>"legendary_creature"</c>, <c>"creature"</c>).
/// </summary>
public sealed class PreventDamageTargetEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
    public string TargetFilter { get; set; } = "creature";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(TargetFilter, $"prevent {Amount} damage to");
}

/// <summary>"Controller gains N life." Default <see cref="Amount"/> = 1.</summary>
public sealed class GainLifeSelfEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
}

/// <summary>
/// "Mill N cards. You may put a card with one of the given types from
/// among the milled cards into your hand." Auto-picks the first
/// qualifying milled card in v1 (the "may" opt-out awaits agent
/// prompting). Matches the Dredger's Insight ETB effect.
///
/// <see cref="MatchingTypes"/> are parsed as
/// <see cref="Majik.Core.Cards.Types.CardType"/>; OR-matched.
/// </summary>
public sealed class MillThenPickFirstMatchingToHandEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
    public List<string> MatchingTypes { get; set; } = new();
}

/// <summary>
/// "Connive" applied to the source creature (CR 701.50). The source's
/// controller draws a card, then discards a card; if the discarded card
/// was nonland, a +1/+1 counter is placed on the source. Source must be
/// a <see cref="Majik.Core.Cards.Creature"/>; otherwise the effect is a
/// silent no-op. Uses the v1 deterministic discard-pick policy of
/// <see cref="Majik.Core.Keywords.ConniveAction"/> (most-recent card in hand).
/// <see cref="Amount"/> &gt; 1 repeats the routine that many times
/// (the "Connive X" form).
/// </summary>
public sealed class ConniveSelfEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
}

/// <summary>
/// "Amass [tribe] N" resolved for the source's controller (CR 701.49).
/// If the controller has no Army on the battlefield, creates a 0/0
/// black [tribe] Army creature token. Then puts N +1/+1 counters on
/// an Army the controller controls (v1 auto-picks the first Army found
/// on the battlefield). <see cref="Tribe"/> is the secondary creature
/// subtype on the printed Amass card (e.g. "Zombie", "Orc", "Goblin").
/// When the value resolves to <see cref="Majik.Core.Cards.Types.CardSubtype.Army"/>
/// the created token has only the Army subtype.
/// </summary>
public sealed class AmassSelfEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
    public string Tribe { get; set; } = "Zombie";
}
