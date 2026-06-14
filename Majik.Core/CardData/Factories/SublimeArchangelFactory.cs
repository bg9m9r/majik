using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;
using IEffect = Majik.Core.Abilities.IEffect;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sublime Archangel (Avacyn Restored,
/// Creature — Angel {2}{W}{W} 4/3).
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    Exalted (Whenever a creature you control attacks alone, that creature
///    gets +1/+1 until end of turn.)
///    Other creatures you control have exalted. (If a creature has multiple
///    instances of exalted, each triggers separately.)"
///
/// ## Implemented (v1)
/// - 4/3 Creature — Angel, mana cost {2}{W}{W}, owner/controller wired.
/// - <b>Flying</b> (CR 702.9) — supplied by the JSON definition
///   (<c>"keywords": ["Flying"]</c>) and materialised as a
///   <see cref="KeywordAbility"/> marker by
///   <see cref="CardDefinitionFactory"/>; consumed by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>.
/// - <b>Exalted on the Archangel itself</b> (CR 702.91) — a
///   <see cref="KeywordAbility"/> "Exalted" marker plus the exalted
///   triggered ability, built by the same shared
///   <see cref="BuildExaltedTrigger"/> helper used for the granted copies.
/// - <b>"Other creatures you control have exalted."</b> (CR 613.1f Layer 6
///   ability-adding static) — a <see cref="GrantAbilityToGroupStaticEffect"/>
///   whose scope is the controller's OTHER creatures (the Archangel itself
///   excluded — "Other"). Each granted member receives its own exalted
///   triggered ability (built per-bearer so the closure binds the live
///   creature), registered with the live <see cref="TriggerManager"/> so it
///   actually fires (CR 702.91b — "each triggers separately"). Membership
///   recomputes live as creatures enter / leave (CR 611.2c).
///
/// ## Exalted semantics (CR 702.91b)
/// "Whenever a creature you control attacks alone, that creature gets +1/+1
/// until end of turn." The shared trigger fires on every
/// <see cref="CreatureAttacksEvent"/> whose attacker is controlled by the
/// Archangel's controller. At trigger-resolution time it reads the live
/// attackers via the injected <c>attackingCreaturesSource</c> closure; if
/// exactly one controller-side attacker exists, that attacker gets a +1/+1
/// EOT pump. Each instance of exalted triggers separately, so a creature
/// attacking alone while the controller has the Archangel + N other creatures
/// gets +(N+1)/+(N+1) — the reminder-text "each triggers separately" behaviour
/// (multiple exalted instances each register an independent trigger and each
/// pump runs).
///
/// ## Source-closure injection
/// Same deferral as <see cref="NobleHierarchFactory"/> / Goblin Piledriver:
/// the engine doesn't yet expose a global "attacking creatures" view from
/// inside the effect closure, so the factory accepts a
/// <c>Func&lt;IReadOnlyList&lt;Creature&gt;&gt;</c> that callers (Game /
/// tests) populate. When null the pump body is a no-op — suitable for
/// shape / dispatcher tests.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The Archangel's own exalted
///   marker + trigger are attached structurally; the group grant is NOT
///   registered (no continuous-effects service / trigger manager). This is
///   the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?, Func{IReadOnlyList{Creature}}?)"/>
///   — fully wired. The Archangel's own exalted trigger is registered with the
///   manager and the "other creatures you control have exalted" group grant is
///   wired to the layer + trigger systems.
///
/// ## Deferred (v1 gaps)
/// - <b>Live combat-attackers provider</b>: same gap as Noble Hierarch /
///   Goblin Piledriver. Once an <c>ICurrentCombatProvider</c> ships the
///   factory will read attackers off the live provider directly instead of the
///   injected closure.
/// - <b>Trigger-on-stack timing</b>: pump is registered immediately at
///   trigger-resolution (same collapse as Noble Hierarch). Observationally
///   equivalent for the +1/+1 read at the damage step in a single combat step.
/// </summary>
[CardName("Sublime Archangel")]
public static class SublimeArchangelFactory
{
    public const string CardName = "Sublime Archangel";
    public const string PrintedManaCost = "{2}{W}{W}";
    public const int Power = 4;
    public const int Toughness = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sublime-archangel");

    /// <summary>
    /// Construct Sublime Archangel with no live layer / trigger wiring. The
    /// Archangel's own exalted keyword marker + trigger are attached
    /// structurally (the trigger's pump body is a no-op without an attackers
    /// source); the "other creatures you control have exalted" group grant is
    /// NOT registered. Suitable for shape / dispatcher tests — the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct a fully-wired Sublime Archangel.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service the "other creatures you
    /// control have exalted" group grant registers against. Null ⇒ shape
    /// only (no grant).</param>
    /// <param name="triggers">TriggerManager. Registers the Archangel's own
    /// exalted trigger and (via the group grant) each granted creature's
    /// exalted trigger so they actually fire. May be null — the Archangel's
    /// own trigger is still attached to the card shape.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list, called at trigger resolution so the "attacks
    /// alone" check can be made. May be null — pump bodies are no-ops.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Stats, Angel subtype, and Flying come from the JSON definition.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.91 — Exalted on the Archangel itself. Keyword marker so
        // data-side tools see it, plus the exalted triggered ability.
        card.AddAbility(new KeywordAbility("Exalted", card, owner));

        var ownExalted = BuildExaltedTrigger(card, owner, attackingCreaturesSource);
        card.AddAbility(ownExalted);
        triggers?.RegisterTriggeredAbility(ownExalted);

        if (continuousEffects == null)
        {
            return card;
        }

        // CR 613.1f — "Other creatures you control have exalted." A Layer-6
        // ability-adding static granting the exalted triggered ability to each
        // OTHER creature the controller controls. The grant is registered with
        // the TriggerManager so each granted exalted instance fires (CR 702.91b
        // — "each triggers separately"). includeSelf semantics: the scope
        // predicate excludes the Archangel itself ("Other"). Membership is
        // recomputed live as creatures enter / leave (CR 611.2c).
        continuousEffects.Register(new GrantAbilityToGroupStaticEffect(
            source: card,
            scope: p => !ReferenceEquals(p, card)
                && p is Creature
                && ReferenceEquals(p.Controller, card.Controller),
            abilityFactory: member => new IAbility[]
            {
                BuildExaltedTrigger((Creature)member, card.Controller!, attackingCreaturesSource),
            },
            membershipProvider: () => ControllerCreatures(card),
            triggers: triggers));

        return card;
    }

    /// <summary>
    /// CR 702.91b — build one instance of exalted bound to
    /// <paramref name="source"/>. "Whenever a creature you control attacks
    /// alone, that creature gets +1/+1 until end of turn." The trigger fires
    /// on any <see cref="CreatureAttacksEvent"/> whose attacker is controlled
    /// by <paramref name="source"/>'s controller; at resolution the live
    /// attackers (via <paramref name="attackingCreaturesSource"/>) are read and
    /// if exactly one controller-side attacker exists it is pumped +1/+1 EOT.
    /// </summary>
    private static TriggeredAbility BuildExaltedTrigger(
        Creature source,
        Player controller,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        var exaltedEffect = new Effect(
            "Sublime Archangel Exalted: +1/+1 EOT when a creature attacks alone",
            () =>
            {
                if (attackingCreaturesSource == null) return;

                var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();

                // CR 702.91b — count only attackers controlled by this exalted
                // instance's controller; "attacks alone" means no other
                // controlled creatures are attacking.
                var controlledAttackers = new List<Creature>();
                foreach (var atk in attackers)
                {
                    if (atk == null) continue;
                    if (!ReferenceEquals(atk.Controller, source.Controller)) continue;
                    controlledAttackers.Add(atk);
                }

                if (controlledAttackers.Count != 1) return;

                var soloAttacker = controlledAttackers[0];
                if (soloAttacker.ActiveEffects == null) return;

                soloAttacker.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(soloAttacker, 1, 1));
            });

        return new TriggeredAbility(
            source: source,
            controller: controller,
            condition: new EventTriggerCondition<CreatureAttacksEvent>(
                (e, _) => ReferenceEquals(e.Attacker.Controller, source.Controller)),
            effects: new IEffect[] { exaltedEffect },
            activeZones: new[] { ZoneType.Battlefield });
    }

    /// <summary>
    /// Live candidate set for the "other creatures you control have exalted"
    /// group grant: every creature on the Archangel's controller's
    /// battlefield. The <c>scope</c> predicate further excludes the Archangel
    /// itself and filters to the controller's creatures.
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
