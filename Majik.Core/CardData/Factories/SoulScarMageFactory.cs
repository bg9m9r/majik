using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soul-Scar Mage (Amonkhet, {R}).
///
/// Creature — Human Monk 1/2. Oracle text:
///   "Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    If a source you control would deal noncombat damage to a creature
///    an opponent controls, put that many -1/-1 counters on that creature
///    instead."
///
/// ## Implementation
///
/// - 1/2 Human Monk, mana cost {R}.
/// - <b>Prowess (CR 702.108)</b>: wired via <see cref="ProwessFactory.Build"/>
///   when a <see cref="ContinuousEffectsService"/> is supplied. Mirrors
///   <see cref="MonasteryMentorFactory"/>'s shape — the keyword marker is
///   surfaced as a <see cref="TriggeredAbility"/>; no separate keyword
///   marker is added.
/// - <b>Damage → -1/-1 counters replacement (CR 614 + CR 122 + CR 119)</b>:
///   registered on the supplied <see cref="ReplacementBus"/> as a
///   <see cref="SoulScarMageDamageReplacement"/>. The replacement inspects
///   every <see cref="DamageIntent"/> and:
///     * gates on Soul-Scar Mage being on the battlefield (so LTB / flicker
///       lifts the rider naturally — same lifecycle pattern as
///       <see cref="ContainmentPriestExileReplacementEffect"/> /
///       <see cref="StonySilenceStaticEffect"/>);
///     * skips combat damage by filtering out intents whose
///       <see cref="DamageIntent.Source"/> is a <see cref="Creature"/>
///       (CR 510 — combat damage is always from a creature on the
///       battlefield; non-combat damage is sourced from a spell / ability
///       — currently surfaced as the controller <see cref="Player"/> in
///       <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageSpellFactory"/>);
///     * gates on the source being controlled by Soul-Scar Mage's
///       controller (controller-comparison from a <see cref="Player"/>
///       source today; future ICard-aware source threading inherits the
///       same controller-on-source check);
///     * gates on a creature target controlled by an opponent
///       (<see cref="DamageIntent.TargetCreature"/> set + controller !=
///       Soul-Scar Mage's controller).
///   On match the replacement returns a zero-damage intent and stamps
///   <c>N</c> <see cref="CounterType.MinusOneMinusOne"/> counters on the
///   target creature (CR 614.1b — "instead" replacements; per CR 122.3 the
///   counters are placed as part of the replacement, not as a separate
///   effect).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Prowess is NOT wired
///   (no effects service). The damage→counters replacement is NOT
///   registered. Suitable for dispatcher / structural tests. Mirrors
///   <see cref="AngerOfTheGodsFactory.Create(Player)"/>'s shape-only
///   posture.
/// - <see cref="Create(Player, ContinuousEffectsService?, ReplacementBus?, TriggerManager?)"/>
///   — fully wired. Prowess trigger registered when <paramref name="effects"/>
///   is supplied; damage→counters replacement registered when
///   <paramref name="replacements"/> is supplied.
///
/// ## Deferred (v1 gaps)
/// - <b>Source identity</b>: spell damage today threads the casting
///   <see cref="Player"/> as the <see cref="DamageIntent.Source"/>, not
///   the resolving spell card (see DamageSpellFactory's Filter helper).
///   That's fine for the "you control" check here — controller equality is
///   the same — but a future ICard-aware threading will need the
///   "controlled by Soul-Scar Mage's controller" predicate to consult
///   <c>ICard.Controller</c> instead. The replacement handles both shapes.
/// - <b>Ability-source damage</b>: ability pings (Walking Ballista, Goblin
///   Bombardment, etc.) currently call <c>target.TakeDamage</c> directly
///   rather than routing through <see cref="ReplacementBus"/>. Soul-Scar
///   Mage's rider only catches damage that flows through the bus — same
///   coverage gap as Anger of the Gods's exile rider and the Fog shield.
///   When ability damage starts routing through the bus the same filter
///   will pick it up without further changes (provided the source is
///   threaded as the controller Player or an ICard whose Controller is
///   readable).
/// - <b>Multiple replacements ordering</b>: if two damage-replacement
///   effects apply to the same intent CR 616 lets the affected player
///   choose order; <see cref="ReplacementBus.Apply"/> currently uses
///   registration order (CR 616.1c each effect fires at most once). Same
///   simplification as every other replacement here.
/// </summary>
[CardName("Soul-Scar Mage")]
public static class SoulScarMageFactory
{
    public const string CardName = "Soul-Scar Mage";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Soul-Scar Mage with no live wiring. Prowess is not wired
    /// (no effects service supplied) and the damage→counters replacement
    /// is not registered (no replacement bus supplied). Suitable for
    /// dispatcher / structural tests. Mirrors
    /// <see cref="AngerOfTheGodsFactory.Create(Player)"/>'s shape-only
    /// posture.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, replacements: null, triggers: null);

    /// <summary>
    /// Construct Soul-Scar Mage with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess pump
    /// effect (CR 613.1f, Layer 7c). May be null — Prowess trigger is not
    /// wired when null.</param>
    /// <param name="replacements">ReplacementBus to register the
    /// damage→-1/-1 counters replacement against (CR 614). May be null —
    /// no replacement is registered. The replacement self-gates on
    /// Soul-Scar Mage being on the battlefield, so LTB / flicker disables
    /// the rider naturally without explicit unregister.</param>
    /// <param name="triggers">TriggerManager for the Prowess trigger.
    /// May be null — the trigger is still attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Monk });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Prowess (CR 702.108) — "Whenever you cast a noncreature spell,
        // this creature gets +1/+1 until end of turn."
        // Wired via ProwessFactory.Build when a ContinuousEffectsService
        // is supplied; same shape as MonasteryMentorFactory. Layer 7c
        // pump flows through card.ActiveEffects so Power / Toughness
        // reads recompute via the layers pipeline (CR 613 Layer 7c).
        // ----------------------------------------------------------------
        if (effects != null)
        {
            card.ActiveEffects = effects;
            var prowessTrigger = ProwessFactory.Build(card, effects);
            card.AddAbility(prowessTrigger);
            triggers?.RegisterTriggeredAbility(prowessTrigger);
        }

        // ----------------------------------------------------------------
        // Damage → -1/-1 counters replacement (CR 614).
        // The replacement self-gates on Soul-Scar Mage being on the
        // battlefield via SoulScarMageDamageReplacement.Applies — flicker
        // / LTB lifts the rider naturally without an unregister.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<DamageIntent>(new SoulScarMageDamageReplacement(card));
        }

        return card;
    }
}

/// <summary>
/// Replacement effect for Soul-Scar Mage's "noncombat damage → -1/-1
/// counters" clause. Inspects every <see cref="DamageIntent"/> on the
/// <see cref="ReplacementBus"/> and rewrites matching intents to
/// zero-damage while stamping <c>N</c>
/// <see cref="CounterType.MinusOneMinusOne"/> counters on the target
/// creature (CR 614.1b — "instead" replacements; CR 122.3 — counter
/// placement as the replacement payload).
///
/// Match predicate (all must hold):
///   - Soul-Scar Mage is on the battlefield (CR 614.6 — replacement is
///     only active while the printed source is in the right zone).
///   - <see cref="DamageIntent.Source"/> is NOT a <see cref="Creature"/>
///     (CR 510 — combat damage is creature-sourced; non-combat damage is
///     sourced from a spell / ability — currently surfaced as the
///     controller <see cref="Player"/> via
///     <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageSpellFactory"/>'s
///     Filter helper).
///   - The source is controlled by Soul-Scar Mage's controller (Player
///     source compared directly; ICard source via <see cref="ICard.Controller"/>).
///   - <see cref="DamageIntent.TargetCreature"/> is set AND its
///     <see cref="Creature.Controller"/> is NOT Soul-Scar Mage's
///     controller (opponent's creature, per the printed "creature an
///     opponent controls").
///
/// Lifecycle: not <see cref="IEndOfTurnExpirable"/> — Soul-Scar Mage's
/// static is "while on the battlefield", not per-turn. The
/// <c>Applies</c> gate's battlefield-check short-circuits when Soul-Scar
/// Mage is elsewhere, so flicker / LTB lifts the rider correctly without
/// the bus removing the entry (same "no LTB unregister" pattern as
/// <see cref="PlagueEngineerFactory"/>'s LordStaticEffect).
/// </summary>
public sealed class SoulScarMageDamageReplacement : IReplacementEffect<DamageIntent>
{
    private readonly Creature _source;

    public SoulScarMageDamageReplacement(Creature source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    /// <summary>The Soul-Scar Mage instance this replacement is keyed to.</summary>
    public Creature Source => _source;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        // CR 614.6 — replacement is only active while Soul-Scar Mage is on
        // the battlefield.
        if (_source.Zone != ZoneType.Battlefield) return false;

        // Zero-damage / cancelled intents — nothing to redirect.
        if (intent.Amount <= 0) return false;

        // Combat damage is creature-sourced (CR 510); skip it.
        if (intent.Source is Creature) return false;

        // Target must be a creature an opponent controls.
        if (intent.TargetCreature is not Creature target) return false;
        if (ReferenceEquals(target.Controller, _source.Controller)) return false;

        // Source must be controlled by Soul-Scar Mage's controller.
        // Today spell damage threads the casting Player as Source; ability
        // damage doesn't route through the bus yet. ICard branch covers
        // future card-aware threading.
        var sourceController = intent.Source switch
        {
            Player p => p,
            ICard c => c.Controller,
            _ => null,
        };
        if (!ReferenceEquals(sourceController, _source.Controller)) return false;

        return true;
    }

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
    {
        // CR 614.1b — "instead" replacement. Stamp the counters AS the
        // replacement payload, then zero out the damage so the caller
        // doesn't double-apply.
        if (intent.TargetCreature is Creature target && intent.Amount > 0)
        {
            target.Counters.Add(CounterType.MinusOneMinusOne, intent.Amount);
        }
        return intent with { Amount = 0 };
    }
}
