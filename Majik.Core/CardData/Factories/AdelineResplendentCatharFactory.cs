using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Adeline, Resplendent Cathar (Innistrad: Midnight
/// Hunt, {1}{W}{W}). Legendary Creature — Human Knight, printed power "*" /
/// toughness 4. Oracle text (verified against Scryfall):
///   "Vigilance
///    Adeline's power is equal to the number of creatures you control.
///    Whenever you attack, for each opponent, create a 1/1 white Human
///    creature token that's tapped and attacking that player or a
///    planeswalker they control."
///
/// The base shape (name, Legendary supertype, Creature, Human + Knight
/// subtypes, {1}{W}{W}, toughness 4) is materialised from the embedded JSON
/// definition (<c>adeline-resplendent-cathar.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Vigilance, the characteristic-
/// defining power, and the attack trigger are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express CDA P/T or attack triggers
/// (same posture as <see cref="IntiSeneschalOfTheSunFactory"/> /
/// <see cref="HeroOfBladeholdFactory"/>).
///
/// ## Implemented (v1)
///
/// - <b>Vigilance (CR 702.21)</b> — a <see cref="KeywordAbility"/> marker so
///   <c>ICard.Abilities</c> reflects the printed line and Scryfall keyword
///   parsing matches. Combat declaration reads vigilance off the keyword set.
///
/// - <b>"Adeline's power is equal to the number of creatures you control"
///   (CR 604.3 / 613.2 Layer 7a)</b> — a characteristic-defining ability
///   implemented via <see cref="CdaPowerToughnessEffect"/> whose power
///   evaluator counts every <see cref="CardType.Creature"/> on the battlefield
///   under Adeline's controller (read fresh on every Compute, so it tracks
///   creatures entering / leaving live — same evaluator-closure posture as
///   <see cref="TarmogoyfFactory"/>). The toughness evaluator returns the
///   printed 4 unchanged. Layer 7a SETS power; 7c counters / anthems stack on
///   top (CR 613.7). The CDA registers when Adeline enters the battlefield and
///   unregisters when she leaves, via a <see cref="CardMovedEvent"/>-driven
///   lifecycle mirroring Tarmogoyf's. Printed power is seeded 0 (CR 208.2c —
///   "*" is treated as the CDA-defined value; Layer 7a overwrites the seed).
///   Adeline counts herself among creatures you control (she is a creature on
///   the battlefield), so her minimum power on the battlefield is 1.
///
/// - <b>"Whenever you attack, for each opponent, create a 1/1 white Human
///   creature token that's tapped and attacking that player" (CR 508.3g)</b>
///   — a <see cref="TriggeredAbility"/> scoped to
///   <see cref="AttackersDeclaredEvent"/> where the attacking player is
///   Adeline's controller ("Whenever you attack", CR 508.1 / 109.5 — the
///   controller-scoped attack trigger, same gate as
///   <see cref="IntiSeneschalOfTheSunFactory"/>). On resolution, for each
///   opponent the supplied <paramref name="opponentResolver"/> returns, a 1/1
///   white Human token is created via
///   <see cref="TokenFactory.CreateOnBattlefield"/> and spliced into the
///   in-progress combat as a token that is already tapped and attacking the
///   same defender as the combat, via
///   <see cref="CombatManager.AddTappedAndAttackingToken"/> (CR 508.3 — enters
///   tapped; CR 508.4 — attacking the defending player). Because the tokens
///   are "put onto the battlefield attacking" rather than declared, they do
///   not re-trigger attack triggers (CR 508.3g).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Multiplayer per-opponent defenders</b>: the printed token attacks
///   "that player or a planeswalker they control" — i.e. each token attacks
///   ITS opponent. In 2-player (the engine's combat model) there is exactly
///   one opponent and the in-progress combat already targets that opponent, so
///   <see cref="CombatManager.AddTappedAndAttackingToken"/> (which mirrors the
///   combat's single defender) is exact. A true multiplayer combat with
///   per-opponent attacking-bands is deferred behind the same single-defender
///   combat model as <see cref="HeroOfBladeholdFactory"/>.
/// - <b>Planeswalker-attack choice</b>: "that player OR a planeswalker they
///   control" — v1 always attacks the player (the combat's defending player).
///   Choosing a planeswalker the opponent controls is deferred behind
///   agent-driven attack-target selection.
/// - <b>No-combat fallback</b>: when no combat is live the token enters
///   untapped, not attacking (the "tapped and attacking" fidelity requires a
///   combat to splice into — same no-combat fallback as Hero of Bladehold).
/// </summary>
[CardName("Adeline, Resplendent Cathar")]
public static class AdelineResplendentCatharFactory
{
    public const string CardName = "Adeline, Resplendent Cathar";
    public const string Slug = "adeline-resplendent-cathar";

    /// <summary>Granted keyword — CR 702.21 Vigilance.</summary>
    public const string Vigilance = "Vigilance";

    /// <summary>Printed toughness (power is the CDA "*").</summary>
    public const int Toughness = 4;

    /// <summary>Per-opponent token — 1/1 white Human.</summary>
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Adeline with no live runtime wiring (the dispatcher / shape
    /// path). Vigilance and the attack trigger are attached for shape
    /// observability; the CDA is not registered (no effects service) and the
    /// attack trigger creates no tokens (no opponent resolver / combat). This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, effects: null, eventBus: null, creaturesYouControlSource: null,
            opponentResolver: null, triggers: null, combat: null);

    /// <summary>
    /// Construct Adeline with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service the CDA power
    /// (<see cref="CdaPowerToughnessEffect"/>) registers against. May be null —
    /// the CDA is then not wired and power falls back to the printed seed.</param>
    /// <param name="eventBus">Event bus for the CDA's ETB/LTB lifecycle
    /// (<see cref="CardMovedEvent"/>). May be null — the CDA's battlefield gate
    /// still covers correctness, but no explicit unregister fires.</param>
    /// <param name="creaturesYouControlSource">Closure returning the cards to
    /// count for "creatures you control" — typically
    /// <c>() =&gt; controller.Zones.Battlefield.GetCards()</c>. The CDA filters
    /// to <see cref="CardType.Creature"/>. Read fresh on every Compute. May be
    /// null (CDA not wired).</param>
    /// <param name="opponentResolver">Closure returning the controller's
    /// opponents at attack-trigger resolution; one token is created per
    /// opponent. May be null — the attack trigger then creates no tokens.</param>
    /// <param name="triggers">TriggerManager the attack trigger is registered
    /// with so an <see cref="AttackersDeclaredEvent"/> by the controller lands
    /// it on the stack. May be null.</param>
    /// <param name="combat">When supplied, each token is spliced into the
    /// in-progress combat tapped and attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        Func<IEnumerable<ICard>>? creaturesYouControlSource,
        Func<IReadOnlyList<Player>>? opponentResolver,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Human + Knight, {1}{W}{W}, toughness 4; printed power
        // seeded 0). No abilities in the JSON — all three layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.21 — Vigilance keyword marker.
        card.AddAbility(new KeywordAbility(Vigilance, card, owner));

        // CR 604.3 / 613.2 Layer 7a — "Adeline's power is equal to the number
        // of creatures you control." Toughness stays the printed 4.
        if (effects != null && creaturesYouControlSource != null)
        {
            var lifecycle = new AdelineCdaLifecycle(card, owner, effects, eventBus, creaturesYouControlSource);
            lifecycle.Attach();
        }

        AddAttackTrigger(card, owner, opponentResolver, triggers, combat);

        return card;
    }

    /// <summary>
    /// Count "creatures you control" among the supplied cards. Pure helper
    /// exposed for tests; mirrors the closure baked into the live CDA.
    /// </summary>
    public static int CountCreatures(IEnumerable<ICard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.Count(c => c.HasType(CardType.Creature));
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "Whenever you attack, for each opponent, create a 1/1
    // white Human creature token that's tapped and attacking that player."
    // (CR 508.1 / 508.3g.)
    // -----------------------------------------------------------------------
    private static void AddAttackTrigger(
        Creature card,
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        var condition = new EventTriggerCondition<AttackersDeclaredEvent>((e, _) =>
            // "Whenever you attack" — only when Adeline's controller is the
            // attacking player (CR 508.1 / 109.5).
            ReferenceEquals(e.Combat.AttackingPlayer, card.Controller ?? owner));

        var effect = new Effect(
            $"{CardName}: on attack, create a 1/1 white Human token tapped & attacking for each opponent",
            () => ResolveAttackTrigger(card, owner, opponentResolver, combat));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    private static void ResolveAttackTrigger(
        Creature card,
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        CombatManager? combat)
    {
        if (opponentResolver == null) return;
        var controller = card.Controller ?? owner;

        var opponents = opponentResolver();
        if (opponents == null) return;

        // CR 111.4 — 1/1 white Human creature token.
        var spec = new TokenFactory.TokenSpec(
            Name: "Human",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Human },
            Keywords: null,
            Colors: new[] { ManaColor.White });

        // "for each opponent" — one token per opponent (CR 508.3g).
        foreach (var opp in opponents)
        {
            if (opp == null) continue;

            var token = TokenFactory.CreateOnBattlefield(spec, controller);

            // CR 508.3 / 508.4 — splice the token into the in-progress combat
            // as a tapped and attacking token. In 2-player the combat's single
            // defender IS the opponent, so the token attacks "that player".
            // When no combat is live the token stays on the battlefield
            // untapped (no-combat fallback, same as Hero of Bladehold).
            combat?.AddTappedAndAttackingToken(token);
        }
    }

    /// <summary>
    /// ETB/LTB lifecycle binder for Adeline's CDA power. Subscribes to
    /// <see cref="CardMovedEvent"/>; registers a
    /// <see cref="CdaPowerToughnessEffect"/> when Adeline enters the
    /// battlefield, unregisters when she leaves. Mirrors Tarmogoyf's lifecycle.
    /// </summary>
    private sealed class AdelineCdaLifecycle
    {
        private readonly Creature _source;
        private readonly Player _owner;
        private readonly ContinuousEffectsService _effects;
        private readonly IEventBus? _eventBus;
        private readonly Func<IEnumerable<ICard>> _creaturesSource;
        private readonly Action<CardMovedEvent> _handler;
        private CdaPowerToughnessEffect? _registered;
        private bool _attached;

        public AdelineCdaLifecycle(
            Creature source,
            Player owner,
            ContinuousEffectsService effects,
            IEventBus? eventBus,
            Func<IEnumerable<ICard>> creaturesSource)
        {
            _source = source;
            _owner = owner;
            _effects = effects;
            _eventBus = eventBus;
            _creaturesSource = creaturesSource;
            _handler = OnEvent;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _eventBus?.Subscribe(_handler);
            Sync();
        }

        private void OnEvent(CardMovedEvent e)
        {
            if (!ReferenceEquals(e.Card, _source)) return;
            Sync();
        }

        private void Sync()
        {
            var shouldBeActive = _source.Zone == ZoneType.Battlefield;
            if (shouldBeActive && _registered == null)
            {
                _registered = new CdaPowerToughnessEffect(
                    _source,
                    // CR 604.3 — "number of creatures you control"; toughness
                    // stays the printed 4 (CR 208.2c only the power is "*").
                    powerOf: _ => CountCreatures(_creaturesSource()),
                    toughnessOf: _ => Toughness);
                _effects.Register(_registered);
            }
            else if (!shouldBeActive && _registered != null)
            {
                _effects.Unregister(_registered);
                _registered = null;
            }
        }
    }
}
