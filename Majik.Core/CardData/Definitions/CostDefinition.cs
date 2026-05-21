using System.Text.Json.Serialization;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Discriminated-union base for activated-ability cost specs. JSON
/// discriminator: <c>type</c>. New variants register via
/// <see cref="JsonDerivedTypeAttribute"/> + a matching switch arm in
/// <see cref="CardDefinitionFactory.BuildCost"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ManaCostDef), "mana")]
[JsonDerivedType(typeof(RemoveCounterCostDef), "remove_counter")]
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
