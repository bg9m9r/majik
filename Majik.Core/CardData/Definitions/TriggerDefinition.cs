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
[JsonDerivedType(typeof(CastSelfTriggerDef), "cast_self")]
[JsonDerivedType(typeof(AttacksSelfTriggerDef), "attacks_self")]
[JsonDerivedType(typeof(DiesSelfTriggerDef), "dies_self")]
[JsonDerivedType(typeof(AtBeginningOfYourUpkeepTriggerDef), "at_beginning_of_your_upkeep")]
[JsonDerivedType(typeof(AtBeginningOfYourEndStepTriggerDef), "at_beginning_of_your_end_step")]
[JsonDerivedType(typeof(WheneverAnotherCreatureEntersTriggerDef), "whenever_another_creature_enters")]
[JsonDerivedType(typeof(WheneverAnotherCreatureDiesTriggerDef), "whenever_another_creature_dies")]
[JsonDerivedType(typeof(DealsCombatDamageToPlayerSelfTriggerDef), "whenever_this_deals_combat_damage_to_a_player")]
[JsonDerivedType(typeof(WheneverACreatureYouControlExploresTriggerDef), "whenever_a_creature_you_control_explores")]
[JsonDerivedType(typeof(StateWhenCountersGeTriggerDef), "state_when_counters_ge")]
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
/// "When you cast this spell, …" (CR 601.2i / 603.3) — a self-cast trigger
/// that fires as this very card is put on the stack as a spell. Fires on a
/// <see cref="Majik.Core.Domain.DomainEvents.SpellCastEvent"/> whose
/// <see cref="Majik.Core.Spells.ISpell.Card"/> IS this card (reference match),
/// the cast-trigger family (Eldrazi cast-riders, Annihilator-Eldrazi
/// "draw four", Demigod of Revenge). This is the self-scoped sibling of
/// <see cref="WheneverYouCastSpellTriggerDef"/> (which fires on OTHER spells you
/// cast): no controller read is needed because the source-card match already
/// implies "you cast" — a spell is on the stack only under its controller's
/// cast (CR 601.2). No extra fields; the source is always the card the ability
/// lives on.
///
/// <para>
/// Because a "when you cast this spell" ability is functional while the card is
/// on the stack (CR 603.3e — it triggers from the stack as the spell is cast),
/// the trigger overrides <see cref="TriggerDefinition.ActiveZones"/> to include
/// the Stack so <see cref="Majik.Core.Abilities.TriggerManager"/> still observes
/// it after <see cref="Majik.Core.Zones.ZoneService"/> stamps the card onto the
/// stack as part of casting (same posture as <c>dies_self</c> reaching into the
/// Graveyard).
/// </para>
/// </summary>
public sealed class CastSelfTriggerDef : TriggerDefinition
{
    private static readonly IReadOnlyList<ZoneType> BattlefieldAndStack =
        new[] { ZoneType.Battlefield, ZoneType.Stack };

    /// <inheritdoc />
    public override IReadOnlyList<ZoneType>? ActiveZones => BattlefieldAndStack;
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

/// <summary>
/// "At the beginning of your upkeep, …" (CR 500.1 / CR 603.1) — a phase /
/// step trigger over <see cref="Majik.Core.Events.StepStartedEvent"/>
/// restricted to the trigger controller's own <c>Upkeep</c> step. Maps to
/// <see cref="Majik.Core.Abilities.Triggers.OnStepBegin"/> with
/// <see cref="Majik.Core.StateMachine.PhaseStateType.Upkeep"/>. Controller is
/// resolved live (<c>card.Controller</c>) at fire time so a control change
/// carries the trigger (CR 109.5 — same posture as the other declarative
/// variants). No extra fields. Models the Phyrexian Arena / Dark Confidant /
/// Howling Mine "your-upkeep" family.
/// </summary>
public sealed class AtBeginningOfYourUpkeepTriggerDef : TriggerDefinition { }

/// <summary>
/// "At the beginning of your end step, …" (CR 513.1 / CR 603.1) — a phase /
/// step trigger over <see cref="Majik.Core.Events.StepStartedEvent"/>
/// restricted to the trigger controller's own <c>End</c> step. Maps to
/// <see cref="Majik.Core.Abilities.Triggers.OnStepBegin"/> with
/// <see cref="Majik.Core.StateMachine.PhaseStateType.End"/>. Controller is
/// resolved live (CR 109.5). No extra fields. Models the Wedding Announcement /
/// Bonecrusher Giant-token / end-step-payoff family.
/// </summary>
public sealed class AtBeginningOfYourEndStepTriggerDef : TriggerDefinition { }

/// <summary>
/// "Whenever another creature enters, …" (CR 603.6e) — fires on a creature
/// OTHER than this permanent entering the battlefield, over
/// <see cref="Majik.Core.Events.CardMovedEvent"/>. The "another creature"
/// self-exclusion is what distinguishes it from the ETB-self variant.
///
/// <para>
/// Optional <see cref="YouControlOnly"/> scope (CR 109.5):
/// <list type="bullet">
///   <item><c>false</c> (default) — ANY creature entering under any player's
///   control fires it. Models the Soul Warden / Essence Warden / Soul's
///   Attendant / Auriok Champion "whenever another creature enters, you gain 1
///   life" family.</item>
///   <item><c>true</c> — only a creature entering under the trigger
///   controller (resolved live so a control change carries the trigger). Models
///   the Cathars' Crusade / Impact-Tremors "creature you control enters"
///   family.</item>
/// </list>
/// </para>
/// </summary>
public sealed class WheneverAnotherCreatureEntersTriggerDef : TriggerDefinition
{
    /// <summary>Restrict to creatures entering under the trigger controller
    /// (CR 109.5). Default <c>false</c> = any creature entering.</summary>
    public bool YouControlOnly { get; set; }

    /// <summary>
    /// Optional subtype/tribal gate (CR 205.3 / 700.4) — only a creature whose
    /// (printed) subtypes include this one fires the trigger. <c>null</c>/empty
    /// = no subtype restriction. Parsed as
    /// <see cref="Majik.Core.Cards.Types.CardSubtype"/>. Models the
    /// "another Warrior you control enters" gate (Mardu Woe-Reaper).
    /// </summary>
    public string? Subtype { get; set; }

    /// <summary>
    /// When <c>true</c>, the source permanent's OWN entry also fires the
    /// trigger (CR 603.6e), modelling the "this creature OR another [Warrior]
    /// you control enters" wording (Mardu Woe-Reaper). Default <c>false</c> =
    /// "ANOTHER creature" only (self excluded), the Soul Warden shape.
    /// </summary>
    public bool IncludeSelf { get; set; }
}

/// <summary>
/// "Whenever another creature [you control] dies, …" (CR 603.6e / CR 700.4) —
/// the aristocrat death-payoff mirror of
/// <see cref="WheneverAnotherCreatureEntersTriggerDef"/>. Fires on a
/// Battlefield → Graveyard move of a creature OTHER than this permanent, over
/// <see cref="Majik.Core.Events.CardMovedEvent"/>. The "another creature"
/// self-exclusion distinguishes it from <see cref="DiesSelfTriggerDef"/>.
///
/// <para>
/// Optional, composable filters:
/// <list type="bullet">
///   <item><see cref="YouControlOnly"/> (CR 109.5) — restrict to a creature
///   whose controller is the trigger's controller (resolved live so a control
///   change carries the trigger). Models the Zulaport Cutthroat / Midnight
///   Reaper "creature you control dies" family; <c>false</c> (default) = ANY
///   creature dying (Blood Artist).</item>
///   <item><see cref="NontokenOnly"/> (CR 111 / 700.4) — exclude token
///   creatures (a token ceases to exist as an SBA after dying, CR 111.7).
///   Models the Midnight Reaper "nontoken creature you control dies"
///   shape.</item>
///   <item><see cref="Subtype"/> — tribal gate; only a creature of the given
///   subtype fires it.</item>
/// </list>
/// </para>
///
/// Overrides <see cref="TriggerDefinition.ActiveZones"/> to include the
/// Graveyard so the ability is still observable after
/// <see cref="Majik.Core.Zones.ZoneService"/> stamps the death — including the
/// source's OWN death when <see cref="YouControlOnly"/> means the source
/// counts (CR 603.6c — same posture as the hand-rolled Blood Artist /
/// Zulaport Cutthroat factories).
/// </summary>
public sealed class WheneverAnotherCreatureDiesTriggerDef : TriggerDefinition
{
    private static readonly IReadOnlyList<ZoneType> BattlefieldAndGraveyard =
        new[] { ZoneType.Battlefield, ZoneType.Graveyard };

    /// <summary>Restrict to creatures whose controller is the trigger
    /// controller (CR 109.5). Default <c>false</c> = any creature dying.</summary>
    public bool YouControlOnly { get; set; }

    /// <summary>Exclude token creatures (CR 111 / 111.7). Default
    /// <c>false</c>.</summary>
    public bool NontokenOnly { get; set; }

    /// <summary>Optional subtype/tribal gate (CR 205.3). <c>null</c>/empty =
    /// no subtype restriction. Parsed as
    /// <see cref="Majik.Core.Cards.Types.CardSubtype"/>.</summary>
    public string? Subtype { get; set; }

    /// <inheritdoc />
    public override IReadOnlyList<ZoneType>? ActiveZones => BattlefieldAndGraveyard;
}

/// <summary>
/// "Whenever this creature deals combat damage to a player, …" (CR 510.2 /
/// CR 603.1) — a per-source combat-damage trigger over
/// <see cref="Majik.Core.Domain.DomainEvents.CombatDamageDealtEvent"/> whose
/// <see cref="Majik.Core.Domain.DomainEvents.CombatDamageDealtEvent.Source"/>
/// is this card and whose target is a player (non-null
/// <see cref="Majik.Core.Events.DamageDealtEvent.TargetPlayer"/>). Models the
/// Ophidian / Hypnotic Specter / "combat-damage-to-a-player" payoff family
/// (the same event Ragavan's hand-rolled trigger reads). No extra fields; the
/// source is always the card the ability lives on.
/// </summary>
public sealed class DealsCombatDamageToPlayerSelfTriggerDef : TriggerDefinition { }

/// <summary>
/// "Whenever a creature you control explores, …" (CR 701.40e). Fires on a
/// <see cref="Majik.Core.Events.CreatureExploredEvent"/> whose
/// <see cref="Majik.Core.Events.CreatureExploredEvent.Controller"/> is the
/// trigger's controller (CR 109.5 — "a creature you control"; the controller is
/// resolved live so a control change carries the trigger). The exploring
/// permanent itself counts ("a creature you control" includes the source if it
/// explores). Maps to an
/// <see cref="Majik.Core.Abilities.EventTriggerCondition{TEvent}"/> with the
/// live-controller predicate — the SAME condition the hand-rolled Wildgrowth
/// Walker factory uses ("Whenever a creature you control explores, put a +1/+1
/// counter on this creature and you gain 3 life"). No extra fields.
/// </summary>
public sealed class WheneverACreatureYouControlExploresTriggerDef : TriggerDefinition { }

/// <summary>
/// "When there are <see cref="Threshold"/> or more <see cref="Counter"/>
/// counters on this permanent, …" — a <em>state trigger</em> (CR 603.8).
/// Unlike an event trigger, a state trigger fires whenever its condition is
/// satisfied rather than in response to a particular event; the engine models
/// it as a <see cref="Majik.Core.Abilities.StateChangeTriggerCondition"/>
/// (rising-edge over the predicate) evaluated by
/// <see cref="Majik.Core.Abilities.TriggerManager.EvaluateStateChangeTriggers"/>
/// after each state-based-action pass (CR 704 checkpoint where 603.8 triggers
/// are looked for, CR 603.3).
///
/// <para>
/// The predicate reads the live page/charge/quest/etc. counter count off this
/// permanent's <see cref="Majik.Core.Cards.Permanent.Counters"/> bag and
/// returns true once it is &gt;= <see cref="Threshold"/>. Models the
/// counter-threshold payoff family: Mazemind Tome ("four or more page
/// counters → exile it, gain 4 life"), and broadly any "When there are N or
/// more [type] counters on this …" state trigger. The threshold-reached
/// effect itself (exile / gain life / etc.) is supplied separately by the
/// card's effect list — this def only expresses the firing condition.
/// </para>
/// </summary>
public sealed class StateWhenCountersGeTriggerDef : TriggerDefinition
{
    /// <summary>The counter type whose count gates the trigger
    /// (e.g. <c>"Page"</c>). Parsed as a
    /// <see cref="Majik.Core.Counters.CounterType"/> by name.</summary>
    public string Counter { get; set; } = string.Empty;

    /// <summary>The inclusive lower bound: the trigger fires on the rising
    /// edge of <c>count &gt;= Threshold</c> (CR 603.8). Mazemind Tome = 4.</summary>
    public int Threshold { get; set; }
}
