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
[JsonDerivedType(typeof(ExileTargetEffectDef), "exile_target")]
[JsonDerivedType(typeof(ExileUntilLeavesEffectDef), "exile_until_leaves")]
[JsonDerivedType(typeof(ReturnToHandEffectDef), "return_to_hand")]
[JsonDerivedType(typeof(UntapTargetEffectDef), "untap_target")]
[JsonDerivedType(typeof(TapTargetEffectDef), "tap_target")]
[JsonDerivedType(typeof(PreventDamageTargetEffectDef), "prevent_damage_target")]
[JsonDerivedType(typeof(GainLifeSelfEffectDef), "gain_life_self")]
[JsonDerivedType(typeof(LoseLifeSelfEffectDef), "lose_life_self")]
[JsonDerivedType(typeof(LoseLifeTargetEffectDef), "lose_life_target")]
[JsonDerivedType(typeof(MillThenPickFirstMatchingToHandEffectDef), "mill_then_pick_first_matching_to_hand")]
[JsonDerivedType(typeof(ConniveSelfEffectDef), "connive_self")]
[JsonDerivedType(typeof(AmassSelfEffectDef), "amass_self")]
[JsonDerivedType(typeof(GainControlEffectDef), "gain_control")]
[JsonDerivedType(typeof(FightEffectDef), "fight")]
[JsonDerivedType(typeof(ExploreTargetEffectDef), "explore_target")]
[JsonDerivedType(typeof(PumpTargetEffectDef), "pump_target")]
[JsonDerivedType(typeof(GrantKeywordUntilEotTargetEffectDef), "grant_keyword_until_eot_target")]
[JsonDerivedType(typeof(BecomesArtifactTargetEffectDef), "becomes_artifact_target")]
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
    /// pipeline collects it (<see cref="ReturnToHandEffectDef"/> joins the same
    /// targeted family — a declarative bounce onto <c>Fx.BounceToHand</c>).
    /// </para>
    /// </summary>
    public Func<Majik.Core.Cards.ICard, Majik.Core.Players.Player, Majik.Core.Effects.ReplacementBus?, int, Majik.Core.Abilities.IEffect> ToResolveEffect() =>
        (card, controller, replacements, targetRequestIndex) =>
            CardDefRuntime.BuildJsonEffect(this, card, controller, replacements, targetRequestIndex);

    /// <summary>
    /// Continuous-effects-aware overload of <see cref="ToResolveEffect()"/>.
    /// Identical to the base closure, but additionally captures the live
    /// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> so verbs that
    /// register a CR 613 continuous effect (currently
    /// <see cref="GainControlEffectDef"/> — the Threaten / Act of Treason
    /// "gain control until end of turn" family, which registers a
    /// <see cref="Majik.Core.Effects.TemporaryControlChangeEffect"/> + an
    /// until-EOT haste grant) can reach it at resolution. Threaded through the
    /// SPELL path (<see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>)
    /// exactly as the <see cref="Majik.Core.Effects.ReplacementBus"/> already is;
    /// untargeted/non-control verbs ignore the extra capture, so the produced
    /// effect is byte-identical to the base path for them.
    /// </summary>
    public Func<Majik.Core.Cards.ICard, Majik.Core.Players.Player, Majik.Core.Effects.ReplacementBus?, int, Majik.Core.Abilities.IEffect> ToResolveEffect(
        Majik.Core.Effects.ContinuousEffectsService? continuous) =>
        (card, controller, replacements, targetRequestIndex) =>
            CardDefRuntime.BuildJsonEffect(
                this, card, controller, replacements, targetRequestIndex, continuous);

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

    /// <summary>
    /// A <b>rider</b> verb that reuses the immediately-preceding targeted
    /// effect's chosen target instead of declaring a fresh target slot
    /// (default: <c>false</c>). The canonical case is Vapor Snag's
    /// "Return target creature to its owner's hand. Its controller loses 1
    /// life." — the second clause's "its controller" refers to the SAME
    /// creature the bounce targeted, so the spell has exactly one target.
    ///
    /// <para>
    /// When <c>true</c>, the effect contributes no <see cref="TargetRequest"/>
    /// (so <see cref="ToTargetRequest"/> must return <c>null</c>) and the
    /// spell / ability bridge wires its resolution-time
    /// <c>targetRequestIndex</c> to the slot the most-recent targeted effect
    /// reserved. CR 608.2b — if that shared target is illegal at resolution,
    /// both the host effect and the rider fizzle together.
    /// </para>
    /// </summary>
    public virtual bool SharesPreviousTargetSlot => false;

    /// <summary>
    /// A SECOND target slot this effect declares, immediately following its
    /// primary <see cref="ToTargetRequest"/>, or <c>null</c> for the common
    /// single-target / untargeted case (the default). The only verb that needs
    /// two own target slots is <see cref="FightEffectDef"/> in its
    /// <c>source: "target"</c> mode — "target creature you control fights
    /// target creature you don't control" (CR 701.12) — where the FIGHTER and
    /// the OTHER creature are two distinct targets the spell announces.
    ///
    /// <para>
    /// When non-null, <see cref="ToTargetRequest"/> MUST also be non-null (the
    /// extra slot is the second of an ordered pair), and at resolution the
    /// effect reads its primary pick at <c>targetRequestIndex</c> and its extra
    /// pick at <c>targetRequestIndex + 1</c>. Both the ability materializer
    /// (<see cref="CardDefAbilityEffects.Materialize"/>) and the spell bridge
    /// (<see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>) append the
    /// extra request right after the primary so the two slots are contiguous.
    /// </para>
    /// </summary>
    public virtual Majik.Core.Players.Agents.TargetRequest? ToExtraTargetRequest() => null;
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
/// "Exile target [filter]" — real targeted exile effect (CR 701.21). The
/// mirror of <see cref="DestroyTargetEffectDef"/> onto the exile primitive
/// (<see cref="Majik.Core.Primitives.Fx.MoveToExile(Majik.Core.Cards.ICard)"/>).
/// At resolution the effect reads the chosen target off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and exiles
/// it. Unlike destroy, exile bypasses Indestructible (CR 702.12) and
/// regeneration (CR 701.15) — the card moves regardless.
///
/// <para>
/// CR 608.2b — at resolution the effect re-checks the SAME
/// <see cref="TargetFilter"/> predicate the candidate gatherer used (not just
/// battlefield presence), so a conditional filter (e.g. <c>nonbasic_land</c>,
/// <c>black_or_red_permanent</c>) fizzles cleanly when the target no longer
/// matches — even if the agent hands an off-filter object directly. The
/// graveyard filters (<c>card_in_graveyard</c> /
/// <c>creature_card_in_graveyard</c>) reuse the same machinery, so "exile
/// target card from a graveyard" (Soul Guide Lantern / Scavenging Ooze's
/// piece / Boggart Trawler-style) resolves through the SAME verb — the only
/// zone difference is the predicate (graveyard vs battlefield), handled by
/// <see cref="TargetFilters"/>.
/// </para>
///
/// <see cref="TargetFilter"/> is the filter string the runtime translates into
/// a <see cref="Majik.Core.Players.Agents.TargetRequest"/> (e.g.
/// <c>"permanent"</c>, <c>"creature"</c>, <c>"nonbasic_land"</c>,
/// <c>"card_in_graveyard"</c>, <c>"creature_card_in_graveyard"</c>).
/// </summary>
public sealed class ExileTargetEffectDef : EffectDefinition
{
    public string TargetFilter { get; set; } = "permanent";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(
            TargetFilter, "exile", Majik.Core.Cards.BotIntent.Removal);
}

/// <summary>
/// "When [this] enters, exile target [filter] until [this] leaves the
/// battlefield." — the Banishing Light / Oblivion Ring / Glass Casket /
/// Portable Hole / Leonin Relic-Warder family of two LINKED abilities
/// (CR 607 linked abilities; CR 603.6e "until" duration; CR 610.3 return).
///
/// <para>
/// This verb sits on an <c>etb_self</c> trigger and is the ONE declarative
/// shape for the whole "exile until this leaves" removal cluster. Unlike the
/// plain <see cref="ExileTargetEffectDef"/> (a one-shot exile), this verb
/// composes the full linked pair: at card-build time it attaches the linked
/// leaves-the-battlefield (LTB) triggered ability to the SAME card, sharing a
/// per-instance closure with the ETB effect that captures the exiled object +
/// its owner. When the source leaves the battlefield, the LTB returns the SAME
/// remembered object to its <b>owner's</b> battlefield (CR 110.2 — under its
/// owner's control), or no-ops if the source already left (the closure only
/// fires once) or the exiled object has since left exile (CR 603.6e — the
/// linked "until" return finds nothing). See
/// <see cref="Majik.Core.CardData.Definitions.CardDefRuntime.BuildExileUntilLeavesEffect"/>.
/// </para>
///
/// <para>
/// The verb composes the printed restrictions of the cluster onto the base
/// <see cref="TargetFilter"/> (a battlefield permanent / creature filter):
/// <list type="bullet">
///   <item><see cref="OpponentControlsOnly"/> (default <c>true</c>) — "an
///   opponent controls" (CR 109.5). Banishing Light / Glass Casket / Portable
///   Hole carry it; Oblivion Ring ("another target nonland permanent") does
///   not, so it flips this off and relies on <see cref="ExcludeSelf"/>.</item>
///   <item><see cref="ExcludeSelf"/> (default <c>false</c>) — "another" (CR
///   109.5 — exclude the source permanent itself). Oblivion Ring sets it.</item>
///   <item><see cref="MaxManaValue"/> (default <c>null</c> = no cap) — "with
///   mana value N or less" (CR 202.3). Glass Casket = 3, Portable Hole = 2,
///   Leonin Relic-Warder leaves it null (any artifact/enchantment).</item>
/// </list>
/// Both the candidate gatherer (so the agent is only ever offered legal picks)
/// and the resolution-time legality re-check (CR 608.2b) apply the full
/// composed predicate against the live source controller.
/// </para>
///
/// <para>
/// <see cref="TargetFilter"/> is the base battlefield filter string the runtime
/// translates (e.g. <c>"nonland_permanent"</c>, <c>"creature"</c>,
/// <c>"artifact_or_enchantment"</c>, <c>"permanent"</c>).
/// </para>
/// </summary>
public sealed class ExileUntilLeavesEffectDef : EffectDefinition
{
    /// <summary>Base battlefield target filter (e.g. <c>"nonland_permanent"</c>,
    /// <c>"creature"</c>, <c>"artifact_or_enchantment"</c>).</summary>
    public string TargetFilter { get; set; } = "nonland_permanent";

    /// <summary>"an opponent controls" (CR 109.5). Default <c>true</c>.</summary>
    public bool OpponentControlsOnly { get; set; } = true;

    /// <summary>"another" — exclude the source permanent itself (CR 109.5).
    /// Default <c>false</c>.</summary>
    public bool ExcludeSelf { get; set; }

    /// <summary>"with mana value N or less" (CR 202.3). Null = no cap.</summary>
    public int? MaxManaValue { get; set; }

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        // The candidate gatherer is built in the runtime (it needs the live
        // source card's controller to apply the "an opponent controls" /
        // "another" riders against the resolving GameContext), so the request
        // declared here is a placeholder the runtime replaces. We still emit a
        // 1..1 request so the owning ability reserves a target slot.
        new Majik.Core.Players.Agents.TargetRequest(
            Description: BuildDescription(),
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: System.Array.Empty<object>(),
            Intent: Majik.Core.Cards.BotIntent.Removal);

    private string BuildDescription()
    {
        var who = OpponentControlsOnly ? " an opponent controls" : "";
        var another = ExcludeSelf ? "another " : "";
        var mv = MaxManaValue is int n ? $" with mana value {n} or less" : "";
        return $"exile {another}target {TargetFilter}{who}{mv} until this leaves the battlefield";
    }

    /// <summary>Stable effect description used by the runtime ETB effect
    /// (prefixed with the source name) — kept here so the wording stays in one
    /// place alongside the <see cref="TargetRequest"/> description.</summary>
    internal string BuildEffectDescription(string cardName) =>
        $"{cardName}: {BuildDescription()} (CR 701.21)";
}

/// <summary>
/// "Return target [filter] to its owner's hand" — real targeted bounce
/// effect (CR 701.20). At resolution the effect reads the chosen permanent
/// off <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and
/// returns it to its owner's hand via
/// <see cref="Majik.Core.Primitives.Fx.BounceToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>.
/// CR 608.2b — an illegal target at resolution (the permanent has moved off
/// the battlefield since the ability was put on the stack) fizzles cleanly.
/// Mirrors the existing <see cref="DestroyTargetEffectDef"/> /
/// <see cref="UntapTargetEffectDef"/> targeted-effect shape exactly — the
/// only new piece is the declarative schema wiring onto the pre-existing
/// <c>Fx.BounceToHand</c> primitive. Canonical case: Karakas —
/// <c>"{T}: Return target legendary creature to its owner's hand."</c>
///
/// <see cref="TargetFilter"/> is the filter string the runtime translates
/// into a <see cref="Majik.Core.Players.Agents.TargetRequest"/> (e.g.
/// <c>"legendary_creature"</c>, <c>"creature"</c>, <c>"nonland_permanent"</c>,
/// <c>"permanent"</c>).
/// </summary>
public sealed class ReturnToHandEffectDef : EffectDefinition
{
    public string TargetFilter { get; set; } = "permanent";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(
            TargetFilter, "return to its owner's hand",
            Majik.Core.Cards.BotIntent.Bounce);
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
/// "Tap target [filter]" — real targeted effect (CR 701.21a). The mirror of
/// <see cref="UntapTargetEffectDef"/>: at resolution the effect reads the
/// chosen permanent off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and taps
/// it via <see cref="Majik.Core.Primitives.Fx.Tap(Majik.Core.Cards.Permanent)"/>.
/// Tapping an already-tapped permanent is a no-op (CR 701.21b — "taps" with no
/// effect; <see cref="Majik.Core.Cards.Permanent.Tap"/> is idempotent). CR
/// 608.2b — an illegal target at resolution (the permanent has left the
/// battlefield since the ability went on the stack) fizzles cleanly. Mirrors
/// the existing targeted-effect family exactly — the only new piece is the
/// declarative schema wiring onto the pre-existing <c>Fx.Tap</c> primitive.
/// Canonical case: Goldmeadow Harrier — <c>"{W}, {T}: Tap target creature."</c>
///
/// <see cref="TargetFilter"/> is the filter string the runtime translates
/// into a <see cref="Majik.Core.Players.Agents.TargetRequest"/> (e.g.
/// <c>"creature"</c>, <c>"permanent"</c>).
/// </summary>
public sealed class TapTargetEffectDef : EffectDefinition
{
    public string TargetFilter { get; set; } = "creature";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(
            TargetFilter, "tap", Majik.Core.Cards.BotIntent.Removal);
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

/// <summary>
/// "Gain control of target creature [until end of turn]" — the Threaten /
/// Act of Treason / Claim the Firstborn family (CR 613.2). Real targeted
/// control-change verb onto the
/// <see cref="Majik.Core.Effects.TemporaryControlChangeEffect"/> primitive: at
/// resolution the effect reads the chosen creature off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and, for
/// <see cref="Duration"/> = <c>"end_of_turn"</c> (the only v1 mode), registers a
/// temporary control swap on the live
/// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> — the controlling
/// player keeps the creature until the cleanup step (CR 514.2), after which
/// control reverts to the owner/prior controller automatically.
///
/// <para>The verb composes the FULL Threaten rider in one effect (CR 805
/// standard wording — "Gain control of target creature until end of turn.
/// Untap it. It gains haste until end of turn."):
/// <list type="bullet">
///   <item><see cref="Untap"/> (default <c>true</c>) — untaps the gained
///   creature so it can attack.</item>
///   <item><see cref="GainsHaste"/> (default <c>true</c>) — registers a
///   <see cref="Majik.Core.Effects.GrantKeywordUntilEndOfTurnEffect"/> with
///   keyword "Haste" so the creature, which has summoning sickness for its new
///   controller (CR 302.6), can attack the turn it is stolen.</item>
/// </list>
/// Cards that only steal without the haste/untap rider (rare) can flip the
/// flags off in JSON.</para>
///
/// <para>CR 608.2b — an illegal target at resolution (the creature has left the
/// battlefield since the spell went on the stack) fizzles cleanly: no swap,
/// untap, or haste. Without a live continuous-effects service (pure-shape test
/// path) the control swap is a no-op, mirroring the legacy
/// <see cref="Majik.Core.CardData.Factories.ArchmagesCharmFactory"/> posture.</para>
/// </summary>
public sealed class GainControlEffectDef : EffectDefinition
{
    /// <summary>Target filter (default <c>"creature"</c>).</summary>
    public string TargetFilter { get; set; } = "creature";

    /// <summary>Duration of the control change. v1 supports only
    /// <c>"end_of_turn"</c> (the Threaten family); any other value is treated
    /// as <c>"end_of_turn"</c> for now (permanent Mind-Control-style control
    /// is served by the bespoke <c>ControlSpellFactory</c> path).</summary>
    public string Duration { get; set; } = "end_of_turn";

    /// <summary>Untap the gained creature (Threaten rider). Default <c>true</c>.</summary>
    public bool Untap { get; set; } = true;

    /// <summary>Grant the gained creature haste until end of turn (Threaten
    /// rider, CR 302.6 — a creature whose control changed this turn is sick).
    /// Default <c>true</c>.</summary>
    public bool GainsHaste { get; set; } = true;

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(
            TargetFilter, "gain control of", Majik.Core.Cards.BotIntent.Removal);
}

/// <summary>
/// "Fight" (CR 701.12) — two creatures each deal damage equal to their power
/// to the other SIMULTANEOUSLY (CR 701.12a). Real targeted verb onto the
/// shared <see cref="Majik.Core.Primitives.Fx.Fight"/> primitive: at
/// resolution the effect reads the chosen creature(s) off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> and runs
/// the bilateral power exchange. Fight is damage but NOT combat damage (no
/// first/double strike, no combat triggers, no trample); deathtouch (CR
/// 702.2b) and lifelink (CR 702.15a) still apply, and the resulting
/// lethal-damage / deathtouch state-based action runs afterward (CR 704).
///
/// <para>Two <see cref="Source"/> modes:</para>
/// <list type="bullet">
///   <item><c>"self"</c> (default) — "[this creature] fights target creature."
///   The FIGHTER is the ability/spell's own source creature; the verb declares
///   ONE target (the OTHER creature) via <see cref="ToTargetRequest"/>. Pounce /
///   Prey Upon-on-a-creature / Setessan Tactics-style abilities.</item>
///   <item><c>"target"</c> — "Target creature you control fights target
///   creature you don't control" (Prey Upon, Khalni Ambush, Pit Fight). The
///   verb declares TWO ordered targets: the FIGHTER ("a creature you control",
///   <see cref="ToTargetRequest"/>) and the OTHER creature
///   (<see cref="ToExtraTargetRequest"/>). At resolution the fighter is read at
///   the primary slot and the other creature at the next slot.</item>
/// </list>
///
/// <para>
/// CR 608.2b — if either fighter is gone / no longer a legal creature on the
/// battlefield at resolution, the whole fight fizzles (no damage either way) —
/// a fight needs BOTH creatures present (CR 701.12c). The candidate gatherers /
/// resolution re-check use the <see cref="TargetFilters"/> predicate.
/// </para>
///
/// <para><see cref="TargetFilter"/> filters the OTHER creature in
/// <c>"self"</c> mode, and BOTH targets in <c>"target"</c> mode
/// (<see cref="ControllerTargetFilter"/> overrides the fighter's filter when
/// the printed "you control" / "you don't control" split matters; defaults to
/// <c>"creature"</c>).</para>
/// </summary>
public sealed class FightEffectDef : EffectDefinition
{
    /// <summary><c>"self"</c> (the source creature fights, default) or
    /// <c>"target"</c> (the fighter is a chosen creature).</summary>
    public string Source { get; set; } = "self";

    /// <summary>Filter for the OTHER (fought) creature — and, in
    /// <c>"target"</c> mode, the default filter for the fighter too (unless
    /// <see cref="ControllerTargetFilter"/> overrides it). Default
    /// <c>"creature"</c>.</summary>
    public string TargetFilter { get; set; } = "creature";

    /// <summary>In <c>"target"</c> mode, the filter for the FIGHTER ("a
    /// creature you control"). Null = reuse <see cref="TargetFilter"/>.</summary>
    public string? ControllerTargetFilter { get; set; }

    private bool IsTargetSource =>
        string.Equals(Source, "target", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        IsTargetSource
            // Primary slot is the FIGHTER ("creature you control"); the OTHER
            // creature is the extra slot below.
            ? TargetFilters.ToTargetRequest(
                ControllerTargetFilter ?? TargetFilter, "fight with",
                Majik.Core.Cards.BotIntent.Removal)
            // self-source: the single declared target is the OTHER creature.
            : TargetFilters.ToTargetRequest(
                TargetFilter, "fight", Majik.Core.Cards.BotIntent.Removal);

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToExtraTargetRequest() =>
        IsTargetSource
            ? TargetFilters.ToTargetRequest(
                TargetFilter, "fight", Majik.Core.Cards.BotIntent.Removal)
            : null;
}

/// <summary>"Controller gains N life." Default <see cref="Amount"/> = 1.</summary>
public sealed class GainLifeSelfEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
}

/// <summary>
/// "You lose N life" (CR 119.3) — the untargeted, controller-scoped life-loss
/// verb. The mirror of <see cref="GainLifeSelfEffectDef"/> onto the
/// pre-existing <see cref="Majik.Core.Primitives.Fx.LoseLife"/> primitive. The
/// loss lands on the ability's controller directly (no target slot), so unlike
/// <see cref="LoseLifeTargetEffectDef"/> it needs neither a target nor the
/// shared-slot rider machinery. Models the symmetric / self-cost half of the
/// "you draw a card and you lose N life" upkeep family (Phyrexian Arena, Dark
/// Tutelage) and the Howling-Mine-style payoff. Default <see cref="Amount"/> = 1.
/// </summary>
public sealed class LoseLifeSelfEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
}

/// <summary>
/// "[Target / its controller] loses N life" — the targeted life-drain rider
/// onto the pre-existing <see cref="Majik.Core.Primitives.Fx.LoseLife"/>
/// primitive (CR 119.3). Two <see cref="Subject"/> modes:
///
/// <list type="bullet">
///   <item><c>"controller"</c> (default) — the effect is a <b>rider</b> that
///   shares the immediately-preceding targeted effect's chosen target
///   (<see cref="SharesPreviousTargetSlot"/> = <c>true</c>) and drains
///   <b>that target's controller</b>. The canonical case is Vapor Snag's
///   "Return target creature to its owner's hand. Its controller loses 1
///   life." — one printed target, two clauses. CR 608.2b: if the shared
///   target is illegal at resolution the rider fizzles with the host.</item>
///   <item><c>"target"</c> — the effect declares its OWN target (via
///   <see cref="TargetFilter"/>, e.g. <c>"player"</c> / <c>"creature"</c>)
///   and drains the chosen object's controller (a player targets itself).
///   "Target player loses N life."</item>
/// </list>
/// </summary>
public sealed class LoseLifeTargetEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;

    /// <summary><c>"controller"</c> (rider, default) or <c>"target"</c>
    /// (declares its own target slot). See the type summary.</summary>
    public string Subject { get; set; } = "controller";

    /// <summary>The filter string for the OWN target slot when
    /// <see cref="Subject"/> is <c>"target"</c> (e.g. <c>"player"</c>,
    /// <c>"creature"</c>). Ignored in <c>"controller"</c> rider mode.</summary>
    public string TargetFilter { get; set; } = "player";

    private bool IsRider =>
        string.Equals(Subject, "controller", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool SharesPreviousTargetSlot => IsRider;

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        IsRider
            ? null
            : TargetFilters.ToTargetRequest(TargetFilter, $"lose {Amount} life");
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

/// <summary>
/// "Target creature you control explores" (CR 701.40) — the targeted explore
/// verb. The declarative serialization of the Explore keyword action onto the
/// shared <see cref="Majik.Core.Keywords.ExploreAction.ExploreAsync"/>
/// primitive (PR #2237). At resolution the effect reads the chosen creature off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> at the
/// reserved index and explores it under <b>its</b> controller (CR 701.40a — the
/// exploring permanent's controller reveals the top card; the +1/+1 counter, if
/// any, lands on the exploring creature). The keep-on-top / graveyard choice
/// (CR 701.40c) consults the resolving context's agent, and a
/// <see cref="Majik.Core.Events.CreatureExploredEvent"/> is published so
/// "Whenever a creature you control explores" payoffs (Wildgrowth Walker) fire.
///
/// <para>
/// CR 608.2b — an illegal target at resolution (the chosen creature has left the
/// battlefield since the ability / spell was put on the stack) fizzles cleanly:
/// no explore. The candidate gatherer / resolution re-check apply the
/// <see cref="TargetFilter"/> predicate via <see cref="TargetFilters"/>.
/// </para>
///
/// <para>
/// The canonical case is the <b>Map</b> token's
/// "{1}, {T}, Sacrifice this token: Target creature you control explores"
/// activated ability (CR 111.10 — Lost Caverns of Ixalan), shared by Get Lost,
/// Sentinel of the Nameless City, Spyglass Siren, et al. Get Lost / Spyglass
/// Siren produce the token; the verb is what the token's ability resolves to.
/// </para>
///
/// <see cref="TargetFilter"/> defaults to <c>"creature_you_control"</c> (every
/// printed explore-target effect today says "creature you control"); any other
/// filter string the runtime understands is honoured.
/// </summary>
public sealed class ExploreTargetEffectDef : EffectDefinition
{
    /// <summary>Target filter (default <c>"creature_you_control"</c>).</summary>
    public string TargetFilter { get; set; } = "creature_you_control";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(
            TargetFilter, "explore", Majik.Core.Cards.BotIntent.Buff);
}

/// <summary>
/// "Target creature gets +X/+X until end of turn" (CR 611 continuous effect,
/// CR 514.2 end-of-turn expiry) — the targeted declarative pump verb onto the
/// pre-existing <see cref="Majik.Core.Effects.PumpUntilEndOfTurnEffect"/>
/// primitive (the Layer-7c +P/+T modifier the fluent <c>pump_self</c> /
/// <c>PumpUntilEndOfTurn</c> family already emits). At resolution the effect
/// reads the chosen creature off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> at the
/// reserved index and registers the +<see cref="Power"/>/+<see cref="Toughness"/>
/// modifier on <b>that creature's own</b>
/// <see cref="Majik.Core.Effects.ContinuousEffectsService"/>
/// (<see cref="Majik.Core.Cards.Creature.ActiveEffects"/>) — the same posture
/// the fluent <c>PumpUntilEndOfTurn</c> <see cref="MaterializeStep"/> uses, so
/// the modifier auto-expires at the cleanup step.
///
/// <para>Signed values are honoured: a negative
/// <see cref="Power"/> / <see cref="Toughness"/> models the "−X/−X until end of
/// turn" debuff half (CR 611) — the Layer-7c effect simply adds a negative
/// delta. CR 608.2b — an illegal target at resolution (the creature has left
/// the battlefield since the ability was put on the stack) fizzles cleanly: no
/// modifier. Without a live continuous-effects service (pure-shape test path)
/// the registration no-ops, mirroring
/// <see cref="GainControlEffectDef"/>'s posture.</para>
///
/// <para>Canonical case: Okina, Temple to the Grandfathers —
/// <c>"{G}, {T}: Target legendary creature gets +1/+1 until end of turn."</c>
/// (<see cref="TargetFilter"/> <c>"legendary_creature"</c>).</para>
/// </summary>
public sealed class PumpTargetEffectDef : EffectDefinition
{
    /// <summary>The power delta (may be negative for a −X/−X debuff). Default 1.</summary>
    public int Power { get; set; } = 1;

    /// <summary>The toughness delta (may be negative). Default 1.</summary>
    public int Toughness { get; set; } = 1;

    /// <summary>Target filter (default <c>"creature"</c>).</summary>
    public string TargetFilter { get; set; } = "creature";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(
            TargetFilter,
            $"get +{Power}/+{Toughness} until end of turn",
            Power < 0 || Toughness < 0
                ? Majik.Core.Cards.BotIntent.Removal
                : Majik.Core.Cards.BotIntent.Buff);
}

/// <summary>
/// "Target creature gains [keyword] until end of turn" (CR 613.1c Layer-6
/// keyword grant, CR 514.2 end-of-turn expiry) — the targeted declarative
/// keyword-grant verb onto the pre-existing
/// <see cref="Majik.Core.Effects.GrantKeywordUntilEndOfTurnEffect"/> primitive
/// (the same until-EOT grant <see cref="GainControlEffectDef"/>'s haste rider
/// and the Temur Battle Rage / Berserk pump family use). At resolution the
/// effect reads the chosen creature off
/// <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/> at the
/// reserved index and registers a <see cref="Keyword"/> grant on <b>that
/// creature's own</b> <see cref="Majik.Core.Cards.Creature.ActiveEffects"/>, so
/// it auto-expires at cleanup. CR 608.2b — an illegal target at resolution
/// fizzles cleanly: no grant. Without a live continuous-effects service
/// (pure-shape test path) the registration no-ops.
///
/// <para>Canonical cases:
/// <list type="bullet">
///   <item>Soaring Seacliff — "When this land enters, target creature gains
///   flying until end of turn." (<see cref="Keyword"/> <c>"Flying"</c>).</item>
///   <item>Sunhome, Fortress of the Legion — "{2}{R}{W}, {T}: Target creature
///   gains double strike until end of turn." (<see cref="Keyword"/>
///   <c>"Double strike"</c>).</item>
/// </list></para>
/// </summary>
public sealed class GrantKeywordUntilEotTargetEffectDef : EffectDefinition
{
    /// <summary>The keyword granted until end of turn (e.g. <c>"Flying"</c>,
    /// <c>"Double strike"</c>, <c>"Trample"</c>, <c>"Haste"</c>). The keyword
    /// HashSet uses <see cref="System.StringComparer.OrdinalIgnoreCase"/>, so
    /// casing is irrelevant; the engine's canonical capitalization is preferred
    /// for readability.</summary>
    public string Keyword { get; set; } = "Flying";

    /// <summary>Target filter (default <c>"creature"</c>).</summary>
    public string TargetFilter { get; set; } = "creature";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(
            TargetFilter, $"gain {Keyword} until end of turn",
            Majik.Core.Cards.BotIntent.Buff);
}

/// <summary>
/// "Target [filter] becomes an artifact in addition to its other types until
/// end of turn" (CR 613.1d Layer-4 type-add, CR 514.2 end-of-turn expiry) — the
/// Liquimetal Coating / Liquimetal Torque family. The targeted declarative verb
/// onto the pre-existing
/// <see cref="Majik.Core.Effects.LiquimetalCoatingAddArtifactEffect"/> primitive
/// (the EOT-expiring Layer-4 add-artifact effect; the source-anchored
/// <see cref="Majik.Core.Effects.AddCardTypeEffect"/> from the enters-as-copy PR
/// is its non-expiring sibling). At resolution the effect reads the chosen
/// permanent off <see cref="Majik.Core.Abilities.ResolutionContext.ChosenTargets"/>
/// at the reserved index and registers the add-artifact modifier on <b>that
/// permanent's own</b> <see cref="Majik.Core.Cards.Permanent.ActiveEffects"/>,
/// so the printed types remain present (the ADD union) and the modifier expires
/// at cleanup. CR 608.2b — an illegal target at resolution fizzles cleanly: no
/// type-add. Without a live continuous-effects service (pure-shape test path)
/// the registration no-ops, mirroring the hand-rolled Liquimetal Coating
/// factory's posture.
///
/// <para>Canonical cases:
/// <list type="bullet">
///   <item>Liquimetal Coating — "{T}: Target permanent becomes an artifact in
///   addition to its other types until end of turn." (<see cref="TargetFilter"/>
///   <c>"permanent"</c>).</item>
///   <item>Liquimetal Torque — "{T}: Target nonland permanent becomes an
///   artifact in addition to its other types until end of turn."
///   (<see cref="TargetFilter"/> <c>"nonland_permanent"</c>).</item>
/// </list></para>
///
/// <see cref="TargetFilter"/> is the filter string the runtime translates into a
/// <see cref="Majik.Core.Players.Agents.TargetRequest"/> (e.g. <c>"permanent"</c>,
/// <c>"nonland_permanent"</c>).
/// </summary>
public sealed class BecomesArtifactTargetEffectDef : EffectDefinition
{
    /// <summary>Target filter (default <c>"permanent"</c>).</summary>
    public string TargetFilter { get; set; } = "permanent";

    /// <inheritdoc />
    public override Majik.Core.Players.Agents.TargetRequest? ToTargetRequest() =>
        TargetFilters.ToTargetRequest(
            TargetFilter, "becomes an artifact until end of turn");
}
