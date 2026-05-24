using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Monastery Mentor (Fate Reforged, {2}{W}).
///
/// Creature — Human Monk 2/2. Oracle text:
///   "Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    Whenever you cast a noncreature spell, create a 1/1 white Monk
///    creature token with prowess."
///
/// ## Implementation
///
/// - 2/2 Human Monk, mana cost {2}{W}.
/// - <b>Prowess (CR 702.108)</b>: wired via <see cref="ProwessFactory.Build"/>
///   when a <see cref="ContinuousEffectsService"/> is supplied. Same event
///   filter (SpellCastEvent, controller, noncreature) as the token trigger.
///   The keyword marker is NOT added separately — ProwessFactory surfaces
///   the trigger as a <see cref="TriggeredAbility"/> with description
///   "prowess +1/+1 until end of turn".
/// - <b>Token trigger (CR 603.1)</b>: a separate
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> fires
///   whenever the controller casts a noncreature spell. Effect: create a
///   1/1 Monk creature token via <see cref="TokenFactory.CreateOnBattlefield"/>.
///   Monk tokens are "with prowess" — prowess on the token is deferred (same
///   gap as Goblin Rabblemaster's token keyword wiring).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Token trigger is attached
///   for shape observability; Prowess trigger is NOT wired (no effects service).
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?, ZoneService?)"/>
///   — fully wired. Prowess trigger registered when <paramref name="effects"/>
///   is supplied; token trigger registered when <paramref name="triggers"/>
///   is supplied.
///
/// ## Deferred (v1 gaps)
/// - Prowess on Monk tokens: TokenFactory Keywords list could carry "Prowess"
///   but the ProwessFactory.Build() requires a Creature reference + a live
///   ContinuousEffectsService, which isn't available at token-creation time
///   without a wider TokenFactory refactor. Deferred to the broader token-
///   keyword-wiring pass.
/// </summary>
[CardName("Monastery Mentor")]
public static class MonasteryMentorFactory
{
    public const string CardName = "Monastery Mentor";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Monastery Mentor with no live wiring. The token trigger is
    /// attached to the card for shape observability; Prowess is not wired
    /// (no effects service supplied). Suitable for dispatcher / structural
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, effects: null);

    /// <summary>
    /// Construct Monastery Mentor with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Not used directly by this factory; reserved
    /// for future lifecycle subscribers (e.g. LTB unregister).</param>
    /// <param name="triggers">TriggerManager for the Prowess trigger (via
    /// <see cref="ProwessFactory"/>) and the token trigger. May be null —
    /// triggers are still attached to the card shape.</param>
    /// <param name="effects">ContinuousEffectsService for the Prowess pump
    /// effect (CR 613.1f, Layer 7c). May be null — Prowess trigger is not
    /// wired when null.</param>
    /// <param name="zoneService">Optional zone service so token-ETB
    /// CardMovedEvent fires. Pass null for raw zone moves.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? effects,
        ZoneService? zoneService = null)
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
        // is supplied; the prowess ability is registered with the trigger
        // manager when provided. When effects == null the prowess trigger
        // is not wired (shape-only path keeps the card shape lean).
        //
        // card.ActiveEffects is set to the effects service so that
        // card.Power / card.Toughness reads flow through the layers
        // compute (CR 613 — Layer 7c applies PumpUntilEndOfTurnEffect).
        // ----------------------------------------------------------------
        if (effects != null)
        {
            card.ActiveEffects = effects;
            var prowessTrigger = ProwessFactory.Build(card, effects);
            card.AddAbility(prowessTrigger);
            triggers?.RegisterTriggeredAbility(prowessTrigger);
        }

        // ----------------------------------------------------------------
        // Token trigger — "Whenever you cast a noncreature spell, create a
        // 1/1 white Monk creature token with prowess." (CR 603.1)
        // Same predicate as Prowess: spell controller matches AND the spell
        // is not a Creature. The two triggers are independent — both fire
        // on the same event, each resolves separately per CR 603.3b.
        // ----------------------------------------------------------------
        var tokenCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && !e.Spell.Card.HasType(CardType.Creature));

        var tokenEffect = new Effect(
            $"{CardName}: create 1/1 Monk token (whenever you cast a noncreature spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateMonkToken(controller, zoneService);
            });

        var tokenTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: tokenCondition,
            effects: new IEffect[] { tokenEffect });

        card.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.6 — create one 1/1 Monk creature token under
    /// <paramref name="controller"/>'s control.
    /// "With prowess" on the token is deferred — see factory xmldoc.
    /// </summary>
    public static Creature CreateMonkToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Monk",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Monk },
            Keywords: null,
            // CR 105 / CR 111.4 — printed "1/1 white Monk creature token
            // with prowess". The Prowess keyword on the token is deferred
            // (see class xmldoc) but the colour identity is now stamped.
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
