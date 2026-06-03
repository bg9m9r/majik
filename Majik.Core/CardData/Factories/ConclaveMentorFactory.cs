using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conclave Mentor (Jumpstart / Commander Legends, {G}{W}).
///
/// Creature — Centaur Cleric 2/2. Oracle text:
///   "If one or more +1/+1 counters would be put on a creature you control,
///    that many plus one +1/+1 counters are put on that creature instead.
///    When this creature dies, you gain life equal to its power."
///
/// ## Shape source
/// Card identity (name, {G}{W}, 2/2, Centaur Cleric) is loaded from
/// <c>Majik.Core/CardData/Cards/conclave-mentor.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The counter-replacement and the dies
/// trigger are attached in code below.
///
/// ## Implemented (v1)
/// - <b>Counter replacement (CR 614)</b>: same intent pathway as
///   <see cref="HardenedScalesAddReplacement"/>, but Conclave Mentor adds
///   exactly ONE extra counter (it does not double like Branching Evolution).
///   A direct <see cref="Counters.CounterAddIntent"/> routed via
///   <see cref="Majik.Core.Services.CountersService.Add"/> for one-or-more
///   +1/+1 counters on a creature controlled by Conclave Mentor's controller
///   is bumped by 1. The ETB-counter route is intentionally NOT intercepted
///   here — Conclave Mentor's printed bonus applies to all +1/+1 placement,
///   but the direct-add route is the same one the suggested analogue exercises;
///   the ETB route mirror is a follow-up shared with Hardened Scales' own
///   ETB replacement and is not duplicated per card.
/// - <b>Dies trigger (CR 603.6c / 700.4)</b>: "you gain life equal to its
///   power." Reads the creature's power (CR 603.10a last-known-information —
///   the value is taken as the trigger resolves; with counters still applied
///   the power reflects them). Fires on Battlefield → Graveyard; active in
///   both zones because <see cref="Majik.Core.Zones.ZoneService"/> stamps the
///   new zone before publishing the <c>CardMovedEvent</c>. Life routed through
///   <see cref="Fx.GainLife"/> (CR 119.3) so life-gain triggers + the
///   <c>LifeChangedEvent</c> fire.
/// </summary>
[CardName("Conclave Mentor")]
public static class ConclaveMentorFactory
{
    public const string CardName = "Conclave Mentor";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("conclave-mentor");

    /// <summary>
    /// Construct Conclave Mentor with the dies trigger attached but neither the
    /// trigger nor the counter replacement registered with any manager / bus.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Conclave Mentor. When <paramref name="triggers"/> is supplied
    /// the dies trigger is registered so the <c>CardMovedEvent</c> places it on
    /// the stack automatically (CR 603.3). When <paramref name="replacements"/>
    /// is supplied a <see cref="ConclaveMentorAddReplacement"/> is registered so
    /// the printed "+1 counter" bump fires on every matching
    /// <see cref="Counters.CounterAddIntent"/> routed via
    /// <see cref="Majik.Core.Services.CountersService.Add"/>.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Counter replacement — CR 614. "If one or more +1/+1 counters
        // would be put on a creature you control, that many plus one ...
        // are put on that creature instead." +1, never doubling.
        // ----------------------------------------------------------------
        replacements?.Register<CounterAddIntent>(new ConclaveMentorAddReplacement(card));

        // ----------------------------------------------------------------
        // Dies triggered ability — CR 603.6c / 700.4.
        //   "When this creature dies, you gain life equal to its power."
        // Power is read as the trigger resolves (CR 603.10a). Active in
        // Battlefield + Graveyard: ZoneService stamps the zone before
        // publishing the CardMovedEvent (mirrors Filigree Familiar).
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName} dies: gain life equal to its power",
            () =>
            {
                var controller = card.Controller ?? owner;
                var power = card.Power;     // CR 603.10a last-known-information
                if (power > 0)
                {
                    Fx.GainLife(controller, power); // CR 119.3
                }
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}

/// <summary>
/// CR 614 replacement: when a direct +1/+1 counter placement intent would put
/// one or more +1/+1 counters on a creature controlled by Conclave Mentor's
/// controller, bump the count by exactly one. Mirrors
/// <see cref="HardenedScalesAddReplacement"/> but adds +1 rather than doubling.
/// </summary>
public sealed class ConclaveMentorAddReplacement : IReplacementEffect<CounterAddIntent>
{
    private readonly Creature _source;

    public ConclaveMentorAddReplacement(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(CounterAddIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.Type != CounterType.PlusOnePlusOne) return false;
        if (intent.Amount < 1) return false;
        if (intent.Target is not Creature) return false;
        return ReferenceEquals(intent.Target.Controller, _source.Controller);
    }

    public CounterAddIntent? Replace(CounterAddIntent intent, IReadOnlyList<object> history) =>
        intent with { Amount = intent.Amount + 1 };
}
