using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boromir, Warden of the Tower
/// (The Lord of the Rings: Tales of Middle-earth, {2}{W}).
///
/// Legendary Creature — Human Soldier 2/3. Oracle text:
///   "Vigilance"
///   "Whenever an opponent casts a spell, if no mana was spent to cast it,
///    counter that spell."
///   "Sacrifice Boromir: Creatures you control gain indestructible until end
///    of turn. The Ring tempts you."
///
/// ## Implementation
///
/// - 2/3 <see cref="Creature"/> — <see cref="CardSupertype.Legendary"/>,
///   <see cref="CardSubtype.Human"/> + <see cref="CardSubtype.Soldier"/>,
///   mana cost {2}{W} (mana value 3, white — CR 202.3 / CR 105.1).
/// - <b>Vigilance (CR 702.20)</b> — <see cref="KeywordAbility"/> marker
///   (same shape as <see cref="StandingTroopsFactory"/>); the combat
///   subsystem reads it to skip tapping on attack.
/// - <b>Free-spell counter (CR 603.1 / CR 118)</b> — a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> gated
///   on (a) the caster being an opponent and (b)
///   <see cref="Majik.Core.Spells.Spell.WasFreeCast"/> (the "no mana was
///   spent to cast it" sentinel stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> when the collapsed total
///   cost is zero). Resolution counters the spell via
///   <see cref="Majik.Core.Primitives.Fx.Counter"/>. Mirrors
///   <see cref="RoilingVortexFactory"/>'s free-cast trigger but counters
///   instead of pinging, and finally wires Vexing Bauble's deferred
///   "counter free spells" behaviour.
/// - <b>Sacrifice ability (CR 602 / CR 701.16 / CR 701.54)</b> —
///   <see cref="ActivatedAbility"/> whose only cost is
///   <see cref="SacrificeSelfCost"/>. On resolution it grants every creature
///   the controller controls Indestructible until end of turn via
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> (reuses
///   <see cref="BorosCharmFactory"/> mode-1's pattern) and then tempts the
///   controller (CR 701.54a) — incrementing the tempt count and designating
///   a Ring-bearer through <see cref="Player.TheRingTemptsYou"/>.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only; abilities attached for
///   observability, nothing registered with a service.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ContinuousEffectsService?, Majik.Core.Stack.Stack?, Func{IReadOnlyList{Player}}?, Func{Player, Permanent?})"/>
///   — fully wired: registers the free-spell counter trigger, lets the sac
///   ability grant indestructible + tempt off live services.
///
/// ## Deferred (v1 gaps)
/// - <b>Ring-bearer choice prompt</b>: the sac ability tempts via the
///   supplied <c>ringBearerChooser</c> (defaults to the first creature the
///   controller controls). Agent-driven choice is deferred to the broader
///   prompt pass — same posture as the rest of the "choose a creature"
///   family.
/// - <b>Indestructible scope</b>: grant is creatures-only (same limitation
///   as Boros Charm mode 1 / Selfless Spirit).
/// </summary>
[CardName("Boromir, Warden of the Tower")]
public static class BoromirWardenOfTheTowerFactory
{
    public const string CardName = "Boromir, Warden of the Tower";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Boromir with no live wiring. All abilities are attached to
    /// the card shape for structural observability; none are registered with
    /// a trigger manager. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, continuousEffects: null,
            stack: null, allPlayersResolver: null, ringBearerChooser: null);

    /// <summary>
    /// Construct Boromir with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Bus the free-spell counter trigger and the Ring
    /// subsystem subscribe to.</param>
    /// <param name="triggers">When supplied, the free-spell counter trigger is
    /// registered so opponent casts drive it automatically, and the Ring's
    /// staged abilities self-register when Boromir tempts.</param>
    /// <param name="continuousEffects">When supplied, the sac ability grants
    /// the controller's creatures Indestructible until end of turn (CR 613.1f
    /// Layer 6).</param>
    /// <param name="stack">The live stack — required for the counter effect to
    /// remove the free spell (CR 701.5). Without it the counter trigger fires
    /// but no-ops.</param>
    /// <param name="allPlayersResolver">Supplies the full table for the Ring's
    /// 4+ "each opponent loses 3 life" ability.</param>
    /// <param name="ringBearerChooser">Picks the creature to become the
    /// Ring-bearer when Boromir tempts (CR 701.54a). Defaults to the first
    /// creature the controller controls.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects,
        Majik.Core.Stack.Stack? stack,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        Func<Player, Permanent?>? ringBearerChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Vigilance — CR 702.20. KeywordAbility marker.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // ----------------------------------------------------------------
        // Free-spell counter — CR 603.1 / CR 118.
        //   "Whenever an opponent casts a spell, if no mana was spent to
        //    cast it, counter that spell."
        // Gated on (1) the caster is NOT Boromir's controller (opponent),
        // and (2) the spell's WasFreeCast sentinel. The captured spell is
        // countered at resolution via Fx.Counter against the live stack.
        // ----------------------------------------------------------------
        Majik.Core.Spells.ISpell? capturedFreeSpell = null;

        var counterCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (e.Spell is not Majik.Core.Spells.Spell s) return false;
            if (!s.WasFreeCast) return false;
            // "an opponent" — only opponents of Boromir's controller (CR 102.5).
            if (ReferenceEquals(s.Controller, card.Controller ?? owner)) return false;
            capturedFreeSpell = s;
            return true;
        });

        var counterEffect = new Effect(
            $"{CardName}: counter the free spell",
            () =>
            {
                var spell = capturedFreeSpell;
                capturedFreeSpell = null;
                if (spell == null || stack == null) return;
                Majik.Core.Primitives.Fx.Counter(stack, spell);
            });

        var counterTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: counterCondition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(counterTrigger);
        triggers?.RegisterTriggeredAbility(counterTrigger);

        // ----------------------------------------------------------------
        // Sacrifice ability — CR 602 / CR 701.16 / CR 701.54.
        //   "Sacrifice Boromir: Creatures you control gain indestructible
        //    until end of turn. The Ring tempts you."
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: creatures you control gain indestructible until end of turn; the Ring tempts you",
            () =>
            {
                var controller = owner; // sacrifice resolves under the controller at activation
                // CR 613.1f / 702.12 — indestructible until end of turn for
                // every creature the controller controls (mirrors Boros Charm
                // mode 1). Creatures-only scope.
                if (continuousEffects != null)
                {
                    foreach (var creature in controller.Zones.Battlefield
                        .GetCards()
                        .OfType<Creature>()
                        .ToList())
                    {
                        continuousEffects.Register(
                            new GrantKeywordUntilEndOfTurnEffect(creature, "Indestructible"));
                    }
                }
                else
                {
                    foreach (var creature in controller.Zones.Battlefield
                        .GetCards()
                        .OfType<Creature>()
                        .ToList())
                    {
                        // Fallback path (no live game CES supplied). Mint the
                        // per-creature layers service BUS-WIRED when a bus is
                        // available so its generation cache invalidates on
                        // external events (CR 613 memoization) — a creature here
                        // may be a CDA (Tarmogoyf, Death's Shadow, …) whose P/T
                        // reads external state via this same ActiveEffects
                        // instance; a busless one would go stale. Busless only
                        // when no bus is threaded in (standalone construction).
                        creature.ActiveEffects ??= eventBus != null
                            ? new ContinuousEffectsService(eventBus)
                            : new ContinuousEffectsService();
                        creature.ActiveEffects.Register(
                            new GrantKeywordUntilEndOfTurnEffect(creature, "Indestructible"));
                    }
                }

                // CR 701.54a — "the Ring tempts you." Choose a creature the
                // controller controls to become the Ring-bearer (v1: first
                // creature, overridable via ringBearerChooser).
                var chosen = ringBearerChooser?.Invoke(controller)
                    ?? controller.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .FirstOrDefault();
                controller.TheRingTemptsYou(chosen, eventBus, triggers, allPlayersResolver);
            });

        var sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new SacrificeSelfCost(card) },
            effects: new IEffect[] { sacEffect });

        card.AddAbility(sacAbility);

        return card;
    }
}
