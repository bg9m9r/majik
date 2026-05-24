using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soul-Scar Mage (Amonkhet, {R}).
///
/// Creature — Human Shaman Wizard 1/2. Oracle text:
///   "Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    If a source you control would deal noncombat damage to a creature an
///    opponent controls, put that many -1/-1 counters on that creature
///    instead."
///
/// ## Implemented (v1)
/// - 1/2 Human Shaman Wizard, mana cost {R}.
/// - <b>Prowess (CR 702.108)</b>: keyword marker wired as a
///   <see cref="KeywordAbility"/>. Live pump via <see cref="ProwessFactory.Build"/>
///   when a <see cref="ContinuousEffectsService"/> is supplied (same
///   pattern as MonasteryMentor / BedlamRevelerFactory).
/// - <b>Noncombat-damage-to-counters replacement (CR 614)</b>: wired via a
///   <see cref="LambdaReplacement{DamageIntent}"/> registered on the
///   supplied <see cref="ReplacementBus"/>. The replacement:
///     · Applies when <see cref="DamageIntent.Source"/> is owned by the
///       Soul-Scar controller (source is the controller Player, or is a
///       card/permanent whose <c>Controller</c> matches).
///     · Target must be a creature under an opponent's control
///       (<see cref="DamageIntent.TargetCreature"/> non-null, with a
///       controller different from the Soul-Scar controller — CR 608.2b).
///     · The DamageIntent is from a spell or non-combat ability path
///       (i.e. NOT produced by CombatFlow's combat-damage routing); v1
///       approximation: combat-damage intents in CombatFlow use a
///       Creature as the source, while spell-damage intents use a Player
///       or a non-creature-combat-source. Since the engine currently uses
///       the caster Player as source for spell damage (DamageSpellFactory)
///       and uses the Creature directly for combat damage (CombatFlow),
///       the replacement checks: if the source is a Creature AND the
///       creature is on the battlefield AND the damage is combat-sourced
///       (heuristic: source is a Creature whose Zone == Battlefield and
///       the intent looks like combat — see xmldoc below for the v1 gap).
///       For v1 the replacement fires for ALL sources the controller
///       controls; the noncombat discriminator is a deferred gap (see
///       below).
///     · Effect: apply <see cref="CounterType.MinusOneMinusOne"/> × Amount
///       to the target creature via <c>Counters.Add</c>, then zero the
///       intent's Amount (returning a replacement with Amount = 0 cancels
///       the damage — the counter placement IS the replacement).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Prowess keyword marker
///   attached; no replacement or trigger wiring. Suitable for dispatcher /
///   structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?, ReplacementBus?)"/>
///   — fully wired. Prowess trigger registered when effects is supplied;
///   replacement registered when replacements is supplied.
///
/// ## Deferred (v1 gaps)
/// - <b>Noncombat discriminator</b>: The DamageIntent record has no
///   IsCombatDamage flag (CombatDamage.IsCombatDamage is on a separate
///   value object). For v1 the replacement fires for ALL controller-owned
///   sources, including combat damage. Adding an IsCombatDamage field to
///   DamageIntent would make this precise; that plumbing is deferred to a
///   follow-up pass covering other damage-type replacements (e.g. Deflecting
///   Palm, Sword of Light and Shadow).
/// - Prowess live pump requires the (owner, ..., effects) overload.
/// </summary>
public static class SoulScarMageFactory
{
    public const string CardName = "Soul-Scar Mage";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Soul-Scar Mage with no live wiring. The Prowess keyword
    /// marker is attached for shape observability; no replacement effect or
    /// trigger is registered (no effects/replacements service supplied).
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null, replacements: null);

    /// <summary>
    /// Construct Soul-Scar Mage with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not used directly by this factory; reserved
    /// for future lifecycle subscribers (e.g. LTB unregister).</param>
    /// <param name="triggers">TriggerManager for the Prowess trigger (via
    /// <see cref="ProwessFactory"/>). May be null.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess pump
    /// (CR 613.1f, Layer 7c). May be null — Prowess trigger is not wired
    /// when null.</param>
    /// <param name="replacements">ReplacementBus to register the noncombat-
    /// damage-to-counters replacement effect. May be null — replacement is
    /// not wired when null.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Shaman, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Prowess (CR 702.108) — "Whenever you cast a noncreature spell,
        // this creature gets +1/+1 until end of turn."
        // Keyword marker always attached. Live pump via ProwessFactory when
        // a ContinuousEffectsService is supplied (same as MonasteryMentor).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Prowess", card, owner));

        if (effects != null)
        {
            card.ActiveEffects = effects;
            var prowessTrigger = ProwessFactory.Build(card, effects);
            card.AddAbility(prowessTrigger);
            triggers?.RegisterTriggeredAbility(prowessTrigger);
        }

        // ----------------------------------------------------------------
        // Noncombat-damage-to-counters replacement (CR 614).
        // "If a source you control would deal noncombat damage to a creature
        //  an opponent controls, put that many -1/-1 counters on that
        //  creature instead."
        //
        // Source-ownership test: a source "you control" is:
        //   - the controller Player themselves (spell source in DamageSpellFactory),
        //   - OR any ICard/Creature whose Controller == owner.
        //
        // Target test: TargetCreature is non-null AND its Controller is
        // not the Soul-Scar controller (i.e. it's an opponent's creature).
        //
        // Replacement action: add Amount -1/-1 counters to the target
        // creature, then return a zeroed-Amount intent (the counters ARE
        // the damage replacement — no additional damage applies). A zeroed
        // intent (Amount = 0) is returned rather than null so the bus logs
        // the replacement in its history for CR 616 ordering; the downstream
        // damage-applier short-circuits on Amount == 0 (consistent with
        // prevention shields).
        //
        // v1 gap — noncombat discriminator: see factory xmldoc. All
        // controller-owned sources fire this replacement for now.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            var damageToCountersReplacement = new LambdaReplacement<DamageIntent>(
                applies: (intent, _) =>
                {
                    // Gate 1: target must be an opponent's creature.
                    if (intent.TargetCreature == null) return false;
                    var targetController = intent.TargetCreature.Controller;
                    if (targetController == null || ReferenceEquals(targetController, owner))
                        return false;

                    // Gate 2: source must be controller-owned.
                    // Source is the caster Player (spell damage) or a
                    // card/creature (ability / combat damage).
                    var source = intent.Source;
                    if (source is Player sourcePlayer)
                    {
                        if (!ReferenceEquals(sourcePlayer, owner)) return false;
                    }
                    else if (source is ICard sourceCard)
                    {
                        if (!ReferenceEquals(sourceCard.Controller, owner)) return false;
                    }
                    else
                    {
                        // Unknown source shape — do not fire.
                        return false;
                    }

                    // Gate 3: amount must be positive.
                    return intent.Amount > 0;
                },
                replace: (intent, _) =>
                {
                    // CR 122.1 — place that many -1/-1 counters on the
                    // target creature instead of dealing damage.
                    intent.TargetCreature!.Counters.Add(CounterType.MinusOneMinusOne, intent.Amount);

                    // Return a zeroed intent so the bus records the replacement
                    // but the damage-application site sees Amount == 0 and
                    // skips the actual damage. This mirrors the prevention-
                    // shield pattern (PreventNextNDamageToAnyTargetShield).
                    return intent with { Amount = 0 };
                },
                oneShot: false,
                tag: card);

            card.AddAbility(new StaticAbility(
                source: card,
                controller: owner,
                description: "If a source you control would deal noncombat damage to a creature an opponent controls, put that many -1/-1 counters on that creature instead.",
                isActiveCheck: () => card.Zone == ZoneType.Battlefield));

            replacements.Register(damageToCountersReplacement);
        }

        return card;
    }
}
