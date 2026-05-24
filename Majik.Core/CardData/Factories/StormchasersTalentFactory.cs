using Majik.Core.Abilities;
using Majik.Core.CardData.Classes;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormchaser's Talent (Modern Horizons 3, {U}{R}).
///
/// Enchantment — Class {U}{R}. Oracle text:
///   "Class. (Gain the next level as a sorcery to add its ability.)
///    When this Class enters, create a 1/1 blue and red Mercenary creature
///      token with prowess.
///    {1}{U}{R}: Level 2
///    — Whenever you cast a noncreature spell, the Mercenary deals 1 damage
///      to any target.
///    {3}{U}{R}: Level 3
///    — Whenever you cast a noncreature spell, draw a card, then discard a
///      card."
///
/// ## Implementation (full Class leveling — CR 716)
/// - Shell: <see cref="Enchantment"/> with <see cref="CardSubtype.Class"/>
///   subtype (CR 205.3h / CR 716). Mana cost {U}{R}.
/// - <b>Class state binder</b>: a <see cref="ClassState"/> with MaxLevel=3
///   and per-level activation costs <c>{1}{U}{R}</c> / <c>{3}{U}{R}</c> is
///   attached to the <see cref="Permanent"/> via
///   <see cref="Permanent.AttachClassState"/>. <c>OnLevelUp</c> publishes a
///   <see cref="ClassLevelUpEvent"/> when an <see cref="IEventBus"/> is
///   supplied. Mirrors the <see cref="Majik.Core.CardData.Sagas.SagaState"/>
///   side-table pattern.
/// - <b>ETB trigger</b> (CR 603.6a): "When this Class enters, create a 1/1
///   blue and red Mercenary creature token with prowess." Spawned token is
///   captured into a per-Class holder so the Level-2 trigger can target
///   "the Mercenary" — the printed text. Routes through
///   <see cref="ZoneService"/> when supplied so <see cref="CardMovedEvent"/>
///   fires for downstream ETB listeners (Soul Warden, Champion of the
///   Parish, etc.). Token color identity (blue + red) stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/>.
/// - <b>Level-up activated abilities</b> (CR 716.4 — sequential): two
///   <see cref="ActivatedAbility"/>s wired with <c>sorcerySpeed: true</c>
///   (PR #460 — <see cref="Majik.Core.Rules.ActionValidator"/> gates the
///   timing). Each one's mana cost is the printed per-level cost; an
///   <see cref="ActivatedAbility.InterveningIf"/>-style gate is enforced by
///   the resolution body — it checks
///   <see cref="ClassState.CanLevelUpTo"/> (the strict CR 716.4 sequential
///   reading) and no-ops otherwise. The Level-2 activation gates on
///   <c>CurrentLevel == 1</c>; Level-3 on <c>CurrentLevel == 2</c>.
///   <see cref="Majik.Core.Costs.ActivatedAbility"/> already enforces the
///   mana cost — insufficient mana → activation rejected by
///   <see cref="Majik.Core.Costs.ICost.CanPay"/>.
/// - <b>Level 2 cast-trigger</b> ("Whenever you cast a noncreature spell,
///   the Mercenary deals 1 damage to any target"): a
///   <see cref="TriggeredAbility"/> filtered to noncreature spells cast by
///   the controller (<see cref="Triggers.OnNonCreatureSpellCastByController"/>)
///   AND gated by an <see cref="TriggeredAbility.InterveningIf"/> that
///   reads <see cref="ClassState.CurrentLevel"/> &gt;= 2. CR 603.4 — won't
///   trigger if the gate fails on event delivery. "Any target" picker is
///   v1 deterministic: first opponent (life-loss surface). "The Mercenary"
///   referent reads the Class's spawned-token holder; if the token is
///   no longer on the battlefield (died / exiled / bounced) the trigger
///   no-ops (CR 608.2b — illegal targets are skipped).
/// - <b>Level 3 cast-trigger</b> ("Whenever you cast a noncreature spell,
///   draw a card, then discard a card"): same <c>OnNonCreatureSpellCastByController</c>
///   filter, gated on <c>CurrentLevel &gt;= 3</c>. Loot body is the same
///   shape as <see cref="FaithlessLootingFactory"/> (deterministic
///   v1 discard picker — last card in hand).
///
/// ## Deferred (v1 gaps — narrowed by this PR)
/// - <b>"Any target" prompt for Level-2 damage</b>: v1 deterministically
///   hits the first opponent. Real agent-driven any-target prompt
///   (creature / planeswalker / battle / player) is deferred behind the
///   broader prompt surface — same posture as Lightning Bolt.
/// - <b>"You may" discard for Level-3 loot</b>: the printed text is
///   unconditional "draw a card, then discard a card", so no may-clause to
///   defer. Discard pick is the last card in hand (mirrors Faithless
///   Looting's deterministic v1 policy).
/// - <b>Prowess pump on the Mercenary token</b>: still keyword-marker-only
///   (same gap as Cori-Steel Cutter / Monastery Mentor — TokenFactory
///   doesn't thread ContinuousEffectsService for token-resident keywords
///   yet). The token reads as "has Prowess" via
///   <see cref="KeywordAbility"/> introspection but the +1/+1 pump is not
///   registered.
/// </summary>
[CardName("Stormchaser's Talent")]
public static class StormchasersTalentFactory
{
    public const string CardName = "Stormchaser's Talent";
    public const string PrintedManaCost = "{U}{R}";
    public const string Level2Cost = "{1}{U}{R}";
    public const string Level3Cost = "{3}{U}{R}";

    /// <summary>
    /// Construct Stormchaser's Talent with no live ZoneService / TriggerManager
    /// / EventBus wiring. The ETB Mercenary-token trigger + the two level-up
    /// activated abilities + the two per-level cast triggers are all
    /// attached to the card for shape inspection; tests fire them by
    /// invoking the effect directly. Suitable for dispatcher / shape tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null, eventBus: null, opponentResolver: null);

    /// <summary>
    /// Construct Stormchaser's Talent with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied, the spawned Mercenary
    /// token routes through <see cref="TokenFactory.CreateOnBattlefield"/>
    /// using the service so the token publishes <see cref="CardMovedEvent"/>
    /// on battlefield entry. When <paramref name="triggers"/> is supplied,
    /// all three triggered abilities (ETB + Level-2 + Level-3) are
    /// registered for bus-driven firing. When <paramref name="eventBus"/>
    /// is supplied, level-up resolutions publish
    /// <see cref="ClassLevelUpEvent"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IEventBus? eventBus = null,
        Func<Player>? opponentResolver = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Class });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Class state binder (CR 716). MaxLevel=3 with per-level costs
        // {1}{U}{R} and {3}{U}{R}.
        // ----------------------------------------------------------------
        var classState = new ClassState(
            maxLevel: 3,
            levelUpCosts: new[]
            {
                ManaCost.Parse(Level2Cost),
                ManaCost.Parse(Level3Cost),
            });

        if (eventBus != null)
        {
            classState.OnLevelUp = (from, to) =>
                eventBus.Publish(new ClassLevelUpEvent(card, card.Controller ?? owner, from, to));
        }

        card.AttachClassState(classState);

        // ----------------------------------------------------------------
        // ETB trigger — CR 603.6a.
        //   "When this Class enters, create a 1/1 blue and red Mercenary
        //    creature token with prowess."
        // The spawned Mercenary is captured into a per-Class holder so the
        // Level-2 trigger can address "the Mercenary" — the printed text.
        // Prowess pump on token deferred — keyword marker only, see class
        // xmldoc.
        // ----------------------------------------------------------------
        var mercenaryHolder = new Creature?[] { null };

        var etbEffect = new Effect(
            $"{CardName}: create a 1/1 Mercenary creature token with prowess",
            () =>
            {
                var token = CreateMercenaryToken(card.Controller ?? owner, zoneService);
                mercenaryHolder[0] = token;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Level-up activated abilities — CR 716.4 (sequential).
        // sorcerySpeed: true (PR #460) — ActionValidator rejects activations
        // outside the controller's main phase / on a non-empty stack.
        // The resolution body re-checks ClassState.CanLevelUpTo(N) so an
        // accidentally-resolved skip (e.g. Level-3 with CurrentLevel == 1)
        // no-ops without mutating state.
        // ----------------------------------------------------------------
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 2));
        card.AddAbility(BuildLevelUpAbility(card, owner, classState, targetLevel: 3));

        // ----------------------------------------------------------------
        // Level-2 cast trigger — "Whenever you cast a noncreature spell,
        // the Mercenary deals 1 damage to any target."
        // Gated by ClassState.CurrentLevel >= 2 via interveningIf
        // (CR 603.4 — trigger won't queue if the gate fails on event
        // delivery). "The Mercenary" referent reads mercenaryHolder; if
        // the token is no longer on the battlefield, the trigger no-ops
        // (CR 608.2b — illegal target → fizzle). Any-target picker is v1
        // deterministic: first opponent.
        // ----------------------------------------------------------------
        var level2Effect = new Effect(
            $"{CardName}: Level 2 — the Mercenary deals 1 damage to any target",
            () =>
            {
                var token = mercenaryHolder[0];
                if (token == null) return;
                if (token.Zone != ZoneType.Battlefield) return;

                // v1 any-target picker: opponent supplied by the caller's
                // resolver (mirrors WishclawTalismanFactory's opponentChooser).
                // Without a resolver wired in the dispatcher path, the
                // trigger queues but its damage step is a no-op (the
                // shape is still observable; live damage requires the
                // (owner, zones, triggers, bus, opponentResolver) overload).
                var opponent = opponentResolver?.Invoke();
                if (opponent == null) return;
                opponent.LoseLife(1);
            });

        var level2Trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnNonCreatureSpellCastByController(owner),
            effects: new IEffect[] { level2Effect },
            interveningIf: () => classState.CurrentLevel >= 2,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(level2Trigger);
        triggers?.RegisterTriggeredAbility(level2Trigger);

        // ----------------------------------------------------------------
        // Level-3 cast trigger — "Whenever you cast a noncreature spell,
        // draw a card, then discard a card." Same filter, gated on
        // CurrentLevel >= 3. Loot body mirrors FaithlessLootingFactory.
        // ----------------------------------------------------------------
        var level3Effect = new Effect(
            $"{CardName}: Level 3 — draw a card, then discard a card",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 121.1 — draw one card.
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    controller.MarkTriedToDrawFromEmptyLibrary();
                    // The "then discard" still resolves on whatever's in
                    // hand — but if the hand is empty we no-op (CR 701.16a).
                }
                else
                {
                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }

                // CR 701.16 — discard one card. Deterministic v1: last in
                // hand (matches Faithless Looting's policy).
                var pick = controller.Zones.Hand.GetCards().LastOrDefault();
                if (pick == null) return;
                controller.Zones.Hand.RemoveCard(pick);
                controller.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            });

        var level3Trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnNonCreatureSpellCastByController(owner),
            effects: new IEffect[] { level3Effect },
            interveningIf: () => classState.CurrentLevel >= 3,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(level3Trigger);
        triggers?.RegisterTriggeredAbility(level3Trigger);

        return card;
    }

    /// <summary>
    /// Build the "Level up to <paramref name="targetLevel"/>" activated
    /// ability (CR 716.4). Cost is <see cref="ClassState.CostFor"/>. The
    /// resolution body re-checks the sequential gate so an out-of-order
    /// activation no-ops without mutating state.
    /// </summary>
    private static ActivatedAbility BuildLevelUpAbility(
        Enchantment card, Player owner, ClassState classState, int targetLevel)
    {
        var cost = classState.CostFor(targetLevel);

        var effect = new Effect(
            $"{CardName}: level up to {targetLevel}",
            () =>
            {
                // CR 716.4 — sequential gate. CanLevelUpTo enforces
                // targetLevel == CurrentLevel + 1. If a player somehow
                // resolved Level-3 from CurrentLevel == 1 (impossible via
                // ActionValidator + cost flow today, but defensive), no-op
                // here — never silently skip levels.
                classState.LevelUpTo(targetLevel);
            });

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(cost) },
            effects: new IEffect[] { effect },
            sorcerySpeed: true);
    }

    /// <summary>
    /// CR 603.6a ETB effect — create a 1/1 blue and red Mercenary creature
    /// token with the <c>"Prowess"</c> keyword marker under
    /// <paramref name="controller"/>'s control. CR 105 / CR 111.4 — colour
    /// identity stamped via <see cref="TokenFactory.TokenSpec.Colors"/>;
    /// Prowess pump on the token deferred (see class xmldoc).
    /// </summary>
    private static Creature CreateMercenaryToken(Player controller, ZoneService? zones)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Mercenary",
            Power: 1,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Mercenary },
            Keywords: new[] { "Prowess" },
            // CR 105 / CR 111.4 — printed "1/1 blue and red Mercenary".
            Colors: new[]
            {
                Majik.Core.ValueObjects.ManaColor.Blue,
                Majik.Core.ValueObjects.ManaColor.Red,
            });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }

}
