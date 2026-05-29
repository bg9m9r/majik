using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sling-Gang Lieutenant (Lorwyn, {3}{B}).
///
/// Creature — Goblin 1/1. Oracle text (verified against Scryfall seed):
///   "When this creature enters, create two 1/1 red Goblin creature tokens.
///    Sacrifice a Goblin: Target player loses 1 life and you gain 1 life."
///
/// The base shape (name, Creature, Goblin subtype, {3}{B}, 1/1) is
/// materialised from the embedded JSON definition
/// (<c>sling-gang-lieutenant.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB token-creation
/// trigger and the "Sacrifice a Goblin" drain ability are layered on top
/// here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// token-creation effects or sacrifice-cost activated abilities, so those
/// live in the factory (same posture as <see cref="TwinSilkSpiderFactory"/>
/// for the ETB token half and <see cref="GoblinSledderFactory"/> /
/// <see cref="ClawsOfGixFactory"/> for the sacrifice-cost activated half).
///
/// ## Implemented (v1)
/// - 1/1 <see cref="Creature"/> — Goblin at {3}{B}.
/// - <b>ETB triggered ability (CR 603.6a / CR 111)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). Resolution creates two 1/1 red Goblin creature tokens
///   under this card's controller via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111.4 — same token
///   spec as <see cref="DragonFodderFactory"/>). No targets.
/// - <b>Activated ability (CR 602)</b>: "Sacrifice a Goblin: Target player
///   loses 1 life and you gain 1 life." Sole cost is a deterministic
///   "sacrifice a Goblin you control" payment
///   (<see cref="SlingGangSacrificeAGoblinCost"/>). The oracle has no
///   "another" qualifier — the Lieutenant itself is a legal sacrifice
///   (canonical Goblin-aristocrat line: chain Goblins through the drain,
///   ending on the Lieutenant). A single 1..1 "target player"
///   <see cref="TargetRequest"/> is declared; on resolution the chosen
///   player loses 1 life (CR 119.3) and the controller gains 1 life
///   (CR 119.3 — discrete life events).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. The ETB trigger is
///   attached for shape observability (not registered with a
///   <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring);
///   the drain ability is attached with a deterministic sacrifice cost.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. The ETB trigger registers with <paramref name="triggers"/>;
///   the tokens' ETB routes through <paramref name="zones"/> so their own
///   <see cref="CardMovedEvent"/> publishes for downstream subscribers.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven sacrifice picker</b>: the cost picks the first
///   non-self Goblin available, falling back to self. Optimal play
///   ("save the Lieutenant for last") is approximated, not agent-driven.
///   Same gap as <see cref="SkirkProspectorFactory"/> /
///   <see cref="GoblinSledderFactory"/>.
/// - <b>Target-player prompt</b>: the drain target is a mutable slot
///   (<see cref="SlingGangLieutenantAbility.DrainTarget"/>) set by the
///   caller/bot before activation; null no-ops the drain side (the
///   lifegain side still fires only when a target was chosen, since both
///   halves are a single "Target player loses 1 life and you gain 1 life"
///   instruction gated on a legal target — CR 608.2b). Same prompt-deferral
///   posture as <see cref="GoblinBombardmentFactory"/>'s damage target.
/// </summary>
[CardName("Sling-Gang Lieutenant")]
public static class SlingGangLieutenantFactory
{
    public const string CardName = "Sling-Gang Lieutenant";
    public const string Slug = "sling-gang-lieutenant";

    public const int TokenPower = 1;
    public const int TokenToughness = 1;
    public const int TokenCount = 2;

    public const int DrainAmount = 1;
    public const int GainAmount = 1;

    /// <summary>
    /// Construct Sling-Gang Lieutenant with no live wiring. The ETB trigger
    /// is attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Sling-Gang Lieutenant with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Goblin tokens' ETB routes
    /// through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with
    /// the bus so the corresponding <see cref="CardMovedEvent"/> lands the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Goblin subtype, {3}{B}, 1/1). The JSON carries no abilities —
        // the ETB token trigger + the sac-a-Goblin drain are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111 (Token).
        //   "When this creature enters, create two 1/1 red Goblin creature
        //    tokens."
        // No targets — pure token-creation (same wiring shape as
        // TwinSilkSpiderFactory / DragonFodderFactory).
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: create two 1/1 red Goblin creature tokens",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateGoblinTokens(controller, zones);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.
        //   "Sacrifice a Goblin: Target player loses 1 life and you gain
        //    1 life."
        // NOT a mana ability — it uses the stack and targets a player. The
        // sole cost is a deterministic sacrifice-a-Goblin payment
        // (self included — no "another" qualifier).
        // ----------------------------------------------------------------
        var sacCost = new SlingGangSacrificeAGoblinCost(card, owner);
        var ability = new SlingGangLieutenantAbility(card, owner, sacCost);
        card.AddAbility(ability);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create two 1/1 red Goblin creature tokens under
    /// <paramref name="controller"/>'s control (same spec as Dragon Fodder).
    /// </summary>
    public static void CreateGoblinTokens(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Goblin",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Goblin },
            Keywords: null,
            // CR 111.4 — printed "1/1 red Goblin creature token".
            Colors: new[] { ManaColor.Red });

        // CR 111 — one token per "create"; the instruction creates two.
        for (var i = 0; i < TokenCount; i++)
        {
            TokenFactory.CreateOnBattlefield(spec, controller, zones);
        }
    }
}

/// <summary>
/// Mutable box for the chosen drain target so the effect closure can
/// re-read it across activations (mirrors Goblin Bombardment's
/// <c>PingEffectState</c>). Populated either directly by tests / bots via
/// <see cref="SlingGangLieutenantAbility.DrainTarget"/>, or from
/// <see cref="ActivatedAbility.ChosenTargets"/> at resolution time when the
/// real targeting flow supplied a "target player".
/// </summary>
internal sealed class SlingGangDrainState
{
    /// <summary>Pre-set drain target (test / bot path).</summary>
    public Player? Target { get; set; }

    /// <summary>Back-reference to the owning ability, assigned after
    /// construction, so the effect closure can read
    /// <see cref="ActivatedAbility.ChosenTargets"/> from the real targeting
    /// flow at resolution time.</summary>
    public ActivatedAbility? Ability { get; set; }

    /// <summary>
    /// Resolve the effective drain target: an agent-chosen player from the
    /// targeting flow (<see cref="ActivatedAbility.ChosenTargets"/>) takes
    /// precedence; otherwise the pre-set <see cref="Target"/> slot is used.
    /// </summary>
    public Player? Resolve()
    {
        var ability = Ability;
        if (ability != null &&
            ability.ChosenTargets.Count > 0 &&
            ability.ChosenTargets[0].Count > 0 &&
            ability.ChosenTargets[0][0] is Player chosen)
        {
            return chosen;
        }

        return Target;
    }
}

/// <summary>
/// Sling-Gang Lieutenant's "Sacrifice a Goblin: Target player loses 1 life
/// and you gain 1 life." activated ability. Subclasses
/// <see cref="ActivatedAbility"/> so the chosen drain target and the
/// sacrifice cost travel with the ability instance (test / bot setters),
/// mirroring <see cref="GoblinBombardmentAbility"/>.
/// </summary>
public sealed class SlingGangLieutenantAbility : ActivatedAbility
{
    /// <summary>
    /// The sacrifice cost on the ability — exposed so callers can inspect
    /// or pre-set the chosen Goblin before activation.
    /// </summary>
    public SlingGangSacrificeAGoblinCost SacrificeChoice { get; }

    private readonly SlingGangDrainState _state;

    /// <summary>
    /// The chosen drain target ("Target player"). Set this before
    /// activation. <c>null</c> means the effect is a no-op (no legal target
    /// chosen — the whole "loses 1 / you gain 1" instruction is gated on a
    /// chosen target per CR 608.2b).
    /// </summary>
    public Player? DrainTarget
    {
        get => _state.Target;
        set => _state.Target = value;
    }

    internal SlingGangLieutenantAbility(
        Creature source,
        Player controller,
        SlingGangSacrificeAGoblinCost sacCost)
        : this(source, controller, sacCost, new SlingGangDrainState())
    {
    }

    private SlingGangLieutenantAbility(
        Creature source,
        Player controller,
        SlingGangSacrificeAGoblinCost sacCost,
        SlingGangDrainState state)
        : base(
            source: source,
            controller: controller,
            costs: new ICost[] { sacCost },
            effects: new IEffect[]
            {
                new Effect(
                    $"{SlingGangLieutenantFactory.CardName}: target player loses 1 life and you gain 1 life",
                    () =>
                    {
                        // Prefer an agent-chosen target from the real
                        // targeting flow; otherwise fall back to the
                        // pre-set DrainTarget slot (test / bot path).
                        var target = state.Resolve();

                        // CR 608.2c — no legal target ⇒ the ability does
                        // nothing on resolution.
                        if (target == null) return;

                        // CR 119.3 — life loss and life gain are discrete
                        // events.
                        target.LoseLife(SlingGangLieutenantFactory.DrainAmount);
                        controller.GainLife(SlingGangLieutenantFactory.GainAmount);
                    }),
            },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // CR — the drain is a direct life-loss attack on a
                    // player; Burn is the closest bot intent classifier.
                    Intent: BotIntent.Burn),
            })
    {
        SacrificeChoice = sacCost;
        _state = state;
        // Back-reference so the effect closure can read ChosenTargets from
        // the real targeting flow at resolution time.
        state.Ability = this;
    }
}

/// <summary>
/// "Sacrifice a Goblin" activated-ability cost — controller must have at
/// least one Goblin on the battlefield (includes self per oracle: no
/// "another" qualifier). Pays by sacrificing one Goblin, preferring another
/// Goblin first and falling back to self when self is the only candidate.
/// Mirrors the deterministic v1 picker used by
/// <see cref="GoblinSledderFactory"/> / <see cref="SkirkProspectorFactory"/>.
/// </summary>
public sealed class SlingGangSacrificeAGoblinCost : ICost
{
    private readonly Creature _self;
    private readonly Player _controller;

    /// <summary>The Goblin actually sacrificed once <see cref="Pay"/>
    /// succeeded. Null before payment.</summary>
    public Creature? Sacrificed { get; private set; }

    public SlingGangSacrificeAGoblinCost(Creature self, Player controller)
    {
        _self = self ?? throw new ArgumentNullException(nameof(self));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string Description => "Sacrifice a Goblin";

    public bool CanPay(Player player)
    {
        if (player == null) return false;
        if (!ReferenceEquals(player, _controller) &&
            !ReferenceEquals(player, _self.Controller))
        {
            return false;
        }

        var ctrl = _self.Controller ?? _controller;
        return ctrl.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => c.HasSubtype(CardSubtype.Goblin));
    }

    public void Pay(Player player)
    {
        if (!CanPay(player))
        {
            throw new InvalidOperationException(
                "Cannot pay Sacrifice a Goblin: no Goblin on the battlefield.");
        }

        var ctrl = _self.Controller ?? _controller;

        // Deterministic v1: prefer another Goblin first; fall back to self.
        Creature? pick = ctrl.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c =>
                c.HasSubtype(CardSubtype.Goblin) && !ReferenceEquals(c, _self))
            ?? ctrl.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .FirstOrDefault(c => c.HasSubtype(CardSubtype.Goblin));

        if (pick == null)
        {
            throw new InvalidOperationException(
                "Sacrifice a Goblin: no Goblin found at payment time.");
        }

        ctrl.Zones.Battlefield.RemoveCard(pick);
        ctrl.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        Sacrificed = pick;
    }
}
