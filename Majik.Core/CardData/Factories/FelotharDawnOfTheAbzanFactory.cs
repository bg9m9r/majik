using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Felothar, Dawn of the Abzan (Tarkir: Dragonstorm,
/// {W}{B}{G}).
///
/// Legendary Creature — Human Warrior 3/3. Oracle text (verified against
/// Scryfall, 2026-06-24):
///   "Trample
///    Whenever Felothar enters or attacks, you may sacrifice a nonland
///    permanent. When you do, put a +1/+1 counter on each creature you
///    control."
///
/// The base shape (name, Legendary supertype, Creature, Human + Warrior
/// subtypes, {W}{B}{G}, 3/3) is materialised from the embedded JSON definition
/// (<c>felothar-dawn-of-the-abzan.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Trample keyword + the two
/// enters-or-attacks reflexive-sacrifice triggers are layered on here — the
/// JSON <c>AbilityDefinition</c> schema doesn't express keyword lines or
/// reflexive sacrifice riders (same posture as
/// <see cref="OverlordOfTheBoilerbilgesFactory"/> for the dual ETB/attack
/// trigger and <see cref="CabalTherapistFactory"/> for the optional-sacrifice
/// "When you do, …" reflexive clause).
///
/// ## Implemented (v1)
/// - <b>Trample</b> (CR 702.19) — a <see cref="KeywordAbility"/> marker
///   consumed by combat damage assignment.
/// - <b>Enters-or-attacks reflexive sacrifice trigger
///   (CR 603.1 ETB + CR 508.1f attack + CR 603.2.2 reflexive)</b>: two
///   <see cref="TriggeredAbility"/> instances sharing one effect closure — one
///   gated on <see cref="Triggers.OnEnterBattlefieldSelf"/>, one on
///   <see cref="Triggers.OnAttackSelf"/> (same dual-trigger shape as
///   <see cref="OverlordOfTheBoilerbilgesFactory"/>). At resolution each:
///     1. <b>"you may sacrifice a nonland permanent"</b> — prompts the
///        controller's agent yes/no (CR 601.2b — an optional action). On "yes",
///        picks one of the controller's nonland permanents (CR 109.5 — "you
///        control"; the land filter is CR 305.1 — a Land card is not a legal
///        sacrifice here) via <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>
///        and sacrifices it through <see cref="Fx.Sacrifice(ICard, Player, IEventBus)"/>
///        (publishes the CR 701.16 <see cref="PermanentSacrificedEvent"/> so
///        aristocrat payoffs observe it). A decline OR no nonland permanents to
///        sacrifice skips the reflexive clause entirely (CR 603.2.2).
///     2. <b>"When you do, put a +1/+1 counter on each creature you control."</b>
///        Only runs because a permanent was actually sacrificed (CR 603.2.2).
///        The controller's battlefield is scanned for creatures (CR 608.2 —
///        current game state, so a creature that left the battlefield as the
///        sacrifice fodder is no longer present and Felothar itself receives a
///        counter), and each gets one +1/+1 counter via
///        <see cref="CountersService.Add"/> so Hardened Scales / Doubling Season
///        replacements observe the placement (CR 614 / CR 121.2) and the
///        <see cref="CounterAddedEvent"/> fires for counters-matter payoffs.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Trample + both triggers are
///   attached for shape inspection but the triggers are NOT registered with a
///   <see cref="TriggerManager"/>, and no agent / event bus / replacement bus is
///   threaded (the reflexive sacrifice no-ops at resolution without an agent,
///   honouring the "you may" default-decline). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, IEventBus?, ReplacementBus?)"/>
///   — fully wired. <paramref name="triggers"/> registers the ETB + attack
///   triggers; <paramref name="eventBus"/> publishes the sacrifice /
///   counter-added events; <paramref name="replacements"/> lets the +1/+1
///   counters be rewritten by replacement effects.
///
/// ## Deferred (v1 gaps)
/// - <b>Reflexive trigger as a separate stack object</b>: CR 603.2.2 puts the
///   "When you do, …" clause on the stack as its OWN triggered ability — an
///   opponent gets a response window between the sacrifice and the counters.
///   v1 resolves both in the same trigger resolution (no intervening window),
///   the same posture as <see cref="CabalTherapistFactory"/>.
/// </summary>
[CardName(CardName)]
public static class FelotharDawnOfTheAbzanFactory
{
    public const string CardName = "Felothar, Dawn of the Abzan";
    public const string Slug = "felothar-dawn-of-the-abzan";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Felothar with no live wiring. Trample + both enters/attacks
    /// triggers are attached for shape / dispatcher tests; the triggers are not
    /// registered with a <see cref="TriggerManager"/> and no agent / buses are
    /// threaded (the reflexive sacrifice defaults to declining without an
    /// agent). This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Felothar, Dawn of the Abzan with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB + attack triggers are
    /// registered so the matching events queue their abilities on the stack
    /// automatically.</param>
    /// <param name="eventBus">When supplied the sacrifice publishes the
    /// CR 701.16 <see cref="PermanentSacrificedEvent"/> and the counter
    /// placements publish <see cref="CounterAddedEvent"/>.</param>
    /// <param name="replacements">When supplied the +1/+1 counter placements
    /// route through <see cref="CountersService.Add"/> so Hardened Scales /
    /// Doubling Season can rewrite the count (CR 614).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus = null,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Human + Warrior, {W}{B}{G}, 3/3). No abilities in the JSON —
        // Trample + the enters-or-attacks reflexive trigger are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample keyword marker.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Enters-or-attacks reflexive sacrifice — CR 603.1 / CR 508.1f /
        // CR 603.2.2.
        //   "Whenever Felothar enters or attacks, you may sacrifice a nonland
        //    permanent. When you do, put a +1/+1 counter on each creature you
        //    control."
        // Two triggered abilities share one effect closure; one gated on the
        // ETB event, one on the attack event.
        // ----------------------------------------------------------------
        var etbTrigger = BuildTrigger(
            card, owner, eventBus, replacements,
            Triggers.OnEnterBattlefieldSelf(card),
            $"{CardName}: enters — may sacrifice a nonland permanent, then +1/+1 to each creature");
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        var attackTrigger = BuildTrigger(
            card, owner, eventBus, replacements,
            Triggers.OnAttackSelf(card),
            $"{CardName}: attacks — may sacrifice a nonland permanent, then +1/+1 to each creature");
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Build one enters-or-attacks reflexive triggered ability. At resolution:
    /// prompt the controller's agent to sacrifice a nonland permanent they
    /// control (CR 601.2b / CR 109.5); on an actual sacrifice (CR 701.16) put a
    /// +1/+1 counter on each creature the controller controls (CR 603.2.2 /
    /// CR 614). The closure carries no <see cref="TargetRequest"/> — the printed
    /// text targets nothing (the reflexive "each creature you control" is a mass
    /// effect, not a target).
    /// </summary>
    private static TriggeredAbility BuildTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        ReplacementBus? replacements,
        ITriggerCondition condition,
        string label)
    {
        var effect = new Effect(label, async ctx =>
        {
            if (card.Zone != ZoneType.Battlefield) return;
            var controller = card.Controller ?? owner;

            var agent = ctx.Agent ?? AgentRegistry.Get(controller);
            if (agent == null) return; // no decision-maker → "you may" defaults to declining.

            // 1. "you may sacrifice a nonland permanent" (CR 601.2b — optional;
            //    the reflexive clause is gated behind actually taking it). CR
            //    305.1 — a Land card is not a legal sacrifice for "nonland
            //    permanent"; the controller's tokens / nonland cards qualify
            //    (CR 109.5 — "you control"), including Felothar itself.
            var fodder = controller.Zones.Battlefield.GetCards()
                .Where(c => !c.HasType(CardType.Land))
                .OfType<ICard>()
                .ToList();
            if (fodder.Count == 0) return; // nothing to sacrifice → the "may" can't be taken.

            var wantsTo = await agent
                .ChooseYesNoAsync(ctx.Game, "Sacrifice a nonland permanent?", CardName, ctx.Ct)
                .ConfigureAwait(false);
            if (!wantsTo) return;

            var chosen = await agent
                .ChooseFromBattlefieldAsync(controller, fodder, BotIntent.None, ctx.Ct)
                .ConfigureAwait(false);
            if (chosen is not Permanent sacrificed) return;

            // CR 701.16 — sacrifice the chosen permanent. Use the
            // event-publishing overload when a bus is present so the
            // PermanentSacrificedEvent fires (aristocrat payoffs observe it).
            if (eventBus != null)
            {
                Fx.Sacrifice(sacrificed, controller, eventBus);
            }
            else
            {
                Fx.Sacrifice(sacrificed);
            }

            // 2. "When you do, put a +1/+1 counter on each creature you control."
            //    CR 603.2.2 — only runs because a permanent was actually
            //    sacrificed. CR 608.2 — scan the CURRENT battlefield (a creature
            //    used as the sacrifice fodder is already gone), each gets one
            //    +1/+1 counter via CountersService.Add (CR 614 / CR 121.2).
            foreach (var creature in controller.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                CountersService.Add(
                    creature, CounterType.PlusOnePlusOne, 1, replacements, eventBus);
            }
        });

        return new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });
    }
}
