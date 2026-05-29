using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Electrostatic Field (Guilds of Ravnica, {1}{R}).
///
/// Creature — Wall 0/4. Oracle text (verified against Scryfall):
///   "Defender
///    Whenever you cast an instant or sorcery spell, this creature deals
///    1 damage to each opponent."
///
/// The base shape (name, Creature, Wall subtype, {1}{R}, 0/4) is
/// materialised from the embedded JSON definition
/// (<c>electrostatic-field.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Defender keyword marker
/// and the instant/sorcery-cast damage trigger are layered on here — the
/// JSON <c>AbilityDefinition</c> schema doesn't yet express this trigger
/// shape (same posture as <see cref="ThirdPathIconoclastFactory"/> and the
/// other JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
///
/// - 0/4 Creature — Wall, mana cost {1}{R}.
/// - <b>Defender keyword</b> (CR 702.3) — wired as a
///   <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/> surfaces it
///   for block legality (the card is treated as a blocker only and can't
///   attack). Same marker pattern as <see cref="WallOfFireFactory"/>.
/// - <b>Instant/sorcery-cast damage trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   fires whenever this card's controller ("you") casts a spell whose card
///   is an Instant or Sorcery (CR 205.3 / 302–307). On resolution it deals
///   1 damage to each opponent (CR 800.4 — "opponent" means every other
///   player in the game). Damage routes through <see cref="Fx.DealDamage"/>
///   so each opponent's life total drops and <c>LifeLostThisTurn</c>
///   increments (CR 119.3 / 119.8). Same on-cast trigger shape as
///   <see cref="EidolonOfTheGreatRevelFactory"/>; same "each opponent"
///   resolver-injection pattern as <see cref="VoldarenEpicureFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Live "each opponent" enumeration</b>: no <c>Player.Opponents</c>
///   accessor exists at v1; the resolver-injection pattern is shared with
///   <see cref="VoldarenEpicureFactory"/> / <see cref="SizzleFactory"/>.
///   Without a resolver the damage half silently no-ops (the trigger still
///   fires and is observable as pending). The caller threads the live
///   player list in via <paramref name="opponentResolver"/>.
/// </summary>
[CardName("Electrostatic Field")]
public static class ElectrostaticFieldFactory
{
    public const string CardName = "Electrostatic Field";
    public const string Slug = "electrostatic-field";
    public const int DamageAmount = 1;

    /// <summary>
    /// Construct Electrostatic Field with no live TriggerManager / opponent
    /// wiring. The Defender marker and the cast trigger are attached to the
    /// card shape for dispatcher / structural tests; the damage half no-ops
    /// (no opponent resolver). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, opponentResolver: null);

    /// <summary>
    /// Construct Electrostatic Field with optional TriggerManager + opponent
    /// resolver. When <paramref name="triggers"/> is supplied the cast
    /// trigger is registered so a matching <see cref="SpellCastEvent"/>
    /// (an instant or sorcery cast by the controller) queues the
    /// 1-damage-to-each-opponent effect on the stack.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager for the cast trigger. May be
    /// null — the trigger is still attached to the card shape.</param>
    /// <param name="opponentResolver">Live enumerator of "each opponent" for
    /// the damage half. Without a resolver the damage half no-ops; the
    /// trigger still fires.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Wall
        // subtype, {1}{R}, 0/4). The JSON carries no abilities — Defender and
        // the cast trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.3 — Defender keyword marker. Wired so
        // CombatAbilities.HasDefender surfaces it for block legality (same
        // marker pattern as Wall of Fire).
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, this
        // creature deals 1 damage to each opponent."
        // Predicate: the spell's controller is "you" (this card's controller)
        // AND the spell's card is an Instant or Sorcery (CR 205.3).
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var caster = e.Spell.Controller;
            if (caster is null || !ReferenceEquals(caster, card.Controller ?? owner))
                return false;

            var spellCard = e.Spell.Card;
            return spellCard.HasType(CardType.Instant)
                || spellCard.HasType(CardType.Sorcery);
        });

        var damageEffect = new Effect(
            $"{CardName}: deal {DamageAmount} damage to each opponent (whenever you cast an instant or sorcery spell)",
            () =>
            {
                // CR 800.4 — iterate every opponent and deal 1 damage.
                // CR 119.3 — damage to a player reduces their life total;
                // Fx.DealDamage routes Player → Player.LoseLife (CR 119.8).
                // Without a resolver the player aggregate exposes no opponents
                // list at v1, so the damage half no-ops (same posture as
                // Voldaren Epicure / Sizzle).
                var opponents = opponentResolver?.Invoke();
                if (opponents is null) return;

                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, card.Controller ?? owner)) continue;
                    Fx.DealDamage(opp, DamageAmount);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
