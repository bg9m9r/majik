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
