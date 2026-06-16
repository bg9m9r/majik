using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sedgemoor Witch (Strixhaven: School of Mages,
/// {2}{B}).
///
/// Creature — Human Warlock 3/2. Oracle text (Scryfall, verified):
///   "Menace
///    Ward—Pay 3 life. (Whenever this creature becomes the target of a spell
///    or ability an opponent controls, counter it unless that player pays
///    3 life.)
///    Magecraft — Whenever you cast or copy an instant or sorcery spell,
///    create a 1/1 black and green Pest creature token with 'When this token
///    dies, you gain 1 life.'"
///
/// ## Implementation
///
/// The base shape (name, Creature, Human + Warlock subtypes, {2}{B}, 3/2) is
/// materialised from the embedded JSON definition (<c>sedgemoor-witch.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// are layered on in C# — the JSON <c>AbilityDefinition</c> schema doesn't
/// express keyword markers, Ward, or the magecraft token trigger (same posture
/// as <see cref="EmberheartChallengerFactory"/> / <see cref="SireOfSevenDeathsFactory"/>).
///
/// - <b>Menace (CR 702.111)</b>: a <see cref="KeywordAbility"/>("Menace")
///   marker consumed by CombatValidator (can't be blocked except by two or
///   more creatures).
/// - <b>Ward—Pay 3 life (CR 702.21c)</b>: a <see cref="KeywordAbility"/>("Ward")
///   marker for the uniform discovery surface plus a bound
///   <see cref="WardEffect"/> via <see cref="BuildWardEffect"/> whose payment
///   is a real <see cref="PayLifeCost"/>(3). <see cref="WardEffect.Resolve"/>
///   counters an opponent's targeting spell/ability unless they pay 3 life
///   (same posture as <see cref="SireOfSevenDeathsFactory"/>).
/// - <b>Magecraft — cast half (CR 603.1)</b>: a <see cref="TriggeredAbility"/>
///   over <see cref="SpellCastEvent"/> matching when the spell's controller is
///   Sedgemoor Witch's controller AND the spell is an Instant or Sorcery
///   (CR 300.1 / 307.1). On resolve, create one 1/1 black-and-green Pest
///   creature token (with its own dies-trigger). Same shape as
///   <see cref="YoungPyromancerFactory"/>'s on-cast token trigger.
///
/// ## Deferred (v1 gap)
/// - <b>Magecraft — "or copy" half.</b> Magecraft also triggers when the
///   controller COPIES an instant or sorcery spell (CR 707.10 / 702.151). The
///   engine publishes no spell-copy domain event yet (there is no
///   <c>SpellCopiedEvent</c>), so only the "cast" half is wired — identical to
///   every other magecraft / "whenever you cast or copy" card. When a
///   spell-copy event lands this trigger's predicate can fold it in with no
///   shape change.
/// </summary>
[CardName("Sedgemoor Witch")]
public static class SedgemoorWitchFactory
{
    public const string CardName = "Sedgemoor Witch";
    public const string Slug = "sedgemoor-witch";
    public const string PrintedManaCost = "{2}{B}";
    public const int Power = 3;
    public const int Toughness = 2;

    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>Printed Ward cost — non-mana (Pay 3 life), CR 702.21c.</summary>
    public const string WardLifeCost = "Pay 3 life";

    /// <summary>The amount of life an opponent pays for Sedgemoor Witch's Ward.</summary>
    public const int WardLifeAmount = 3;

    /// <summary>The life the controller gains when a Pest token dies.</summary>
    public const int PestDeathLifeGain = 1;

    /// <summary>
    /// CR 702.21 — Sedgemoor Witch's printed "Ward—Pay 3 life" effect, bound
    /// to the supplied <paramref name="card"/>. The ward cost is the non-mana
    /// "Pay 3 life" rider, modelled via <see cref="PayLifeCost"/>; the mana
    /// portion is <see cref="ManaCost.Zero"/>. <see cref="WardEffect.Resolve"/>
    /// charges the 3-life payment when an opponent's spell/ability targets the
    /// Witch (same posture as <see cref="SireOfSevenDeathsFactory.BuildWardEffect"/>).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, new PayLifeCost(WardLifeAmount));

    /// <summary>
    /// Construct Sedgemoor Witch with no live bus / trigger-manager wiring.
    /// The magecraft token trigger is attached to the card for shape
    /// observability; keyword markers (Menace, Ward) are attached. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Sedgemoor Witch with optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied the magecraft cast-half
    /// trigger is registered so the bus surfaces it as pending on a matching
    /// <see cref="SpellCastEvent"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(def, owner);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.111 — Menace. CR 702.21 — Ward—Pay 3 life. Menace is a
        // keyword marker consumed by CombatValidator; Ward gets the marker PLUS
        // the real battlefield-attached triggered ability ("Whenever this
        // creature becomes the target of a spell or ability an opponent
        // controls, counter it unless its controller pays 3 life") wired off
        // the shared WardTriggerWiring helper from the bound WardEffect
        // (PayLifeCost(3), a non-mana ward — CR 702.21c).
        card.AddAbility(new KeywordAbility("Menace", card, owner));
        card.AddAbility(new KeywordAbility("Ward", card, owner));
        Majik.Core.Keywords.WardTriggerWiring.Attach(
            BuildWardEffect(card), owner, triggers: triggers);

        // CR 603.1 — Magecraft (cast half): "Whenever you cast … an instant or
        // sorcery spell, create a 1/1 black and green Pest creature token with
        // 'When this token dies, you gain 1 life.'" Predicate: spell controller
        // matches AND the spell has Instant or Sorcery card type (CR 300.1 /
        // 307.1). The "or copy" half has no engine event yet (see class xmldoc).
        var tokenCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, owner)
            && (e.Spell.Card.HasType(CardType.Instant)
                || e.Spell.Card.HasType(CardType.Sorcery)));

        var tokenEffect = new Effect(
            $"{CardName}: create a 1/1 black-and-green Pest token (magecraft — whenever you cast an instant or sorcery spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreatePestToken(controller, triggers, zoneService);
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
    /// CR 111 / 111.4 — create one 1/1 black-and-green Pest creature token
    /// under <paramref name="controller"/>'s control, carrying the printed
    /// "When this token dies, you gain 1 life." delayed-free dies trigger
    /// (CR 603.6c / 700.4). When <paramref name="triggers"/> is supplied the
    /// dies trigger is registered so a Battlefield → Graveyard move places it
    /// on the stack automatically.
    /// </summary>
    public static Creature CreatePestToken(
        Player controller,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Pest",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Pest },
            Keywords: null,
            // CR 105 / 111.4 — printed "1/1 black and green Pest creature token".
            Colors: new[] { ManaColor.Black, ManaColor.Green });

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zoneService);

        // CR 603.6c / 700.4 — "When this token dies, you gain 1 life."
        // "Dies" = Battlefield → Graveyard (CR 700.4). Active zones include
        // Graveyard so the trigger is still observable after ZoneService stamps
        // card.Zone = Graveyard before publishing the CardMovedEvent (same
        // posture as Doomed Traveler / Aven Fisher).
        var diesEffect = new Effect(
            "Pest dies: you gain 1 life",
            () =>
            {
                var owner = token.Controller ?? controller;
                Fx.GainLife(owner, PestDeathLifeGain);
            });

        var diesTrigger = new TriggeredAbility(
            source: token,
            controller: controller,
            condition: Triggers.OnDies(token),
            effects: new IEffect[] { diesEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        token.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return token;
    }
}
