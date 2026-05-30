using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kor Spiritdancer (Rise of the Eldrazi / reprints,
/// {1}{W}). Creature — Kor Wizard 0/2. Oracle text (verified against
/// Scryfall):
///   "This creature gets +2/+2 for each Aura attached to it.
///    Whenever you cast an Aura spell, you may draw a card."
///
/// The card's base shape (name, Creature, Kor/Wizard subtypes, {1}{W}, 0/2)
/// is materialised from the embedded JSON definition
/// (<c>kor-spiritdancer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (Aura-count self-pump, Aura-cast draw trigger) are layered on top here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express a dynamic-count
/// P/T static or a spell-cast trigger, so they live in the factory (same
/// posture as <see cref="BladeSplicerFactory"/> and the other JSON-backed
/// cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
///
/// ### Part 1 — Aura-count self-pump (CR 613.1g / CR 303.4)
///
/// "This creature gets +2/+2 for each Aura attached to it." A
/// <b>Layer 7c static modification</b> (printed base P/T 0/2 stands; the
/// bonus stacks on top as a modifier). Implemented via
/// <see cref="AuraCountPumpEffect"/>, a <see cref="ContinuousEffect"/> that
/// re-counts the Auras currently attached to Kor Spiritdancer on every
/// <see cref="ContinuousEffectsService.Compute"/> invocation. The count
/// reads <see cref="Permanent.Attachments"/> filtered to the Aura subtype
/// (CR 205.3g) — so attaching / removing an Aura transfers the bonus
/// automatically, exactly like <see cref="TerritorialKavuFactory"/>'s Domain
/// pump re-counts basic land types. N = (auras attached) and the bonus is
/// +2N/+2N.
///
/// Lifecycle: registered only when a <see cref="ContinuousEffectsService"/>
/// is supplied; <see cref="AuraCountPumpEffect.IsActive"/> short-circuits
/// off the battlefield so the bonus lifts when Kor Spiritdancer leaves
/// (same posture as <see cref="BladeSplicerFactory"/>'s lord static — the
/// active gate handles zone changes; a future Prune pass could drop the
/// entry).
///
/// ### Part 2 — Aura-cast draw trigger (CR 603.1)
///
/// "Whenever you cast an Aura spell, you may draw a card." A spell-cast
/// trigger over <see cref="SpellCastEvent"/> (same shape as
/// <see cref="SramSeniorEdificerFactory"/>): predicate is
/// <c>spell.Controller == this card's controller</c> (the printed "you
/// cast") AND the spell's card carries the Aura subtype (CR 205.3g). On
/// resolution draws one card under the controller via
/// <see cref="Majik.Core.Primitives.Fx.DrawCards"/> (routes through the
/// controller's replacement bus, CR 614).
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt on the draw trigger</b>: the printed text is
///   "you <i>may</i> draw a card"; v1 always takes the draw when the trigger
///   resolves (same posture as <see cref="TerritorialKavuFactory"/>'s "you
///   may discard"/loot — an explicit yes/no prompt is deferred). The draw
///   is harmless to take unconditionally (no library-empty downside that the
///   controller would rationally decline; empty library is handled by
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/>'s loss-condition path,
///   CR 704.5b / 120.3).
/// </summary>
[CardName("Kor Spiritdancer")]
public static class KorSpiritdancerFactory
{
    public const string CardName = "Kor Spiritdancer";
    public const string Slug = "kor-spiritdancer";

    /// <summary>
    /// Construct Kor Spiritdancer with no live wiring. The cast-trigger is
    /// attached for shape observability (not registered with any
    /// <see cref="TriggerManager"/>); the Aura-count pump is NOT registered
    /// (no continuous-effects service). Suitable for shape / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, continuousEffects: null);

    /// <summary>
    /// Construct Kor Spiritdancer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for parity with sibling factories; the
    /// draw trigger is wired through <paramref name="triggers"/> rather than a
    /// raw bus subscription.</param>
    /// <param name="triggers">When supplied, the Aura-cast draw trigger
    /// registers so a matching <see cref="SpellCastEvent"/> lands the ability
    /// on the stack automatically (CR 603.2).</param>
    /// <param name="continuousEffects">Layers service to register the
    /// Aura-count self-pump against. May be null — no live bonus.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Kor/Wizard subtypes, {1}{W}, 0/2). The JSON carries no abilities —
        // the Aura-count pump + Aura-cast draw trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Part 1 — Aura-count self-pump. Layer 7c static effect.
        //   "This creature gets +2/+2 for each Aura attached to it."
        //   CR 613.1g / CR 303.4.
        // Re-counts the Auras attached to Kor Spiritdancer on every Compute
        // call (live-count, same shape as TerritorialKavuFactory's Domain
        // pump but counting attached Auras rather than basic land types).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new AuraCountPumpEffect(card));
        }

        // ----------------------------------------------------------------
        // Part 2 — Aura-cast draw trigger. CR 603.1.
        //   "Whenever you cast an Aura spell, you may draw a card."
        // "You cast" → the spell's controller is this card's controller.
        // Aura subtype gate (CR 205.3g). v1 always takes the draw on resolve
        // (the printed "you may" prompt is deferred). Mirrors
        // SramSeniorEdificerFactory's spell-cast trigger.
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 603.1 — controller match for the printed "you cast".
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
            {
                return false;
            }

            return e.Spell.Card.HasSubtype(CardSubtype.Aura);
        });

        var drawEffect = new Effect(
            $"{CardName}: draw a card (whenever you cast an Aura spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Majik.Core.Primitives.Fx.DrawCards(controller, 1);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }

    /// <summary>
    /// CR 205.3g — the number of Auras currently attached to
    /// <paramref name="creature"/>. Reads <see cref="Permanent.Attachments"/>
    /// (the mirror of <see cref="Permanent.AttachedTo"/>) and filters to the
    /// Aura subtype. Static helper so tests can assert the count directly.
    /// </summary>
    public static int CountAttachedAuras(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        var n = 0;
        foreach (var attachment in creature.Attachments)
        {
            if (attachment.HasSubtype(CardSubtype.Aura))
            {
                n++;
            }
        }
        return n;
    }

    // -----------------------------------------------------------------------
    // AuraCountPumpEffect — Layer 7c live-count self-pump.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Layer 7c continuous effect for Kor Spiritdancer's Aura-count pump.
    /// On every <see cref="ContinuousEffectsService.Compute"/> invocation
    /// this counts the Auras attached to Kor Spiritdancer and applies
    /// +2N/+2N to its characteristics (CR 613.1g — printed base P/T 0/2
    /// stands, the bonus is a modifier). Active only while Kor Spiritdancer
    /// is on the battlefield.
    /// </summary>
    public sealed class AuraCountPumpEffect : ContinuousEffect
    {
        private const int PerAura = 2;
        private readonly Creature _source;

        public AuraCountPumpEffect(Creature source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>CR 613.1g — the permanent generating this effect.</summary>
        public override Permanent? Source => _source;

        /// <inheritdoc/>
        public override Layer Layer => Layer.PT_Modify;

        /// <summary>Active while Kor Spiritdancer is on the battlefield.</summary>
        public override bool IsActive() => _source.Zone == ZoneType.Battlefield;

        /// <summary>Applies only to Kor Spiritdancer itself.</summary>
        public override bool AppliesTo(Creature creature) =>
            ReferenceEquals(creature, _source);

        /// <summary>
        /// Apply +2N/+2N where N = Auras attached to Kor Spiritdancer.
        /// CR 205.3g / 303.4 — count delegated to
        /// <see cref="CountAttachedAuras"/>.
        /// </summary>
        public override void Apply(CreatureCharacteristics chars)
        {
            var bonus = PerAura * CountAttachedAuras(_source);
            chars.Power += bonus;
            chars.Toughness += bonus;
        }
    }
}
