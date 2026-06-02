using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thing in the Ice — transform-DFC front face
/// (Shadows over Innistrad, {1}{U}). Back face: Awoken Horror (7/8 Kraken
/// Horror).
///
/// Oracle text (verified against Scryfall):
///   Thing in the Ice — Creature — Horror 0/4:
///     "Defender
///      This creature enters with four ice counters on it.
///      Whenever you cast an instant or sorcery spell, remove an ice counter
///      from this creature. Then if it has no ice counters on it,
///      transform it."
///   Awoken Horror — Creature — Kraken Horror 7/8:
///     "When this creature transforms into Awoken Horror, return all
///      non-Horror creatures to their owners' hands."
///
/// The base shape (name, Creature, Horror subtype, {1}{U}, 0/4) is
/// materialised from the embedded JSON definition (<c>thing-in-the-ice.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Defender keyword marker, the
/// four ice counters, the <see cref="MdfcState"/>, and the cast trigger are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// these shapes (same posture as <see cref="ElectrostaticFieldFactory"/> for
/// the Defender + instant/sorcery-cast trigger, and
/// <see cref="DelverOfSecretsFactory"/> for the transform-DFC plumbing).
///
/// ## Implemented (v1)
/// - 0/4 Creature — Horror, mana cost {1}{U}, owner / controller set.
/// - <b>Defender keyword</b> (CR 702.3) — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/>
///   surfaces it for block legality (same marker pattern as Electrostatic
///   Field / Wall of Fire).
/// - <b>Enters with four ice counters</b> (CR 122.1 / CR 614.13c — the
///   "enters with" replacement). Modelled directly: the factory seeds four
///   <c>Ice</c> counters on the card's <see cref="Permanent.Counters"/> bag at
///   construction (same direct-seed posture as Aether Hub's energy counter and
///   Gemstone Mine's mining counters — the factory is the materialisation
///   point, so the replacement is applied up front).
/// - <see cref="MdfcState"/> attached with front = "Thing in the Ice", back =
///   "Awoken Horror" (CR 711). The back-face characteristics carrier (7/8
///   blue Kraken Horror) drives the Layer-0 per-face replacement while the
///   MdfcState is back-face up.
/// - <b>Instant/sorcery-cast trigger (CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> that
///   fires whenever this card's controller ("you") casts a spell whose card is
///   an Instant or Sorcery (CR 205.3). On resolution:
///     1. Remove one ice counter (CR 122.3 — never below zero).
///     2. CR 603.2c "Then if …" — if no ice counters remain, transform the
///        card (CR 701.28). Flipping the <see cref="MdfcState"/> to the back
///        face fires the on-transform ETB below.
/// - <b>Awoken Horror transform ETB (CR 603.1 / CR 701.10)</b>: when the
///   card transforms into Awoken Horror, return all non-Horror creatures on
///   any battlefield to their owners' hands. Wired as the
///   <see cref="MdfcState.OnTransformed"/> callback (the "When this creature
///   transforms into Awoken Horror, …" trigger fires on the flip). The
///   mass-bounce walks every player's battlefield (CR 800.4) and routes each
///   non-Horror creature to its owner's hand. Thing in the Ice / Awoken Horror
///   itself is a Horror, so it is never bounced by its own trigger.
///
/// ## Deferred (v1 gaps)
/// - <b>Back-face P/T / type hot-swap on the live object.</b> The transform
///   only flips the MdfcState; the Creature object remains a 0/4 Horror named
///   "Thing in the Ice" unless the controller's
///   <see cref="Majik.Core.Effects.ContinuousEffectsService"/> consumes the
///   attached <see cref="BackFaceCharacteristics"/> as the Layer-0 seed. Same
///   v1 limit as <see cref="DelverOfSecretsFactory"/>.
/// - <b>Live "all players" enumeration for the mass-bounce.</b> No
///   <c>Player.Opponents</c> / global player list is reachable from the card
///   at v1; the caller threads the live player list in via
///   <paramref name="allPlayersResolver"/> (same resolver-injection posture as
///   <see cref="ElectrostaticFieldFactory"/>). Without a resolver the bounce
///   half no-ops; the transform still flips the MdfcState.
/// - <b>Live wiring against <see cref="TriggerManager"/></b>: the single-arg
///   overload attaches the cast trigger to the card shape for structural /
///   dispatcher tests without registering it; the (owner, triggers,
///   allPlayersResolver) overload registers it so a matching
///   <see cref="SpellCastEvent"/> queues the counter-decrement effect.
/// </summary>
[CardName("Thing in the Ice // Awoken Horror")]
public static class ThingInTheIceFactory
{
    public const string FrontName = "Thing in the Ice";
    public const string BackName = "Awoken Horror";
    public const string Slug = "thing-in-the-ice";
    public const int StartingIceCounters = 4;

    /// <summary>CR 122 — the ice-counter marker. Free-form opaque counter type
    /// (CR 122.1); consumed only by this card's cast trigger.</summary>
    public static readonly CounterType IceCounter = new("Ice");

    /// <summary>
    /// Construct Thing in the Ice with no live TriggerManager / player-list
    /// wiring. Defender, the four ice counters, the MdfcState, and the cast
    /// trigger are attached to the card shape for dispatcher / structural
    /// tests; the trigger is not registered with a <see cref="TriggerManager"/>
    /// (fire it manually) and the transform bounce no-ops (no player resolver).
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Thing in the Ice with optional TriggerManager + all-players
    /// resolver. When <paramref name="triggers"/> is supplied the cast trigger
    /// is registered so a matching <see cref="SpellCastEvent"/> (an instant or
    /// sorcery cast by the controller) queues the ice-counter-decrement effect
    /// on the stack.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager for the cast trigger. May be null
    /// — the trigger is still attached to the card shape.</param>
    /// <param name="allPlayersResolver">Live enumerator of every player in the
    /// game for the transform mass-bounce. Without a resolver the bounce half
    /// no-ops; the transform still flips the MdfcState.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Horror
        // subtype, {1}{U}, 0/4). The JSON carries no abilities — Defender, the
        // ice counters, the MdfcState, and the cast trigger are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.3 — Defender keyword marker (same posture as Electrostatic
        // Field / Wall of Fire).
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // CR 711 — attach the transform-DFC face-tracker. Starts on the front
        // face (Thing in the Ice); Transform() flips IsBackFace. The back-face
        // characteristics carrier (Awoken Horror — 7/8 blue Kraken Horror)
        // drives the Layer-0 per-face replacement while back-face up.
        card.MdfcState = new MdfcState(FrontName, BackName,
            BackFaceCharacteristics.Creature(
                name: BackName,
                power: 7,
                toughness: 8,
                subtypes: new[] { CardSubtype.Kraken, CardSubtype.Horror },
                colors: new[] { Majik.Core.ValueObjects.ManaColor.Blue }));

        // CR 122.1 / CR 614.13c — "This creature enters with four ice counters
        // on it." The factory is the materialisation point, so the "enters
        // with" replacement is applied up front by seeding the counter bag
        // (same direct-seed posture as Aether Hub / Gemstone Mine).
        card.Counters.Add(IceCounter, StartingIceCounters);

        // CR 603.1 — "Whenever you cast an instant or sorcery spell, remove an
        // ice counter from this creature. Then if it has no ice counters on it,
        // transform it." Predicate: the spell's controller is "you" (this
        // card's controller) AND the spell's card is an Instant or Sorcery
        // (CR 205.3). Same on-cast predicate shape as Electrostatic Field.
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var caster = e.Spell.Controller;
            if (caster is null || !ReferenceEquals(caster, card.Controller ?? owner))
                return false;

            var spellCard = e.Spell.Card;
            return spellCard.HasType(CardType.Instant)
                || spellCard.HasType(CardType.Sorcery);
        });

        var castEffect = new Effect(
            $"{FrontName}: remove an ice counter; transform if none remain",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                if (card.MdfcState is null || card.MdfcState.IsBackFace) return;

                // CR 122.3 — remove one ice counter (CounterCollection.Remove
                // never goes below zero).
                card.Counters.Remove(IceCounter);

                // CR 603.2c — the "Then if …" intervening clause. Transform
                // only when no ice counters remain (CR 701.28). Flipping the
                // MdfcState fires the Awoken Horror transform ETB below.
                if (card.Counters.Count(IceCounter) == 0)
                {
                    card.MdfcState.Transform();
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { castEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // CR 603.1 / CR 701.10 — "When this creature transforms into Awoken
        // Horror, return all non-Horror creatures to their owners' hands."
        // Wired as the OnTransformed callback so the flip to back-face up
        // fires the mass-bounce. The MdfcState setter overwrites OnTransformed
        // to bump the continuous-effects generation; chain to that so both the
        // cache invalidation and the bounce run on a flip.
        var cesInvalidation = card.MdfcState.OnTransformed;
        card.MdfcState.OnTransformed = () =>
        {
            cesInvalidation?.Invoke();

            // CR 800.4 — "all" creatures means across every battlefield. Without
            // a resolver the player aggregate exposes no global player list at
            // v1, so the bounce half no-ops (same posture as Electrostatic
            // Field's "each opponent").
            var players = allPlayersResolver?.Invoke();
            if (players is null) return;

            // CR 201.2 — snapshot first so a bounce (a zone move) doesn't
            // disturb enumeration (mirrors the Echoing Truth pattern).
            var toBounce = players
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .OfType<Creature>()
                .Where(c => !c.HasSubtype(CardSubtype.Horror))
                .ToList();

            foreach (var creature in toBounce)
            {
                // CR 608.2b — guard against a same-step move having already
                // pulled this creature off the battlefield.
                if (creature.Zone != ZoneType.Battlefield) continue;
                ReturnToOwnersHand(creature);
            }
        };

        return card;
    }

    /// <summary>
    /// CR 701.10 — return a single creature to its owner's hand. Raw zone
    /// manipulation (same fallback posture as Echoing Truth / Boomerang when
    /// no ZoneService is threaded in).
    /// </summary>
    private static void ReturnToOwnersHand(Creature creature)
    {
        var owner = creature.Owner;
        if (owner is null) return;

        var controller = creature.Controller ?? owner;
        controller.Zones.Battlefield.RemoveCard(creature);
        owner.Zones.Hand.AddCard(creature);
        creature.SetZone(ZoneType.Hand);
        creature.SetController(owner);
    }
}
