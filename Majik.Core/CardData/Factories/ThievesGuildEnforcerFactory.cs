using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thieves' Guild Enforcer (Core Set 2021, {B}).
/// Creature — Human Rogue 1/1.
///
/// Oracle text:
///   "Flash
///    Whenever Thieves' Guild Enforcer enters or attacks, each opponent
///    mills two cards.
///    As long as an opponent has eight or more cards in their graveyard,
///    Thieves' Guild Enforcer gets +2/+1 and has deathtouch."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Rogue, mana cost {B}, owner/controller wired.
/// - <b>Flash</b> keyword marker (CR 702.8) via <see cref="KeywordAbility"/>.
/// - <b>ETB-or-attacks trigger (CR 603.1 / CR 603.6c)</b> wired as a
///   single <see cref="TriggeredAbility"/> with a disjoint
///   <see cref="ITriggerCondition"/> matching either
///   <see cref="CardMovedEvent"/> (self → battlefield) OR
///   <see cref="CreatureAttacksEvent"/> (self attacks). On resolution
///   every opponent mills two cards (CR 701.13b) via
///   <see cref="MillAction.Apply"/>.
/// - <b>Conditional self-buff</b> "As long as an opponent has ≥8 cards
///   in their graveyard, this gets +2/+1 and has deathtouch" wired as
///   TWO continuous effects registered against the layers service:
///     * <see cref="ConditionalSelfPumpEffect"/> — Layer 7c +2/+1 on self
///       gated by the predicate (CR 613.7c).
///     * <see cref="ConditionalSelfKeywordEffect"/> — Layer 6 grant
///       "Deathtouch" on self gated by the same predicate (CR 613.1d /
///       CR 702.2).
///   Both effects re-evaluate the predicate every Compute pass, so the
///   bonus appears / lifts dynamically as graveyards grow and shrink
///   (no manual SBA hook required — the layers service re-runs on every
///   read).
///
/// ## Source closure injection
/// Same shape as <see cref="GoblinRabblemasterFactory"/> /
/// <see cref="AshiokDreamRenderFactory"/> — the conditional buff and the
/// mill trigger both need the live player list. The factory accepts a
/// <c>Func&lt;IReadOnlyList&lt;Player&gt;&gt;</c> closure
/// (<paramref name="allPlayersResolver"/>). Without it the mill is a
/// no-op and the conditional buff stays inactive (graveyard scan returns
/// 0 opponents-with-≥8).
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger-on-stack timing</b>: the mill body runs immediately when
///   the trigger effect resolves. Real MTG puts the trigger on the stack
///   and resolves later; v1 collapses to trigger-resolves-now (same
///   posture as Goblin Rabblemaster's attack rider).
/// - <b>Single ETB / attack rider</b>: combined into one
///   <see cref="TriggeredAbility"/> with a disjoint condition rather than
///   two separate abilities. Observationally equivalent for the "each
///   opponent mills two" payload.
/// - <b>LTB unregister</b>: both conditional effects gate on
///   <see cref="ContinuousEffect.IsActive"/> reading
///   <see cref="Permanent.Zone"/>, so leaving the battlefield drops the
///   buff cleanly without a Prune pass.
/// </summary>
[CardName("Thieves' Guild Enforcer")]
public static class ThievesGuildEnforcerFactory
{
    public const string CardName = "Thieves' Guild Enforcer";
    public const string PrintedManaCost = "{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>Per-opponent mill count on the ETB-or-attack trigger.</summary>
    public const int MillCount = 2;

    /// <summary>
    /// Graveyard threshold for the conditional self-buff (CR 700.2g —
    /// "as long as" continuously re-evaluates).
    /// </summary>
    public const int GraveyardThreshold = 8;

    /// <summary>Power bonus while the threshold predicate holds.</summary>
    public const int PowerBonus = 2;

    /// <summary>Toughness bonus while the threshold predicate holds.</summary>
    public const int ToughnessBonus = 1;

    /// <summary>
    /// Construct Thieves' Guild Enforcer with no live runtime services.
    /// Suitable for card-shape / dispatcher tests — the conditional
    /// continuous effects are NOT registered (no layers service) and the
    /// ETB-or-attack mill body is a no-op (no players resolver). The
    /// trigger ability shape is still attached to the card so
    /// <see cref="ICard.Abilities"/> includes it.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(
            owner,
            continuousEffects: null,
            triggers: null,
            allPlayersResolver: null);

    /// <summary>
    /// Construct a fully-wired Thieves' Guild Enforcer.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// two conditional self-effects against. May be null — no live
    /// buff / deathtouch grant.</param>
    /// <param name="triggers">TriggerManager to register the ETB-or-
    /// attack mill trigger against. May be null — the trigger shape is
    /// still attached to the card.</param>
    /// <param name="allPlayersResolver">Closure returning the full
    /// player list. Used by both the trigger body (mill every opponent)
    /// AND the conditional self-buff predicate (scan opponents'
    /// graveyards). May be null — mill body is a no-op and buff stays
    /// inactive.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash keyword marker.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 700.2g — predicate is "any opponent has ≥8 cards in their
        // graveyard". Closure shape: capture owner + the resolver so the
        // continuous effects can re-read each Compute.
        bool Predicate()
        {
            if (allPlayersResolver == null) return false;
            var players = allPlayersResolver();
            if (players == null) return false;

            var controller = card.Controller ?? owner;
            foreach (var p in players)
            {
                if (ReferenceEquals(p, controller)) continue;
                if (p.Zones.Graveyard.GetCards().Count() >= GraveyardThreshold)
                    return true;
            }
            return false;
        }

        // CR 603.1 / 603.6c — combined "ETB or attacks" trigger. One
        // ability, one trigger condition that matches either event.
        var millEffect = new Effect(
            $"{CardName}: each opponent mills 2",
            () =>
            {
                if (allPlayersResolver == null) return;
                var players = allPlayersResolver();
                if (players == null) return;

                var controller = card.Controller ?? owner;
                foreach (var p in players)
                {
                    if (ReferenceEquals(p, controller)) continue;
                    // CR 701.13b — mill 2 per opponent. Empty-library
                    // handled inside MillAction.Apply.
                    MillAction.Apply(p, MillCount);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EtbOrAttacksSelfCondition(card),
            effects: new IEffect[] { millEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // CR 700.2g — "As long as ..." conditional static. Two effects
        // share the same predicate closure:
        //   * Layer 7c +2/+1 on self
        //   * Layer 6 grant Deathtouch on self
        // IsActive re-evaluates the predicate every Compute pass so the
        // bonus / deathtouch appear and lift dynamically as graveyards
        // change size.
        if (continuousEffects != null)
        {
            continuousEffects.Register(new ConditionalSelfPumpEffect(
                card, PowerBonus, ToughnessBonus, Predicate));
            continuousEffects.Register(new ConditionalSelfKeywordEffect(
                card, "Deathtouch", Predicate));
        }

        return card;
    }

    /// <summary>
    /// Disjoint trigger condition matching either "self enters the
    /// battlefield" (CR 603.6 ETB) or "self attacks" (CR 508.1f
    /// per-attacker self-match). Public for unit-test introspection.
    /// </summary>
    public sealed class EtbOrAttacksSelfCondition : ITriggerCondition
    {
        private readonly ICard _source;
        public EtbOrAttacksSelfCondition(ICard source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// Not load-bearing for routing — <see cref="TriggerManager"/>
        /// subscribes via <see cref="IEventBus.SubscribeAll"/> and lets
        /// <see cref="Matches"/> filter. Reported as <see cref="CardMovedEvent"/>
        /// (the first of the two events the condition cares about); the
        /// attack arm is checked in <see cref="Matches"/> regardless.
        /// </summary>
        public Type EventType => typeof(CardMovedEvent);

        public bool Matches(GameEvent e, ITriggeredAbility ability)
        {
            switch (e)
            {
                case CardMovedEvent cme
                        when ReferenceEquals(cme.Card, _source)
                          && cme.ToZone == ZoneType.Battlefield:
                    return true;
                case CreatureAttacksEvent cae
                        when ReferenceEquals(cae.Attacker, _source):
                    return true;
                default:
                    return false;
            }
        }
    }
}

/// <summary>
/// CR 613.7c — Layer 7c +P/+T applied to a single source creature ONLY
/// while a predicate holds. Sibling of <see cref="PumpUntilEndOfTurnEffect"/>
/// without the EOT expiry; sibling of <see cref="LordStaticEffect"/>
/// without the multi-target subtype filter. Used by
/// <see cref="Majik.Core.CardData.Factories.ThievesGuildEnforcerFactory"/>
/// for the "as long as an opponent has 8+ cards in their graveyard,
/// Thieves' Guild Enforcer gets +2/+1" rider.
///
/// The predicate is re-evaluated every <see cref="IsActive"/> read (which
/// the layers service calls every Compute pass), so the bonus appears /
/// lifts dynamically as the gated game state changes — no manual SBA hook
/// required.
/// </summary>
public sealed class ConditionalSelfPumpEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly int _power;
    private readonly int _toughness;
    private readonly Func<bool> _predicate;

    public ConditionalSelfPumpEffect(
        Permanent source, int power, int toughness, Func<bool> predicate)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _power = power;
        _toughness = toughness;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public override Layer Layer => Layer.PT_Modify;
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == ZoneType.Battlefield && _predicate();

    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
    }
}

/// <summary>
/// CR 613.1d / 702.2 — Layer 6 keyword grant on a single source creature
/// ONLY while a predicate holds. Sibling of
/// <see cref="GrantKeywordUntilEndOfTurnEffect"/> without the EOT expiry.
/// Used by <see cref="Majik.Core.CardData.Factories.ThievesGuildEnforcerFactory"/>
/// for the "and has deathtouch" rider on the conditional self-buff.
/// </summary>
public sealed class ConditionalSelfKeywordEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly string _keyword;
    private readonly Func<bool> _predicate;

    public ConditionalSelfKeywordEffect(
        Permanent source, string keyword, Func<bool> predicate)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(keyword))
            throw new ArgumentException("Keyword required", nameof(keyword));
        _keyword = keyword;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public override Layer Layer => Layer.Abilities;
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == ZoneType.Battlefield && _predicate();

    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add(_keyword);
    }
}
