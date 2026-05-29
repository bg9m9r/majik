using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Guttersnipe (Return to Ravnica, {2}{R}).
///
/// Creature — Goblin Shaman 2/2. Oracle text (verified against Scryfall):
///   "Whenever you cast an instant or sorcery spell, this creature deals
///    2 damage to each opponent."
///
/// The base shape (name, Creature, Goblin/Shaman subtypes, {2}{R}, 2/2) is
/// materialised from the embedded JSON definition (<c>guttersnipe.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The instant/sorcery-cast
/// damage trigger is layered on here — the JSON <c>AbilityDefinition</c>
/// schema doesn't yet express this trigger shape (same posture as
/// <see cref="ThirdPathIconoclastFactory"/>'s noncreature-cast token
/// trigger).
///
/// ## Implementation
///
/// - 2/2 Goblin Shaman, mana cost {2}{R}.
/// - <b>Instant/sorcery-cast damage trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   fires whenever this card's controller casts a spell whose card has the
///   Instant OR Sorcery card type (CR 205.3 / 302.1 / 304.1 — same
///   instant/sorcery filter used by Mausoleum Wanderer / Delver of Secrets).
///   Effect: this creature deals <see cref="Damage"/> (2) to each opponent.
/// - <b>Each-opponent damage (CR 119 / CR 102.4)</b>: the opponent list is
///   supplied via the optional <paramref name="opponentResolver"/> (mirrors
///   <see cref="ZulaportCutthroatFactory"/>'s resolver convention —
///   single-arg <c>Create(owner)</c> wires no resolver, so the damage side
///   is a no-op in shape/dispatcher tests). Damage to a player routes
///   through <see cref="Fx.DealDamage"/> → <see cref="Player.LoseLife"/>
///   (CR 119.3 / 119.8). In a 2-player game this is 1 opponent; in
///   multiplayer it scales (CR 102.4 — "opponent" = every other player).
///
/// ## Deferred (v1 gaps)
/// - None at this layer. Damage source attribution (the creature is the
///   source, CR 119.7) is not modelled distinctly for player damage —
///   player damage resolves to life loss regardless of source, so this is
///   not observable here. Same posture as <see cref="SizzleFactory"/>.
/// </summary>
[CardName("Guttersnipe")]
public static class GuttersnipeFactory
{
    public const string CardName = "Guttersnipe";
    public const string Slug = "guttersnipe";
    public const int Damage = 2;

    /// <summary>
    /// Construct Guttersnipe with no live runtime services. The damage
    /// trigger is attached to the card shape but not registered with a
    /// <see cref="TriggerManager"/>, and no opponent resolver is wired
    /// (so the damage side is a no-op). Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Guttersnipe with optional runtime services.
    /// <paramref name="opponentResolver"/> supplies the player list the
    /// damage trigger hits (typically every <c>Game.Players</c> entry that
    /// isn't the controller). <paramref name="triggers"/> registers the
    /// triggered ability so the bus drives it automatically.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentResolver">Resolves the opponent list at
    /// resolution time. May be null — the damage side then no-ops.</param>
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
        // Goblin/Shaman subtypes, {2}{R}, 2/2). The JSON carries no
        // abilities — the damage trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, this
        // creature deals 2 damage to each opponent."
        // Predicate: spell controller matches AND the spell's card is an
        // Instant OR Sorcery (CR 304.1 / 307.1). Same instant/sorcery
        // filter as Mausoleum Wanderer / Delver of Secrets.
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && (e.Spell.Card.HasType(CardType.Instant)
                || e.Spell.Card.HasType(CardType.Sorcery)));

        var damageEffect = new Effect(
            $"{CardName}: deal {Damage} damage to each opponent (whenever you cast an instant or sorcery spell)",
            () =>
            {
                // CR 102.4 — "each opponent" = every other player.
                // CR 119.3 / 119.8 — damage to a player reduces life total
                // via Fx.DealDamage → Player.LoseLife. The resolver
                // typically supplies every Game.Players entry; we still
                // skip the controller defensively.
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
            condition: condition,
            effects: new IEffect[] { damageEffect });

        card.AddAbility(damageTrigger);
        triggers?.RegisterTriggeredAbility(damageTrigger);

        return card;
    }
}
