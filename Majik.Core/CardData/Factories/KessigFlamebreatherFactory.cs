using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kessig Flamebreather (Midnight Hunt, {1}{R}).
///
/// Creature — Human Shaman 1/3. Oracle text (verified against Scryfall):
///   "Whenever you cast a noncreature spell, this creature deals 1 damage
///    to each opponent."
///
/// The base shape (name, Creature, Human/Shaman subtypes, {1}{R}, 1/3) is
/// materialised from the embedded JSON definition
/// (<c>kessig-flamebreather.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The noncreature-cast damage
/// trigger is layered on here — the JSON <c>AbilityDefinition</c> schema
/// doesn't yet express this trigger shape (same posture as
/// <see cref="ThirdPathIconoclastFactory"/>, whose noncreature-cast token
/// trigger uses the exact same predicate).
///
/// ## Implementation
///
/// - 1/3 Human Shaman, mana cost {1}{R}.
/// - <b>Noncreature-cast damage trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   fires whenever this card's controller casts a spell whose card does
///   NOT have the Creature card type (CR 205.3 / 302.1 — a "noncreature
///   spell" is any spell that isn't a creature spell, so artifact
///   creatures and other creature spells are excluded). Same noncreature
///   predicate as <see cref="ThirdPathIconoclastFactory"/> and Monastery
///   Mentor. Effect: deal 1 damage to each opponent (CR 119.2 / 119.3 /
///   119.8 — non-combat damage to a player is dealt as life loss), routed
///   through <see cref="Fx.DealDamage"/>. "Each opponent" (CR 800.4) is
///   every player other than the controller, supplied by the optional
///   <paramref name="opponentResolver"/> (same posture as
///   <see cref="CruelCelebrantFactory"/>'s drain resolver — the single-arg
///   <c>Create(owner)</c> overload no-ops the damage side).
///
/// ## Deferred (v1 gaps)
/// - None at this layer. "This creature deals 1 damage" means the damage
///   has Kessig Flamebreather as its source (CR 119.4); the engine's
///   non-combat player-damage path collapses to life loss (CR 119.8) so
///   the source attribution carries no additional gameplay surface here.
/// </summary>
[CardName("Kessig Flamebreather")]
public static class KessigFlamebreatherFactory
{
    public const string CardName = "Kessig Flamebreather";
    public const string Slug = "kessig-flamebreather";
    public const int Damage = 1;

    /// <summary>
    /// Construct Kessig Flamebreather with no live runtime services. The
    /// damage trigger is attached to the card shape for observability but
    /// not registered with a <see cref="TriggerManager"/>, and no opponent
    /// resolver is wired (so the damage side is a no-op). Suitable for
    /// dispatcher / structural tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Kessig Flamebreather with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentResolver">Supplies the player list the
    /// noncreature-cast trigger deals 1 damage to (typically every
    /// <c>Game.Players</c> entry that isn't the controller). When null the
    /// damage side is a no-op (CR 800.4 — "each opponent").</param>
    /// <param name="eventBus">Reserved for future lifecycle subscribers
    /// (e.g. LTB unregister); not used directly by this factory.</param>
    /// <param name="triggers">TriggerManager for the damage trigger. May be
    /// null — the trigger is still attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Shaman subtypes, {1}{R}, 1/3). The JSON carries no
        // abilities — the damage trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 603.1 — "Whenever you cast a noncreature spell, this creature
        // deals 1 damage to each opponent."
        // Predicate: spell controller matches AND the spell's card is NOT a
        // Creature (CR 205.3 / 302.1). Same noncreature filter as Third
        // Path Iconoclast / Monastery Mentor.
        var damageCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && !e.Spell.Card.HasType(CardType.Creature));

        var damageEffect = new Effect(
            $"{CardName}: deal {Damage} damage to each opponent (whenever you cast a noncreature spell)",
            () =>
            {
                // CR 800.4 — "each opponent" = every player other than the
                // controller. CR 119.3 / 119.8 — damage to a player reduces
                // their life total / is dealt as life loss.
                var opponents = opponentResolver?.Invoke();
                if (opponents == null) return;
                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, owner)) continue;
                    Fx.DealDamage(opp, Damage);
                }
            });

        var damageTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: damageCondition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(damageTrigger);
        triggers?.RegisterTriggeredAbility(damageTrigger);

        return card;
    }
}
