using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;
using IEffect = Majik.Core.Abilities.IEffect;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bria, Riptide Rogue (Bloomburrow,
/// Legendary Creature — Otter Rogue {1}{U}{R} 2/2).
///
/// Oracle text:
///   "Prowess (Whenever you cast a noncreature spell, this creature gets
///    +1/+1 until end of turn.)
///    Other creatures you control have prowess. (If a creature has multiple
///    instances of prowess, each triggers separately.)
///    Whenever you cast a noncreature spell, target creature you control
///    can't be blocked this turn."
///
/// ## Implemented (v1)
/// - 2/2 Legendary Creature — Otter Rogue, mana cost {1}{U}{R},
///   owner/controller wired. Stats / supertype / subtypes come from the JSON
///   definition.
/// - <b>Prowess on Bria itself</b> (CR 702.108) — wired via
///   <see cref="ProwessFactory.Build"/> when a
///   <see cref="ContinuousEffectsService"/> is supplied; the prowess trigger
///   is registered with the <see cref="TriggerManager"/> when provided. Same
///   wiring shape as <see cref="MonasteryMentorFactory"/>.
/// - <b>"Other creatures you control have prowess."</b> (CR 613.1f Layer 6
///   ability-adding static) — a <see cref="GrantAbilityToGroupStaticEffect"/>
///   whose scope is the controller's OTHER creatures (Bria herself excluded —
///   "Other"). Each granted member receives its OWN prowess triggered ability
///   built per-bearer via <see cref="ProwessFactory.Build"/> (so the closure
///   binds the live creature + its ActiveEffects), registered with the live
///   <see cref="TriggerManager"/> so it actually FIRES (CR 702.108b — "each
///   triggers separately"). Membership recomputes live as creatures enter /
///   leave (CR 611.2c). This is the same primitive Sublime Archangel uses for
///   "Other creatures you control have exalted."
///   <para>The per-bearer prowess pump registers against the BEARER'S
///   <see cref="Creature.ActiveEffects"/>; the grant binds the shared
///   <see cref="ContinuousEffectsService"/> onto each member before building
///   the prowess trigger so the +1/+1 read flows through the layers compute
///   (CR 613 Layer 7c).</para>
/// - <b>"Whenever you cast a noncreature spell, target creature you control
///   can't be blocked this turn."</b> (CR 603.1) — a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> filtered
///   to the controller casting a non-Creature spell (same predicate as
///   prowess). On resolve it registers a fully-unblockable
///   <see cref="CantBeBlockedExceptByEffect"/> (predicate <c>_ => false</c> —
///   no would-be blocker can ever satisfy it, CR 509.1b) with
///   <c>expiresAtEndOfTurn: true</c> (CR 514.2 — "this turn") onto the chosen
///   creature's <see cref="Creature.ActiveEffects"/>. The target is read LIVE
///   off the controller's battlefield at resolution (resolver-null-safe — same
///   posture as <see cref="ContextOpponents"/> / Stormchaser's Talent): v1
///   deterministic pick is the first creature the controller controls. Bria's
///   own cast does NOT trigger any of these — the SpellCastEvent for Bria
///   fires while she is a Creature spell on the stack (CR 110.4), failing the
///   noncreature predicate.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Prowess / group-grant /
///   unblockable triggers need a live <see cref="ContinuousEffectsService"/> +
///   <see cref="TriggerManager"/>; without them only the card shape (stats,
///   types, the unblockable cast trigger attached for observability) exists.
///   The overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?)"/>
///   — fully wired.
///
/// ## Deferred (v1 gaps)
/// - <b>"target creature you control" picker</b>: v1 deterministically targets
///   the first creature the controller controls (read live at resolution).
///   Real agent-driven target choice is deferred behind the broader prompt
///   surface — same posture as Stormchaser's Talent's "any target".
/// </summary>
[CardName("Bria, Riptide Rogue")]
public static class BriaRiptideRogueFactory
{
    public const string CardName = "Bria, Riptide Rogue";
    public const string PrintedManaCost = "{1}{U}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("bria-riptide-rogue");

    /// <summary>
    /// Construct Bria with no live layer / trigger wiring. The unblockable
    /// cast trigger is attached for shape observability; prowess (on Bria + the
    /// group grant) is NOT wired (no continuous-effects service). Suitable for
    /// shape / dispatcher tests — the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Bria, Riptide Rogue.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service. Required for Bria's own
    /// prowess pump and the "other creatures you control have prowess" group
    /// grant. Null ⇒ shape only (no prowess wiring).</param>
    /// <param name="triggers">TriggerManager. Registers Bria's own prowess
    /// trigger, each granted creature's prowess trigger (via the group grant),
    /// and the unblockable cast trigger so they actually fire. May be null —
    /// the unblockable trigger is still attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Stats, Legendary supertype, Otter Rogue subtypes come from the JSON.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // "Whenever you cast a noncreature spell, target creature you control
        // can't be blocked this turn." (CR 603.1)
        // Attached unconditionally so the card shape carries it even on the
        // shape-only path; registered with the manager when supplied.
        // ----------------------------------------------------------------
        var unblockableTrigger = BuildUnblockableCastTrigger(card, owner);
        card.AddAbility(unblockableTrigger);
        triggers?.RegisterTriggeredAbility(unblockableTrigger);

        if (continuousEffects == null)
        {
            return card;
        }

        // CR 702.108 — Prowess on Bria itself. Binds card.ActiveEffects so
        // her P/T reads flow through the layers compute (Layer 7c).
        card.ActiveEffects = continuousEffects;
        var ownProwess = ProwessFactory.Build(card, continuousEffects);
        card.AddAbility(ownProwess);
        triggers?.RegisterTriggeredAbility(ownProwess);

        // CR 613.1f — "Other creatures you control have prowess." A Layer-6
        // ability-adding static granting a prowess triggered ability to each
        // OTHER creature the controller controls. Each granted prowess instance
        // is registered with the TriggerManager so it fires (CR 702.108b —
        // "each triggers separately"). The scope predicate excludes Bria
        // herself ("Other"). Membership recomputes live as creatures enter /
        // leave (CR 611.2c). Same primitive as Sublime Archangel's "other
        // creatures you control have exalted."
        continuousEffects.Register(new GrantAbilityToGroupStaticEffect(
            source: card,
            scope: p => !ReferenceEquals(p, card)
                && p is Creature
                && ReferenceEquals(p.Controller, card.Controller),
            abilityFactory: member =>
            {
                // CR 613 — the granted prowess pump must register against the
                // BEARER's ActiveEffects so the +1/+1 read flows through that
                // creature's layers compute. Bind the shared service before
                // building the prowess trigger (ProwessFactory closes over it).
                var bearer = (Creature)member;
                bearer.ActiveEffects ??= continuousEffects;
                return new IAbility[] { ProwessFactory.Build(bearer, continuousEffects) };
            },
            membershipProvider: () => ControllerCreatures(card),
            triggers: triggers));

        return card;
    }

    /// <summary>
    /// CR 603.1 — "Whenever you cast a noncreature spell, target creature you
    /// control can't be blocked this turn." The predicate matches
    /// <see cref="ProwessFactory"/>'s (controller casts a non-Creature spell).
    /// On resolve a fully-unblockable <see cref="CantBeBlockedExceptByEffect"/>
    /// (predicate <c>_ => false</c> — CR 509.1b, no blocker satisfies it,
    /// <c>expiresAtEndOfTurn: true</c> for "this turn") is registered onto the
    /// chosen creature's <see cref="Creature.ActiveEffects"/>. v1 deterministic
    /// target: the first creature the controller controls, read LIVE off the
    /// battlefield at resolution (resolver-null-safe).
    /// </summary>
    private static TriggeredAbility BuildUnblockableCastTrigger(Creature card, Player owner)
    {
        var effect = new Effect(
            $"{CardName}: target creature you control can't be blocked this turn",
            () =>
            {
                var controller = card.Controller ?? owner;

                // v1 deterministic "target creature you control": first creature
                // on the controller's battlefield (read live — not a captured
                // resolver, which would be null on the routed prod build).
                var target = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => c.Zone == ZoneType.Battlefield);
                if (target == null) return;
                if (target.ActiveEffects == null) return;

                // CR 509.1b — fully unblockable: no would-be blocker satisfies
                // the predicate. CR 514.2 — "this turn" expiry sweep.
                target.ActiveEffects.Register(
                    new CantBeBlockedExceptByEffect(
                        source: target,
                        predicate: _ => false,
                        expiresAtEndOfTurn: true));
            });

        return new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
                ReferenceEquals(e.Spell.Controller, card.Controller ?? owner)
                && !e.Spell.Card.HasType(CardType.Creature)),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    /// <summary>
    /// Live candidate set for the "other creatures you control have prowess"
    /// group grant: every creature on Bria's controller's battlefield. The
    /// <c>scope</c> predicate further excludes Bria herself and filters to the
    /// controller's creatures.
    /// </summary>
    private static IEnumerable<Permanent> ControllerCreatures(Creature card)
    {
        var controller = card.Controller;
        if (controller == null) return Array.Empty<Permanent>();
        return controller.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.Zone == ZoneType.Battlefield);
    }
}
