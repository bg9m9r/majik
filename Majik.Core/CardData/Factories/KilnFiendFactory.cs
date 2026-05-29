using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kiln Fiend (Rise of the Eldrazi, {1}{R}).
///
/// Creature — Elemental Beast 1/2. Oracle text:
///   "Whenever you cast an instant or sorcery spell, this creature gets
///    +3/+0 until end of turn."
///
/// ## Implementation
///
/// - 1/2 Elemental Beast, mana cost {1}{R}.
/// - <b>Instant/sorcery-cast pump trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   matches when the spell's controller is Kiln Fiend's controller AND the
///   spell's card has type <see cref="CardType.Instant"/> or
///   <see cref="CardType.Sorcery"/> (CR 300.1 / 307.1; the printed oracle
///   tests the card types of the spell as cast — CR 112.1). On resolution
///   it registers a <see cref="KilnFiendPumpEffect"/> on the supplied
///   <see cref="ContinuousEffectsService"/> — a Layer-7c +3/+0 modification
///   expiring at end of turn (CR 613.7c). Same SpellCastEvent → end-of-turn
///   pump shape as <see cref="Majik.Core.Keywords.ProwessFactory"/> /
///   <see cref="SoulScarMageFactory"/> and the same instant/sorcery
///   predicate as <see cref="YoungPyromancerFactory"/>; the direct
///   functional sibling of Festival Crasher (+3/+0 vs +2/+0).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The pump trigger is
///   NOT wired (no effects service supplied). Suitable for dispatcher /
///   structural tests. Mirrors <see cref="SoulScarMageFactory.Create(Player)"/>'s
///   shape-only posture.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?)"/>
///   — fully wired. The pump trigger is built when <paramref name="effects"/>
///   is supplied; <paramref name="triggers"/> registers it with the live
///   <see cref="TriggerManager"/> so a matching <see cref="SpellCastEvent"/>
///   queues a pending trigger.
///
/// ## Deferred (v1 gaps)
/// - None at this layer — the trigger reuses the existing SpellCastEvent
///   plumbing and the pump is a standard Layer-7c end-of-turn modification.
/// </summary>
[CardName("Kiln Fiend")]
public static class KilnFiendFactory
{
    public const string CardName = "Kiln Fiend";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int PumpAmount = 3;

    /// <summary>
    /// Construct Kiln Fiend with no live effects-service / trigger-manager
    /// wiring. The pump trigger is NOT attached (no effects service).
    /// Suitable for dispatcher / structural tests. Mirrors
    /// <see cref="SoulScarMageFactory.Create(Player)"/>'s shape-only posture.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null);

    /// <summary>
    /// Construct Kiln Fiend with optional effects service + trigger manager.
    /// When <paramref name="effects"/> is supplied the cast-trigger pump is
    /// built; when <paramref name="triggers"/> is supplied that trigger is
    /// registered with the <see cref="TriggerManager"/> so a live
    /// <see cref="SpellCastEvent"/> queues a pending trigger.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">ContinuousEffectsService for the +3/+0 pump
    /// (CR 613.7c). May be null — the trigger is not wired when null.</param>
    /// <param name="triggers">TriggerManager for the cast trigger. May be
    /// null — the trigger is still attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, this
        // creature gets +3/+0 until end of turn."
        // Built only when an effects service is available (the pump registers
        // a Layer-7c +3/+0 ContinuousEffect). No effects service → shape-only
        // path, same as Soul-Scar Mage's Create(Player) overload.
        if (effects != null)
        {
            card.ActiveEffects = effects;

            // Predicate: spell controller matches AND spell has Instant or
            // Sorcery card type (CR 300.1 / 307.1). The printed oracle tests
            // the card types of the spell as cast (CR 112.1) — same shape as
            // Young Pyromancer's token trigger.
            var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
                ReferenceEquals(e.Spell.Controller, card.Controller)
                && (e.Spell.Card.HasType(CardType.Instant)
                    || e.Spell.Card.HasType(CardType.Sorcery)));

            var pump = new Effect(
                $"{CardName}: +{PumpAmount}/+0 until end of turn (cast instant or sorcery)",
                () => effects.Register(new KilnFiendPumpEffect(card)));

            var trigger = new TriggeredAbility(
                source: card,
                controller: owner,
                condition: condition,
                effects: new IEffect[] { pump });

            card.AddAbility(trigger);
            triggers?.RegisterTriggeredAbility(trigger);
        }

        return card;
    }
}

/// <summary>
/// CR 613.7c — Kiln Fiend's pump. Layer 7c +3/+0 modification on the source
/// creature, expiring at end of turn. Registered when the cast trigger
/// resolves. Mirrors <see cref="ProwessPumpEffect"/> (+1/+1) but pumps
/// power only (+3/+0).
/// </summary>
public sealed class KilnFiendPumpEffect : ContinuousEffect
{
    private readonly Creature _target;

    public KilnFiendPumpEffect(Creature target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public override Layer Layer => Layer.PT_Modify;
    public override bool ExpiresAtEndOfTurn => true;
    public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += KilnFiendFactory.PumpAmount;
        // +3/+0 — toughness unchanged.
    }
}
