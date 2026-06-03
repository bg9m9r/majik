using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Avatar of the Resolute (Magic Origins, {G}{G}).
///
/// Creature — Avatar 3/2. Oracle text (verified against Scryfall):
///   "Reach, trample
///    This creature enters with a +1/+1 counter on it for each other creature
///    you control with a +1/+1 counter on it."
///
/// ## Shape source
/// Card identity (name, {G}{G}, 3/2, Creature — Avatar, green) is loaded from
/// <c>Majik.Core/CardData/Cards/avatar-of-the-resolute.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same data-driven identity pattern as
/// <see cref="NarnamRenegadeFactory"/> and <see cref="AmbushViperFactory"/>.
/// The Reach / Trample keyword markers and the dynamic enters-with-counters
/// clause are wired in code below: the JSON ability schema does not yet express
/// keyword markers or a dynamic-count ETB-counters replacement.
///
/// ## Implemented (v1)
/// - 3/2 Creature — Avatar at {G}{G}, green (color from the {G} pips per
///   CR 202.2c), owner / controller stamped.
/// - <b>Reach (CR 702.17)</b> — attached as a <see cref="KeywordAbility"/>
///   marker, same shape as <see cref="CanopySpiderFactory"/>. CombatAbilities
///   consumes the marker for can-block-fliers determination.
/// - <b>Trample (CR 702.19)</b> — attached as a <see cref="KeywordAbility"/>
///   marker, same shape as <see cref="PrimevalTitanFactory"/>. CombatDamage
///   consumes the marker for excess-damage trampling.
/// - <b>ETB +1/+1 counters (CR 603.6a / CR 614.1d)</b> — wired as an
///   ETB triggered ability over <see cref="CardMovedEvent"/>
///   Battlefield ← (non-battlefield) for this card. Resolve body counts the
///   <b>other</b> creatures the controller controls that have at least one
///   +1/+1 counter on them at resolution time, and adds that many +1/+1
///   counters to this creature.
///
///   Modeled as a trigger (not a true CR 614.1d replacement) because the count
///   depends on resolve-time battlefield state — the same retrofit posture
///   documented on <see cref="GolgariGraveTrollFactory"/> and
///   <see cref="EndlessOneFactory"/>. The <see cref="EntersWithCountersReplacement"/>
///   shape carries a fixed amount and does not yet support a dynamic battlefield
///   count, so the trigger model is the canonical v1 path. The observable end
///   state (counters on this creature) is identical; CR 116.5 — SBAs only run
///   when a player would get priority, so no intervening SBA pass occurs
///   between this creature's ETB and the trigger resolving.
///
///   "other" excludes this creature itself (CR 109.2-style self-reference): it
///   enters with no counters of its own, so even reflexively it would never
///   contribute to its own count, but the explicit reference-equality guard
///   keeps intent obvious.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Keyword markers + ETB trigger
///   attached structurally; the trigger is not registered with any
///   <see cref="TriggerManager"/>. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired; the ETB
///   trigger registers with <paramref name="triggers"/>.
/// </summary>
[CardName("Avatar of the Resolute")]
public static class AvatarOfTheResoluteFactory
{
    public const string CardName = "Avatar of the Resolute";
    public const string Slug = "avatar-of-the-resolute";

    /// <summary>Intrinsic keyword — CR 702.17 Reach.</summary>
    public const string ReachKeyword = "Reach";

    /// <summary>Intrinsic keyword — CR 702.19 Trample.</summary>
    public const string TrampleKeyword = "Trample";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Avatar of the Resolute with no live wiring. The Reach / Trample
    /// markers + ETB-counters trigger are attached for shape observability; the
    /// trigger is not registered with any <see cref="TriggerManager"/>. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Avatar of the Resolute with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager. When supplied the ETB-counters
    /// trigger registers so a self-enter <see cref="CardMovedEvent"/>
    /// automatically queues the trigger (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.17 — Reach. CombatAbilities reads this marker for the
        // can-block-creatures-with-flying determination.
        card.AddAbility(new KeywordAbility(ReachKeyword, card, owner));

        // CR 702.19 — Trample. CombatDamage reads this marker for assigning
        // excess combat damage to the defending player / planeswalker.
        card.AddAbility(new KeywordAbility(TrampleKeyword, card, owner));

        // ----------------------------------------------------------------
        // ETB +1/+1 counters — CR 603.6a / CR 614.1d.
        //   "This creature enters with a +1/+1 counter on it for each other
        //    creature you control with a +1/+1 counter on it."
        // Modeled as a trigger that snapshots the count of OTHER creatures the
        // controller controls bearing >= 1 +1/+1 counter at resolution time
        // and adds that many +1/+1 counters to this creature.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enters with a +1/+1 counter for each other creature you control with a +1/+1 counter",
            () =>
            {
                var controller = card.Controller ?? owner;
                var count = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Count(c => !ReferenceEquals(c, card)
                                && c.Counters.Count(CounterType.PlusOnePlusOne) > 0);
                if (count <= 0) return;
                card.Counters.Add(CounterType.PlusOnePlusOne, count);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>(
                (e, _) => ReferenceEquals(e.Card, card)
                          && e.ToZone == ZoneType.Battlefield
                          && e.FromZone != ZoneType.Battlefield),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
