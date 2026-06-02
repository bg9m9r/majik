using System.Text.Json.Serialization;
using Majik.Core.Zones;

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
[JsonDerivedType(typeof(WheneverYouGainLifeTriggerDef), "whenever_you_gain_life")]
[JsonDerivedType(typeof(WheneverYouCastSpellTriggerDef), "whenever_you_cast_spell")]
[JsonDerivedType(typeof(AttacksSelfTriggerDef), "attacks_self")]
[JsonDerivedType(typeof(DiesSelfTriggerDef), "dies_self")]
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

    /// <summary>
    /// The zones in which the triggered ability must remain "active" (i.e.
    /// observable by <see cref="Majik.Core.Abilities.TriggerManager"/>) for
    /// this condition to fire — see
    /// <see cref="Majik.Core.Abilities.TriggeredAbility.ActiveZones"/>.
    /// <c>null</c> means "use the engine default" (battlefield only). A
    /// leaves-the-battlefield trigger such as <c>dies_self</c> overrides this
    /// to include the Graveyard, because <see cref="Majik.Core.Zones.ZoneService"/>
    /// stamps <c>card.Zone = Graveyard</c> before publishing the move event,
    /// so the ability must still be observable from the Graveyard (CR 603.6d
    /// / CR 700.4 — same posture as the hand-rolled dies factories).
    /// </summary>
    public virtual IReadOnlyList<ZoneType>? ActiveZones => null;
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

/// <summary>
/// "Whenever you gain life, …" (CR 119.3). Fires on
/// <see cref="Majik.Core.Events.LifeChangedEvent"/> for the trigger's
/// controller where the life total strictly increased — the Ajani's
/// Pridemate / Heliod, Sun-Crowned / lifegain-payoff family. Controller is
/// resolved live (<c>card.Controller</c>) at fire time, mirroring
/// <see cref="CardLeavesYourGraveyardTriggerDef"/>, so a control change moves
/// the trigger with the permanent (CR 109.5). No extra fields.
/// </summary>
public sealed class WheneverYouGainLifeTriggerDef : TriggerDefinition { }

/// <summary>
/// "Whenever you cast a [type] spell, …" (CR 601.2 / 603.1). Fires on
/// <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/> whose
/// spell controller is the trigger's controller (CR 109.5 — "you cast").
/// Two optional, composable filters:
/// <list type="bullet">
///   <item><see cref="NoncreatureOnly"/> — restrict to noncreature spells
///   (CR 112.1), the Prowess / Spellgorger Weird / Young Pyromancer shape.</item>
///   <item><see cref="SpellTypes"/> — restrict to spells whose card has ANY of
///   the listed card types (logical OR), the "cast an enchantment / instant or
///   sorcery / Aura spell" shape (Generous Visitor, Kiln Fiend, Festival
///   Crasher). Strings parse as <see cref="Majik.Core.Cards.Types.CardType"/>.</item>
/// </list>
/// With neither filter set it matches any spell you cast (Birgi).
/// </summary>
public sealed class WheneverYouCastSpellTriggerDef : TriggerDefinition
{
    /// <summary>Restrict to noncreature spells (CR 112.1).</summary>
    public bool NoncreatureOnly { get; set; }

    /// <summary>Restrict to spells whose card has ANY of these card types
    /// (logical OR). Empty = no type restriction.</summary>
    public List<string> SpellTypes { get; set; } = new();
}

/// <summary>
/// "Whenever this creature attacks, …" (CR 508.1f) — per-attacker self
/// trigger over <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
/// matching the source. Maps to
/// <see cref="Majik.Core.Abilities.Triggers.OnAttackSelf"/>. No extra fields;
/// the source is always the card the ability lives on.
/// </summary>
public sealed class AttacksSelfTriggerDef : TriggerDefinition { }

/// <summary>
/// "When this creature dies, …" / "When this permanent dies, …" (CR 700.4) —
/// a Battlefield → Graveyard self move over
/// <see cref="Majik.Core.Events.CardMovedEvent"/>. Maps to
/// <see cref="Majik.Core.Abilities.Triggers.OnDies"/>. Overrides
/// <see cref="TriggerDefinition.ActiveZones"/> to include the Graveyard so the
/// ability is still observable after <see cref="Majik.Core.Zones.ZoneService"/>
/// stamps the death (same posture as Haywire Mite / Aven Fisher). No extra
/// fields; the source is always the card the ability lives on.
/// </summary>
public sealed class DiesSelfTriggerDef : TriggerDefinition
{
    private static readonly IReadOnlyList<ZoneType> BattlefieldAndGraveyard =
        new[] { ZoneType.Battlefield, ZoneType.Graveyard };

    /// <inheritdoc />
    public override IReadOnlyList<ZoneType>? ActiveZones => BattlefieldAndGraveyard;
}
