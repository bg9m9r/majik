using Majik.Core.Abilities;
using Majik.Core.CardData.Adventures;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bonecrusher Giant // Stomp (Throne of Eldraine,
/// {2}{R}).
///
/// ## Card text
/// - Bonecrusher Giant — Creature — Giant {2}{R}, 4/3.
///     "Whenever Bonecrusher Giant becomes the target of a spell,
///      Bonecrusher Giant deals 2 damage to that spell's controller."
/// - Stomp (Adventure) — Instant — Adventure {1}{R}.
///     "Damage can't be prevented this turn. Stomp deals 2 damage to any
///      target."
///
/// ## Implemented (v1)
/// - 4/3 Giant creature with mana cost {2}{R}.
/// - Targeted-by-spell trigger (CR 603.6c, 115.6) wired via
///   <see cref="TargetsChosenEvent"/>. Predicate fires when a spell on the
///   stack picks this Bonecrusher Giant as one of its chosen targets and
///   the spell is controlled by someone other than nobody (deals 2 damage
///   to that spell's controller — same player may target themselves and
///   take the damage).
/// - On resolution: publishes a <see cref="DamageDealtEvent"/>
///   (DamageType.Ability — the damage is dealt by the triggered ability,
///   not by the spell on the stack, per CR 119.2c) and calls
///   <see cref="Player.LoseLife"/> on the spell's controller. We use
///   <c>LoseLife</c> because the engine has no central
///   "deal damage to a player" routine outside combat; spell/ability
///   damage to a player is life loss for SBA + frontend purposes (CR
///   120.3 — damage dealt to a player causes that player to lose that
///   much life).
/// - <b>Adventure cast pipeline (CR 715)</b>: the Stomp half is attached
///   as an <see cref="AdventureSpec"/> on the card. The cast flow
///   (<see cref="Costs.AdventureAlternativeCost"/> + <see cref="SpellCastFlow"/>)
///   routes Stomp through the standard Rule 601 sequence with the
///   Adventure mana cost, exiles the card on resolve (CR 715.3d), and
///   grants the owner a runtime "may cast from exile" permission for the
///   printed Bonecrusher Giant cost via <see cref="Card.GrantRuntimeExileCast"/>
///   — same probe surface Ragavan / Cascade use, so casting the creature
///   side from Adventure-exile reuses the existing
///   <see cref="Costs.ExileCastAlternativeCost"/> path.
///
/// ## Deferred (v1 gaps)
/// - <b>"Damage can't be prevented this turn"</b> global flag from
///   Stomp's first sentence — prevention infra is not modelled yet; the
///   2-damage payload still resolves unconditionally because no
///   prevention shields exist for non-combat damage in v1.
/// </summary>
[CardName("Bonecrusher Giant")]
public static class BonecrusherGiantFactory
{
    public const string CardName = "Bonecrusher Giant";
    public const string PrintedManaCost = "{2}{R}";

    public const string AdventureName = "Stomp";
    public const string AdventureManaCost = "{1}{R}";
    public const int StompDamage = 2;

    /// <summary>
    /// Construct Bonecrusher Giant with no live event-bus / trigger-manager
    /// wiring. The targeted-by-spell trigger is attached to the card so
    /// structural / dispatch tests see the ability shape, but it is not
    /// registered with a <see cref="TriggerManager"/>; tests fire it
    /// manually via <see cref="TriggeredAbility.IsTriggered"/> or by
    /// executing the effect directly.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Bonecrusher Giant with optional event bus + trigger
    /// manager. When <paramref name="triggers"/> is supplied, the
    /// targeted-by-spell trigger is registered so a
    /// <see cref="TargetsChosenEvent"/> matching this Bonecrusher Giant
    /// automatically surfaces as pending.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 4,
            toughness: 3,
            subtypes: new[] { CardSubtype.Giant });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Targeted-by-spell trigger — CR 603.6c, 115.6.
        //   "Whenever Bonecrusher Giant becomes the target of a spell,
        //    Bonecrusher Giant deals 2 damage to that spell's controller."
        // ----------------------------------------------------------------

        Player? capturedSpellController = null;

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            if (e.StackObject is not Majik.Core.Spells.ISpell spell)
            {
                return false;
            }

            var matched = e.Targets.Any(t =>
                (t.TargetType == TargetType.Permanent || t.TargetType == TargetType.Card)
                && t is Target concrete
                && ReferenceEquals(concrete.TargetObject, card));

            if (!matched)
            {
                return false;
            }

            capturedSpellController = spell.Controller;
            return true;
        });

        var pingEffect = new Effect(
            "Bonecrusher Giant: deal 2 damage to that spell's controller",
            () =>
            {
                var target = capturedSpellController;
                if (target == null)
                {
                    return;
                }

                eventBus?.Publish(new DamageDealtEvent(
                    sourceCard: card,
                    sourcePlayer: null,
                    targetCard: null,
                    targetPlayer: target,
                    amount: 2,
                    damageType: DamageType.Ability));

                target.LoseLife(2);

                capturedSpellController = null;
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { pingEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        triggers?.RegisterTriggeredAbility(trigger);

        // CR 715 — attach the Stomp Adventure half. Detached from the cast
        // pipeline plumbing — the AdventureSpec only carries the
        // alternative characteristics + an effects-factory closure; cast
        // path is driven by AdventureAlternativeCost + SpellCastFlow.
        card.AdventureSpec = new AdventureSpec(
            Name: AdventureName,
            ManaCost: ManaCost.Parse(AdventureManaCost),
            AdventureType: CardType.Instant,
            BuildDefinition: BuildAdventureSpell);

        return card;
    }

    /// <summary>
    /// Build the standalone Stomp <see cref="SpellDefinition"/> — a single
    /// 1..1 "any target" target request whose resolve effect deals 2
    /// damage to the chosen target (creature, planeswalker, or player).
    /// The caller resolves the chosen target through
    /// <paramref name="targetResolver"/>.
    /// </summary>
    /// <param name="caster">The controller of Stomp — unused at resolve
    /// time but kept for API symmetry with the other Adventure factories
    /// (Swift End needs it for "you lose 2 life").</param>
    /// <param name="targetResolver">Resolves the raw target token to a
    /// live engine object (typically a <see cref="Permanent"/> or
    /// <see cref="Player"/>).</param>
    public static SpellDefinition BuildAdventureSpell(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal | BotIntent.Burn,
                    // Live gatherer (agent-prompt MVP). Stomp's "any target"
                    // covers players + creatures + planeswalkers (CR 115.4).
                    CandidateGatherer: ctx =>
                    {
                        var pool = new List<object>();
                        foreach (var p in ctx.AllPlayers)
                        {
                            pool.Add(p);
                            foreach (var c in p.Zones.Battlefield.GetCards())
                            {
                                if (c.HasType(CardType.Creature)
                                    || c.HasType(CardType.Planeswalker))
                                {
                                    pool.Add(c);
                                }
                            }
                        }
                        return pool;
                    }),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Stomp: deal 2 damage to any target", () =>
                    {
                        // CR 119.2 — damage from an instant. Damage to a
                        // creature → TakeDamage (lethal SBA picks up).
                        // Damage to a player → LoseLife (Bonecrusher's own
                        // pattern — engine has no separate "deal damage
                        // to a player" outside combat in v1).
                        switch (resolved)
                        {
                            case Creature creature:
                                creature.TakeDamage(StompDamage);
                                break;
                            case Player player:
                                player.LoseLife(StompDamage);
                                break;
                            case Planeswalker pw:
                                // CR 120.3c — non-combat damage to a
                                // planeswalker is removal of that many
                                // loyalty counters.
                                pw.RemoveLoyalty(StompDamage);
                                break;
                        }
                    }),
                };
            });
    }
}
