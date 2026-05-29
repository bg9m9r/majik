using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Screaming Nemesis (Duskmourn: House of Horror,
/// {2}{R}).
///
/// Creature — Spirit 3/3. Oracle text (verified against Scryfall, set
/// <c>dsk</c> #157):
///   "Haste
///    Whenever this creature is dealt damage, it deals that much damage to
///    any other target. If a player is dealt damage this way, they can't
///    gain life for the rest of the game."
///
/// ## Implemented (v1)
/// - 3/3 Creature — Spirit, mana cost {2}{R}. Card shape comes from the
///   embedded JSON (<c>screaming-nemesis.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Haste</b> (CR 702.10) wired via <see cref="KeywordAbility"/>; combat
///   code reads the marker through
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/>.
/// - <b>Damage-received trigger</b> (CR 603.1) over
///   <see cref="DamageDealtEvent"/> filtered to <c>TargetCard == this</c>.
///   The amount of damage is captured off the event and forwarded by the
///   resolved effect to the configured redirect target via
///   <see cref="Fx.DealDamageAny"/> (any target: player / creature /
///   planeswalker). When an <see cref="IEventBus"/> is supplied the effect
///   republishes the redirect as a non-combat <see cref="DamageDealtEvent"/>
///   with <c>DamageType.Ability</c> (CR 119.2c) so clients can animate the
///   ping. This mirrors <see cref="BorosReckonerFactory"/>'s near-identical
///   "whenever dealt damage, deal that much to a target" trigger.
/// - <b>"If a player is dealt damage this way, they can't gain life for the
///   rest of the game" (CR 614 / CR 119.6)</b> — when the redirect target
///   resolves to a <see cref="Player"/>, a <see cref="LifeGainLock"/>
///   replacement scoped to that player is registered on the player's
///   attached <see cref="ReplacementBus"/>. The lock rewrites every
///   <see cref="LifeGainIntent"/> for that player to zero and is
///   <b>permanent</b> — unlike Skullcrack's per-turn blocker it does NOT
///   implement <c>IEndOfTurnExpirable</c>, so it persists for the rest of
///   the game (CR 614 — no duration clause). Registration is idempotent
///   per player via <see cref="ReplacementBus.FindByTag{TIntent}"/>. When
///   the targeted player has no attached bus the lock silently no-ops
///   (mirrors Roiling Vortex's unwired posture).
///
/// ## v1 simplification — trigger vs. replacement effect
/// Like Boros Reckoner, printed Screaming Nemesis's redirect is modelled as
/// a triggered ability ("whenever this creature is dealt damage, it deals
/// that much damage to any other target") rather than a damage-replacement
/// effect. The damage still resolves on Screaming Nemesis (marked damage /
/// SBAs apply) before the redirect fires, and the redirect goes on the
/// stack subject to the priority loop. This matches the printed card's
/// trigger wording exactly (it is a true triggered ability, not a
/// replacement) so the only observable v1 gap is that the redirect target
/// is supplied externally (see <see cref="ScreamingNemesisTrigger.RedirectTarget"/>)
/// rather than chosen via a live prompt — the same convention Boros
/// Reckoner uses.
///
/// ## Redirect target — "any OTHER target"
/// Set <see cref="ScreamingNemesisTrigger.RedirectTarget"/> before the
/// trigger resolves to choose the any-target (Player / Creature /
/// Planeswalker). Per the oracle "any OTHER target", the target must not be
/// Screaming Nemesis itself; the resolved effect defensively no-ops if the
/// redirect target is this creature. When null at resolution the effect is
/// a no-op (no implicit choice — production wiring supplies a prompt).
/// </summary>
[CardName("Screaming Nemesis")]
public static class ScreamingNemesisFactory
{
    public const string CardName = "Screaming Nemesis";
    public const string Slug = "screaming-nemesis";

    /// <summary>
    /// Construct Screaming Nemesis with no live event-bus / TriggerManager
    /// wiring. The damage-received trigger and Haste keyword are attached
    /// for shape but the trigger is not registered. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Screaming Nemesis with optional runtime services. When
    /// <paramref name="eventBus"/> is supplied the redirect republishes a
    /// non-combat <see cref="DamageDealtEvent"/> (CR 119.2c); when
    /// <paramref name="triggers"/> is supplied the damage-received trigger
    /// is registered so a <see cref="DamageDealtEvent"/> automatically
    /// queues the ability.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(def, owner);

        // Haste keyword marker (CR 702.10). Read by combat / summoning-sickness
        // checks via CombatAbilities.HasHaste.
        card.AddAbility(new KeywordAbility("Haste", source: card, controller: owner));

        // ----------------------------------------------------------------
        // Damage-received trigger — CR 603.1.
        //   "Whenever this creature is dealt damage, it deals that much
        //    damage to any other target. If a player is dealt damage this
        //    way, they can't gain life for the rest of the game."
        // Matches DamageDealtEvent (and its CombatDamageDealtEvent subclass)
        // where TargetCard is this card. The amount is captured in a closure
        // shared with the resolved effect.
        // ----------------------------------------------------------------
        int capturedAmount = 0;

        var effect = new Effect(
            "Screaming Nemesis: deal captured damage to redirect target; lock life gain if a player",
            () =>
            {
                if (capturedAmount <= 0) return;

                var trig = card.Abilities.OfType<ScreamingNemesisTrigger>().FirstOrDefault();
                var target = trig?.RedirectTarget;
                if (target == null) return;

                // "any OTHER target" — defensively refuse to redirect onto
                // Screaming Nemesis itself (CR 115.4 — "other" exclusion).
                if (ReferenceEquals(target, card)) return;

                int amount = capturedAmount;
                capturedAmount = 0; // clear so a future fire doesn't reuse it

                // CR 119.2c — non-combat damage from a triggered ability.
                Player? targetPlayer = target as Player;
                ICard? targetCard = target as ICard;
                eventBus?.Publish(new DamageDealtEvent(
                    sourceCard: card,
                    sourcePlayer: null,
                    targetCard: targetCard,
                    targetPlayer: targetPlayer,
                    amount: amount,
                    damageType: DamageType.Ability));

                // Deal the damage (Player / Creature / Planeswalker).
                Fx.DealDamageAny(target, amount);

                // CR 614 / CR 119.6 — "If a player is dealt damage this way,
                // they can't gain life for the rest of the game." Register a
                // permanent, player-scoped life-gain lock on that player's
                // replacement bus (idempotent per player). Unlike Skullcrack's
                // per-turn blocker this lock does NOT expire end-of-turn.
                if (targetPlayer?.Replacements is { } bus)
                {
                    if (bus.FindByTag<LifeGainIntent>(LifeGainLockTag(targetPlayer)) == null)
                    {
                        bus.Register(new LifeGainLock(targetPlayer));
                    }
                }
            });

        var trigger = new ScreamingNemesisTrigger(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<DamageDealtEvent>((e, _) =>
            {
                if (e.TargetCard is not Creature recv) return false;
                if (!ReferenceEquals(recv, card)) return false;
                if (e.Amount <= 0) return false;
                capturedAmount = e.Amount;
                return true;
            }),
            effects: new IEffect[] { effect });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>Tag used to keep the per-player life-gain lock idempotent
    /// across repeated triggers (CR 614 — one lock per player suffices).</summary>
    private static object LifeGainLockTag(Player player) => ("ScreamingNemesis.LifeGainLock", player);

    /// <summary>
    /// CR 614 / CR 119.6 — "[player] can't gain life for the rest of the
    /// game." A permanent, player-scoped <see cref="LifeGainIntent"/>
    /// replacement that rewrites the locked player's life gains to zero.
    /// Deliberately does NOT implement <c>IEndOfTurnExpirable</c>, so
    /// <see cref="ReplacementBus.ExpireEndOfTurn"/> leaves it in place for
    /// the remainder of the game.
    /// </summary>
    public sealed class LifeGainLock : IReplacementEffect<LifeGainIntent>
    {
        private readonly Player _locked;

        public LifeGainLock(Player locked) =>
            _locked = locked ?? throw new ArgumentNullException(nameof(locked));

        public bool OneShot => false;
        public object? Tag => LifeGainLockTag(_locked);

        public bool Applies(LifeGainIntent intent, IReadOnlyList<object> history) =>
            ReferenceEquals(intent.Target, _locked) && intent.Amount > 0;

        public LifeGainIntent Replace(LifeGainIntent intent, IReadOnlyList<object> history) =>
            intent with { Amount = 0 };
    }

    /// <summary>
    /// Screaming Nemesis's damage-received triggered ability. Subclasses
    /// <see cref="TriggeredAbility"/> so the chosen "any other target"
    /// travels with the ability instance (test / bot setter), mirroring
    /// <see cref="BorosReckonerTrigger"/>.
    /// </summary>
    public sealed class ScreamingNemesisTrigger : TriggeredAbility
    {
        /// <summary>
        /// The "any other target" for the redirected damage. Accepts a
        /// <see cref="Player"/>, <see cref="Creature"/>, or
        /// <see cref="Majik.Core.Cards.Planeswalker"/>. When null at
        /// resolution time the effect is a no-op; when it references
        /// Screaming Nemesis itself the redirect is refused ("other").
        /// </summary>
        public object? RedirectTarget { get; set; }

        public ScreamingNemesisTrigger(
            ICard source,
            Player controller,
            ITriggerCondition condition,
            IEffect[] effects)
            : base(
                source: source,
                controller: controller,
                condition: condition,
                effects: effects,
                activeZones: new[] { ZoneType.Battlefield })
        {
        }
    }
}
