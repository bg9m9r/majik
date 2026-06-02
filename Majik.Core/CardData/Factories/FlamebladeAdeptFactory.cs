using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flameblade Adept (Amonkhet, {R}). Creature —
/// Jackal Warrior 1/2. Oracle text (verified against Scryfall):
///   "Menace
///    Whenever you cycle or discard a card, this creature gets +1/+0 until
///    end of turn."
///
/// The card's base shape (name, Creature, Jackal Warrior subtypes, {R}, 1/2)
/// is materialised from the embedded JSON definition
/// (<c>flameblade-adept.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (Menace keyword marker, cycle/discard pump trigger) are layered on top
/// here — the JSON <c>AbilityDefinition</c> schema doesn't express keyword
/// markers or triggered abilities, so they live in the factory (same posture
/// as <see cref="HorrorOfTheBrokenLandsFactory"/>, the suggested analogue,
/// whose cycle/discard pump trigger this reuses).
///
/// ## Implemented (v1)
///
/// - <b>Creature — Jackal Warrior {R} 1/2</b> from JSON.
/// - <b>Menace</b> (CR 702.110) — <see cref="KeywordAbility"/> marker,
///   consumed by <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/>
///   (same posture as <see cref="InsolentNeonateFactory"/> / Grief / Hive of
///   the Eye Tyrant).
/// - <b>"Whenever you cycle ... a card, +1/+0 EOT" trigger</b> (CR 603.1):
///   wired as a <see cref="TriggeredAbility"/> over
///   <see cref="EventTriggerCondition{CardCycledEvent}"/> filtered to
///   <c>e.Player == card.Controller</c> ("you cycle", CR 109.5). Unlike
///   Horror of the Broken Lands, Flameblade Adept's printed text is "a card"
///   (NOT "another card"), so cycling Flameblade Adept itself WOULD fire its
///   trigger — but Flameblade Adept has no cycling ability of its own and is
///   a creature on the battlefield, not a card you cycle from hand, so there
///   is no self-gate to apply. <c>activeZones = Battlefield</c> (abilities on
///   a creature card function from the battlefield only, CR 113.6). On
///   resolve registers a one-turn <see cref="PumpUntilEndOfTurnEffect"/>
///   (+1/+0) on the supplied <see cref="ContinuousEffectsService"/> (CR
///   613.1f, Layer 7c — flows through the layers pipeline; self-expires at
///   cleanup, CR 514.2).
///
/// ## Discard surface deferral
///
/// The "or discard a card" half of the printed trigger is NOT wired in v1 —
/// identical posture to <see cref="HorrorOfTheBrokenLandsFactory"/> and
/// <see cref="CuratorOfMysteriesFactory"/>. The engine has no dedicated
/// <c>DiscardedEvent</c> surface today, so the trigger only fires on cycle
/// events (<see cref="CardCycledEvent"/>). Cycling is the load-bearing half
/// (Flameblade Adept was printed for the Amonkhet cycling shell); the
/// discard half is a small future wire-up once a <c>DiscardedEvent</c>
/// surface ships.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The cycle trigger is
///   attached for shape inspection (the pump registers against a fresh
///   throwaway effects service so structural tests can fire it harmlessly).
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?)"/>
///   — pump wired against the supplied layers service + trigger registered.
///
/// CR rule references: 205.3m (Jackal/Warrior subtypes), 702.110 (Menace),
/// 603.1 (triggered ability), 613.1f / 514.2 (EOT pump lifecycle).
/// </summary>
[CardName("Flameblade Adept")]
public static class FlamebladeAdeptFactory
{
    public const string CardName = "Flameblade Adept";
    public const string Slug = "flameblade-adept";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int PumpPower = 1;
    public const int PumpToughness = 0;

    /// <summary>
    /// Construct Flameblade Adept with no live wiring. Menace marker +
    /// the cycle trigger are attached (the trigger's pump registers against a
    /// throwaway effects service so structural tests can fire the effect
    /// harmlessly). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, triggers: null);

    /// <summary>
    /// Construct Flameblade Adept with the pump wired against the supplied
    /// <paramref name="effects"/> service and the trigger registered with
    /// <paramref name="triggers"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Layers service the +1/+0 pump registers against
    /// (CR 613.1f, Layer 7c). When null a fresh throwaway service is used so
    /// the trigger still attaches + fires structurally.</param>
    /// <param name="triggers">TriggerManager the cycle trigger registers with
    /// so a <see cref="CardCycledEvent"/> auto-queues the pump. May be
    /// null.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Jackal Warrior subtypes, {R}, 1/2). The JSON carries no abilities —
        // Menace + the pump trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.110 — Menace keyword marker. Consumed by
        // CombatAbilities.HasMenace at block-declaration time.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // ----------------------------------------------------------------
        // "Whenever you cycle ... a card, this creature gets +1/+0 until end
        // of turn." (CR 603.1)
        //
        // EventTriggerCondition<CardCycledEvent> gated to:
        //   e.Player == card.Controller — "you cycle" (CR 109.5).
        // activeZones = Battlefield (CR 113.6 — abilities on a creature card
        // function from the battlefield only).
        //
        // On resolve registers a one-turn +1/+0 PumpUntilEndOfTurnEffect on
        // the layers service (CR 613.1f, Layer 7c) that self-expires at
        // cleanup (CR 514.2). Same pump lifecycle as Horror of the Broken
        // Lands.
        //
        // Discard-half deferred — engine has no DiscardedEvent surface today
        // (see class doc; identical posture to Horror of the Broken Lands /
        // Curator of Mysteries). The cycle leg alone covers the cycling-shell
        // payoff role.
        // ----------------------------------------------------------------
        var layers = effects ?? new ContinuousEffectsService();
        card.ActiveEffects = layers;

        var pump = new Effect(
            $"{CardName}: +{PumpPower}/+{PumpToughness} until end of turn (cycle or discard a card)",
            () => layers.Register(new PumpUntilEndOfTurnEffect(card, PumpPower, PumpToughness)));

        var cycleCondition = new EventTriggerCondition<CardCycledEvent>(
            (e, _) => ReferenceEquals(e.Player, card.Controller ?? owner));

        var cycleTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cycleCondition,
            effects: new IEffect[] { pump },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(cycleTrigger);
        triggers?.RegisterTriggeredAbility(cycleTrigger);

        return card;
    }
}
