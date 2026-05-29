using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Festival Crasher (Modern Horizons 2, {1}{R}).
///
/// Creature — Devil 1/3. Oracle text:
///   "Whenever you cast an instant or sorcery spell, this creature gets
///    +2/+0 until end of turn."
///
/// ## Implementation
///
/// - <b>1/3 Creature — Devil at {1}{R}</b> (subtype already present in
///   <see cref="CardSubtype.Devil"/>).
/// - <b>Cast-instant-or-sorcery trigger (CR 603.1)</b> — fires on a
///   <see cref="SpellCastEvent"/> whose
///   <see cref="Majik.Core.Spells.ISpell.Controller"/> matches Festival
///   Crasher's controller AND whose <see cref="Majik.Core.Spells.ISpell.Card"/>
///   carries <see cref="CardType.Instant"/> or <see cref="CardType.Sorcery"/>.
///   This is the same cast-trigger predicate shape as
///   <see cref="SpriteDragonFactory"/> / <see cref="SoulScarMageFactory"/>'s
///   Prowess, but scoped to instants/sorceries (not "noncreature") and the
///   pump is +2/+0 rather than +1/+1.
/// - The effect registers a one-turn <see cref="PumpUntilEndOfTurnEffect"/>
///   on the supplied <see cref="ContinuousEffectsService"/> (CR 613.1f,
///   Layer 7c — the pump flows through the layers pipeline so Power /
///   Toughness reads recompute). The effect self-expires at end of turn
///   (CR 514.2 cleanup), mirroring Soul-Scar Mage's Prowess pump lifecycle.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The cast trigger is NOT
///   wired (no effects service). Suitable for dispatcher / structural tests.
///   Mirrors Soul-Scar Mage's shape-only posture.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?)"/>
///   — fully wired. The trigger is built and registered with the
///   <see cref="TriggerManager"/> (when supplied) so a
///   <see cref="SpellCastEvent"/> from an instant/sorcery cast by Festival
///   Crasher's controller automatically queues the pump.
/// </summary>
[CardName("Festival Crasher")]
public static class FestivalCrasherFactory
{
    public const string CardName = "Festival Crasher";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Constructs Festival Crasher with no live wiring. The cast trigger is
    /// NOT attached (no effects service supplied). Suitable for dispatcher /
    /// structural tests — mirrors <see cref="SoulScarMageFactory.Create(Player)"/>'s
    /// shape-only posture.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, triggers: null);

    /// <summary>
    /// Constructs Festival Crasher with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">ContinuousEffectsService the +2/+0 pump
    /// registers against (CR 613.1f, Layer 7c). May be null — the cast
    /// trigger is not wired when null (shape-only).</param>
    /// <param name="triggers">TriggerManager the cast trigger registers with
    /// so it fires off the event bus. May be null — the trigger is still
    /// attached to the card shape when an effects service is supplied.</param>
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
            subtypes: new[] { CardSubtype.Devil });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cast-instant-or-sorcery trigger — CR 603.1.
        //   "Whenever you cast an instant or sorcery spell, this creature
        //    gets +2/+0 until end of turn."
        // Predicate mirrors ProwessFactory's controller-match shape, but
        // gates on Instant/Sorcery rather than "noncreature". The effect
        // registers a one-turn +2/+0 pump (CR 613 Layer 7c) that self-
        // expires at cleanup (CR 514.2). Wired only when an effects service
        // is supplied — the single-arg path stays shape-only.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            card.ActiveEffects = effects;

            var pump = new Effect(
                $"{CardName}: +2/+0 until end of turn (cast instant or sorcery)",
                () => effects.Register(new PumpUntilEndOfTurnEffect(card, 2, 0)));

            var trigger = new TriggeredAbility(
                source: card,
                controller: owner,
                condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
                    ReferenceEquals(e.Spell.Controller, owner)
                    && (e.Spell.Card.HasType(CardType.Instant)
                        || e.Spell.Card.HasType(CardType.Sorcery))),
                effects: new IEffect[] { pump },
                activeZones: new[] { ZoneType.Battlefield });

            card.AddAbility(trigger);
            triggers?.RegisterTriggeredAbility(trigger);
        }

        return card;
    }
}
