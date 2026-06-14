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
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mage's Attendant ({2}{W}).
///
/// Creature — Cat Rogue 3/2. Oracle text (verified against Scryfall):
///   "When this creature enters, create a 1/1 blue Wizard creature token
///    with "{1}, Sacrifice this token: Counter target noncreature spell
///    unless its controller pays {1}.""
///
/// The base shape (name, Creature, Cat / Rogue subtypes, {2}{W}, 3/2) is
/// materialised from the embedded JSON definition (<c>mages-attendant.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The ETB token-creation trigger
/// is layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express token-creation effects, so that lives in the factory (same posture
/// as <see cref="TwinSilkSpiderFactory"/>, whose ETB also mints a single token
/// carrying its own abilities).
///
/// The minted token is a 1/1 blue Wizard carrying a Spellstutter-style
/// activated ability — the counter-unless-pay half mirrors
/// <see cref="MausoleumWandererFactory"/>'s sac/counter activation and
/// <see cref="SpellPierceFactory"/>'s "noncreature spell unless pay {N}"
/// resolution.
///
/// ## Implemented (v1)
/// - 3/2 <see cref="Creature"/> — Cat Rogue at {2}{W}.
/// - <b>ETB triggered ability (CR 603.6a / CR 111)</b> over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). Resolution mints one 1/1 blue Wizard creature token under
///   this card's controller via <see cref="TokenFactory.CreateOnBattlefield"/>.
/// - <b>Token's activated ability (CR 602.1 / CR 701.5)</b>:
///   "{1}, Sacrifice this token: Counter target noncreature spell unless its
///    controller pays {1}." Costs are <see cref="ManaCostCost"/>("{1}") +
///   <see cref="AdditionalCost.Sacrifice"/>(token); target is a 1..1
///   noncreature spell on the stack. On resolution (CR 608.2b re-check):
///   if the target's controller can pay {1} the engine auto-pays (v1
///   auto-pay posture — same queue as Daze / Mana Leak / Mausoleum Wanderer)
///   and the counter no-ops; otherwise the spell is removed from the stack
///   via <see cref="OracleSpellBinder.RemoveFromStack"/> and moved to the
///   graveyard (CR 701.5 / 701.5b — uncounterable spells survive).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability; not registered with any
///   <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring. This
///   is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?, Majik.Core.Stack.Stack?)"/>
///   — fully wired. The ETB trigger registers with <paramref name="triggers"/>;
///   the token's ETB routes through <paramref name="zones"/>; the token's
///   sac/counter ability removes the targeted spell from
///   <paramref name="stack"/> on resolution.
///
/// ## Deferred
/// - Real "do you want to pay {1}?" agent prompt — same queue as Daze /
///   Mana Leak / Spell Pierce. v1 is deterministic: "pay if able."
/// </summary>
[CardName("Mage's Attendant")]
public static class MagesAttendantFactory
{
    public const string CardName = "Mage's Attendant";
    public const string Slug = "mages-attendant";
    public const string WizardTokenName = "Wizard";
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>The {N} mana cost in the token's activation ("{1}").</summary>
    public const string TokenActivationManaCost = "{1}";

    /// <summary>The {N} the countered spell's controller may pay to avoid
    /// the counter ("{1}").</summary>
    public const int UnlessPayN = 1;

    /// <summary>
    /// Construct Mage's Attendant with no live wiring. The ETB trigger is
    /// attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>, no <see cref="ZoneService"/> / stack
    /// wiring. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null, stack: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises a two-param
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload). Forwards <c>effects.EventBus</c> so the minted Wizard token's
    /// "Sacrifice this token:" activation cost carries the bus on the
    /// construction path and publishes a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a) for aristocrat payoffs. Mirrors the Festival-Crasher /
    /// Spellbomb seam.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, zones: null, triggers: null, stack: null, eventBus: effects?.EventBus);

    /// <summary>
    /// Construct Mage's Attendant with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Wizard token's ETB routes
    /// through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with
    /// the bus so the corresponding <see cref="CardMovedEvent"/> lands the
    /// ability on the stack automatically (CR 603.2).</param>
    /// <param name="stack">Active stack — threaded into the token's
    /// sac/counter ability so it removes the targeted spell on resolution.
    /// May be null for shape tests (the counter half no-ops).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers,
        Majik.Core.Stack.Stack? stack) =>
        Create(owner, zones, triggers, stack, eventBus: null);

    /// <summary>
    /// Construct Mage's Attendant with optional runtime services + an event bus.
    /// When <paramref name="eventBus"/> is supplied it is threaded into the
    /// minted Wizard token's "Sacrifice this token:" activation cost so paying
    /// that cost publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers,
        Majik.Core.Stack.Stack? stack,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Cat / Rogue subtypes, {2}{W}, 3/2). The JSON carries no abilities —
        // the ETB token trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111 (Token).
        //   "When this creature enters, create a 1/1 blue Wizard creature
        //    token with "{1}, Sacrifice this token: Counter target
        //    noncreature spell unless its controller pays {1}.""
        // No targets — pure token-creation.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: create a 1/1 blue Wizard creature token with a sac-to-counter ability",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateWizardToken(controller, zones, stack, eventBus);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 — create one 1/1 blue Wizard creature token under
    /// <paramref name="controller"/>'s control, carrying the printed
    /// "{1}, Sacrifice this token: Counter target noncreature spell unless
    /// its controller pays {1}." activated ability.
    /// </summary>
    public static Creature CreateWizardToken(
        Player controller,
        ZoneService? zones = null,
        Majik.Core.Stack.Stack? stack = null,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: WizardTokenName,
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Wizard },
            // CR 105.2c — printed blue token.
            Colors: new[] { ManaColor.Blue });

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);

        AttachCounterAbility(token, controller, stack, eventBus);

        return token;
    }

    /// <summary>
    /// Attach the token's "{1}, Sacrifice this token: Counter target
    /// noncreature spell unless its controller pays {1}." activated ability
    /// (CR 602.1 / CR 701.5). Costs are {1} mana + sacrifice-self; target is
    /// a 1..1 noncreature spell on the stack.
    /// </summary>
    private static void AttachCounterAbility(
        Creature token,
        Player controller,
        Majik.Core.Stack.Stack? stack,
        IEventBus? eventBus = null)
    {
        ActivatedAbility? ability = null;

        var counterEffect = new Effect(
            $"{WizardTokenName} token — counter target noncreature spell unless its controller pays {{{UnlessPayN}}}",
            () => ResolveCounterActivation(ability, stack));

        ability = new ActivatedAbility(
            source: token,
            controller: controller,
            costs: new ICost[]
            {
                new ManaCostCost(TokenActivationManaCost),
                AdditionalCost.Sacrifice(token, eventBus),
            },
            effects: new IEffect[] { counterEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target noncreature spell",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter,
                    // CR 601.2c — choose-time legality. Enumerate the live
                    // stack and filter to spells whose card is NOT a creature.
                    CandidateGatherer: ctx => ctx.Stack.GetAll()
                        .OfType<ISpell>()
                        .Where(s => s.Card != null && !s.Card.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            });

        token.AddAbility(ability);
    }

    // --- Counter-unless-pay-{1} (CR 701.5 / CR 608.2b / CR 118.4) ----------

    private static void ResolveCounterActivation(
        ActivatedAbility? ability,
        Majik.Core.Stack.Stack? stack)
    {
        if (ability == null || stack == null) return;

        var spell = ResolveTargetSpell(ability);
        if (spell == null) return;

        if (ControllerPaidUnless(spell))
        {
            // Controller paid {1} — spell is NOT countered (CR 118.4).
            return;
        }

        // CR 701.5 — counter: remove from stack + send to graveyard.
        // CR 701.5b — uncounterable spells survive (RemoveFromStack returns false).
        if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
        spell.Card.SetZone(ZoneType.Graveyard);
    }

    private static ISpell? ResolveTargetSpell(ActivatedAbility ability)
    {
        var chosen = ability.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return null;
        if (chosen[0][0] is not ISpell spell) return null;

        // CR 608.2b — re-check legality at resolution. If the spell left the
        // stack or has become a creature spell (mode / type swap), the effect
        // does nothing for it (mirrors SpellPierceFactory).
        var targetCard = spell.Card as Card;
        if (targetCard == null) return null;
        if (targetCard.Zone != ZoneType.Stack) return null;
        if (targetCard.HasType(CardType.Creature)) return null;

        return spell;
    }

    private static bool ControllerPaidUnless(ISpell spell)
    {
        // CR 118.4 — the target's controller may pay {1}. v1 auto-pays when
        // mana is available (same posture as Spell Pierce / Daze / Mana Leak /
        // Mausoleum Wanderer).
        if (spell.Controller is null) return false;
        return spell.Controller.PayMana(ManaCost.Zero.AddGenericCost(UnlessPayN));
    }
}
