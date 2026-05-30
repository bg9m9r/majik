using System.Text.Json.Serialization;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Discriminated-union base for activated-ability effect specs. JSON
/// discriminator: <c>type</c>. New variants register via
/// <see cref="JsonDerivedTypeAttribute"/> + a matching switch arm in
/// <see cref="CardDefinitionFactory.BuildEffect"/>.
///
/// Build semantics: each variant produces an
/// <see cref="Majik.Core.Abilities.IEffect"/> whose lambda captures the
/// live <c>card</c> + <c>controller</c> closure from the build context.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PutCounterEffectDef), "put_counter")]
[JsonDerivedType(typeof(DealDamageStubEffectDef), "deal_damage_stub")]
[JsonDerivedType(typeof(DrawCardEffectDef), "draw_card")]
[JsonDerivedType(typeof(SurveilSelfEffectDef), "surveil_self")]
[JsonDerivedType(typeof(ScrySelfEffectDef), "scry_self")]
[JsonDerivedType(typeof(DestroyTargetStubEffectDef), "destroy_target_stub")]
[JsonDerivedType(typeof(UntapTargetStubEffectDef), "untap_target_stub")]
[JsonDerivedType(typeof(PreventDamageTargetStubEffectDef), "prevent_damage_target_stub")]
[JsonDerivedType(typeof(GainLifeSelfEffectDef), "gain_life_self")]
[JsonDerivedType(typeof(MillThenPickFirstMatchingToHandEffectDef), "mill_then_pick_first_matching_to_hand")]
[JsonDerivedType(typeof(ConniveSelfEffectDef), "connive_self")]
[JsonDerivedType(typeof(AmassSelfEffectDef), "amass_self")]
public abstract class EffectDefinition { }

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
/// "Deals N damage to any target" — stub effect. Records the intent
/// but doesn't actually route damage anywhere because the targeting
/// system isn't ready (matches the existing C# Walking Ballista's
/// behavior — "target damage deferred"). When targeting lands, this
/// type splits into <c>deal_damage</c> (real) + the stub stays
/// available for incremental migration.
/// </summary>
public sealed class DealDamageStubEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
    public string Target { get; set; } = "any";
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
/// "Destroy target [filter]" — stub effect. Records the target filter
/// describing what's destructible but resolves as a no-op because the
/// targeting system isn't wired yet (matches the existing C# Boseiju's
/// Channel-effect deferred behavior). When the targeting prompt lands,
/// this type upgrades to a real <c>destroy_target</c> effect without
/// breaking JSON files.
///
/// <see cref="TargetFilter"/> is a free-form string the future
/// targeting layer will translate (e.g. <c>"artifact_enchantment_nonbasic_land"</c>).
/// </summary>
public sealed class DestroyTargetStubEffectDef : EffectDefinition
{
    public string TargetFilter { get; set; } = "permanent";
}

/// <summary>
/// "Untap target [filter]" — stub effect. Records the target filter
/// describing what's untappable but resolves as a no-op because the
/// targeting prompt system isn't wired yet (mirrors the existing
/// <see cref="DestroyTargetStubEffectDef"/> / <see cref="DealDamageStubEffectDef"/>
/// deferred-targeting pattern). Untapping itself is a CR 701.21 action
/// the engine already supports (<c>Permanent.Untap</c>); the gap is
/// purely target selection. When the targeting prompt lands, this type
/// upgrades to a real <c>untap_target</c> effect without breaking JSON
/// files. Canonical case: Minamo, School at Water's Edge —
/// <c>"{U}, {T}: Untap target legendary permanent."</c>
///
/// <see cref="TargetFilter"/> is a free-form string the future targeting
/// layer will translate (e.g. <c>"legendary_permanent"</c>).
/// </summary>
public sealed class UntapTargetStubEffectDef : EffectDefinition
{
    public string TargetFilter { get; set; } = "permanent";
}

/// <summary>
/// "Prevent the next N damage that would be dealt to target [filter] this
/// turn" — stub effect. Records the prevention <see cref="Amount"/> and the
/// <see cref="TargetFilter"/> describing what can be shielded, but resolves
/// as a no-op because the targeting/prompt system isn't wired yet (mirrors
/// the existing <see cref="UntapTargetStubEffectDef"/> /
/// <see cref="DestroyTargetStubEffectDef"/> deferred-targeting pattern).
///
/// The prevention shield itself (CR 615 — a per-turn pool of prevented
/// damage points) is already supported by the engine via
/// <see cref="Majik.Core.Effects.PreventNextNDamageToAnyTargetShield"/>; the
/// gap is purely target selection. When the targeting prompt lands, this
/// type upgrades to a real <c>prevent_damage_target</c> effect (register the
/// shield against the chosen target) without breaking JSON files. Canonical
/// case: Eiganjo Castle —
/// <c>"{W}, {T}: Prevent the next 2 damage that would be dealt to target
/// legendary creature this turn."</c>
///
/// <see cref="TargetFilter"/> is a free-form string the future targeting
/// layer will translate (e.g. <c>"legendary_creature"</c>).
/// </summary>
public sealed class PreventDamageTargetStubEffectDef : EffectDefinition
{
    public int Amount { get; set; } = 1;
    public string TargetFilter { get; set; } = "creature";
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
