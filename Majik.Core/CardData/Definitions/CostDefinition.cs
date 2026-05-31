using System.Text.Json.Serialization;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Discriminated-union base for activated-ability cost specs. JSON
/// discriminator: <c>type</c>. New variants register via
/// <see cref="JsonDerivedTypeAttribute"/> + a matching switch arm in
/// <see cref="CardDefinitionFactory.BuildCost"/>.
///
/// Convergence (PLAN 03): <see cref="CardDefinitionFactory.BuildCost"/>
/// builds these via the shared <see cref="Majik.Core.Primitives.Costs"/>
/// vocabulary (the cost-side companion to
/// <see cref="Majik.Core.Primitives.Fx"/> / <see cref="Majik.Core.Abilities.Triggers"/>).
/// New cost shapes should add a <c>Costs.*</c> helper (additively) rather
/// than inline a fresh cost ctor here.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ManaCostDef), "mana")]
[JsonDerivedType(typeof(RemoveCounterCostDef), "remove_counter")]
[JsonDerivedType(typeof(TapSelfCostDef), "tap_self")]
[JsonDerivedType(typeof(SacrificeSelfCostDef), "sacrifice_self")]
[JsonDerivedType(typeof(DiscardSelfCostDef), "discard_self")]
public abstract class CostDefinition { }

/// <summary>Mana cost — parsed via <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>.
/// <see cref="Amount"/> accepts bracketed (<c>"{1}{R}"</c>) or unbracketed
/// (<c>"1R"</c>) forms.</summary>
public sealed class ManaCostDef : CostDefinition
{
    public string Amount { get; set; } = "";
}

/// <summary>
/// "Remove N &lt;counter&gt; counters from &lt;target&gt;" — Walking Ballista's
/// ping cost is the canonical case. v1 only supports
/// <c>"from": "self"</c> + <c>"counter": "+1/+1"</c>; broader counter
/// types + targeting plug into the same shape later.
/// </summary>
public sealed class RemoveCounterCostDef : CostDefinition
{
    public string Counter { get; set; } = "+1/+1";
    public int Amount { get; set; } = 1;
    public string From { get; set; } = "self";
}

/// <summary>Tap this permanent ({T}) — CR 605 standard ability cost.
/// No additional fields because the cost always targets the activating
/// permanent itself.</summary>
public sealed class TapSelfCostDef : CostDefinition { }

/// <summary>Sacrifice this permanent — CR 701.16. No additional fields;
/// the cost always targets the activating permanent itself.</summary>
public sealed class SacrificeSelfCostDef : CostDefinition { }

/// <summary>Discard this card — required for activated abilities that
/// fire from the hand (Channel, CR 702.74). No additional fields.</summary>
public sealed class DiscardSelfCostDef : CostDefinition { }
