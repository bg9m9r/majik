using System.Text.Json.Serialization;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Discriminated-union base for triggered-ability trigger conditions.
/// JSON discriminator: <c>type</c>. Each variant maps to a factory
/// method on <see cref="Majik.Core.Abilities.Triggers"/> in the build
/// pipeline (<see cref="CardDefRuntime.BuildJsonTrigger"/>, reached via
/// <see cref="ToTrigger"/>).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EnterBattlefieldSelfTriggerDef), "etb_self")]
[JsonDerivedType(typeof(CardLeavesYourGraveyardTriggerDef), "card_leaves_your_graveyard")]
public abstract class TriggerDefinition
{
    /// <summary>
    /// Map this JSON trigger onto a canonical trigger-condition builder
    /// closure (PLAN 03 S2). The closure defers to
    /// <see cref="CardDefRuntime.BuildJsonTrigger"/> (which routes through the
    /// shared <see cref="Majik.Core.Abilities.Triggers"/> vocabulary) against
    /// the live card, so the produced
    /// <see cref="Majik.Core.Abilities.ITriggerCondition"/> is byte-identical
    /// to the legacy direct build.
    /// </summary>
    public Func<Majik.Core.Cards.ICard, Majik.Core.Abilities.ITriggerCondition> ToTrigger() =>
        card => CardDefRuntime.BuildJsonTrigger(this, card);
}

/// <summary>"When this permanent enters the battlefield" — the most
/// common trigger. Maps to
/// <see cref="Majik.Core.Abilities.Triggers.OnEnterBattlefieldSelf"/>.
/// No extra fields; the source is always the card the ability lives on.</summary>
public sealed class EnterBattlefieldSelfTriggerDef : TriggerDefinition { }

/// <summary>
/// "Whenever a card with one of the given types leaves your graveyard" —
/// the lifegain shape used by Dredger's Insight. Filter is matched against
/// any of <see cref="CardTypes"/> (logical OR — strings parsed as
/// <see cref="Majik.Core.Cards.Types.CardType"/>).
///
/// Wired via <see cref="Majik.Core.Abilities.EventTriggerCondition{TEvent}"/>
/// over <see cref="Majik.Core.Events.CardMovedEvent"/> with the predicate
/// "FromZone == Graveyard &amp;&amp; Card.Owner == controller &amp;&amp;
/// (Card.HasType(t1) || …)".
/// </summary>
public sealed class CardLeavesYourGraveyardTriggerDef : TriggerDefinition
{
    public List<string> CardTypes { get; set; } = new();
}
