using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Basking Broodscale (Modern Horizons 3,
/// <c>{1}{G}</c>). Creature — Eldrazi Lizard. 2/2.
///
/// Oracle text (Scryfall-verified):
///   "Devoid (This card has no color.)
///    <c>{1}{G}</c>: Adapt 1. (If this creature has no +1/+1 counters on
///    it, put a +1/+1 counter on it.)
///    Whenever one or more +1/+1 counters are put on this creature, you
///    may create a 0/1 colorless Eldrazi Spawn creature token with
///    \"Sacrifice this token: Add {C}.\""
///
/// The card's base shape (name, Creature, Eldrazi + Lizard subtypes,
/// <c>{1}{G}</c>, 2/2) is materialised from the embedded JSON definition
/// (<c>basking-broodscale.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed
/// behaviours (Devoid, the Adapt activated ability, and the
/// counters-placed token trigger) are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express keyword markers,
/// activated Adapt, or event triggers, so they live in the factory (same
/// posture as <see cref="StormscaleScionFactory"/> and the other
/// JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// <list type="bullet">
///   <item><b>Devoid (CR 702.114)</b> — stamps <see cref="Card.SetDevoid"/>
///   so <see cref="Majik.Core.Cards.CardColors"/> reports colourless
///   despite the <c>{G}</c> pip, plus a <see cref="KeywordAbility"/>
///   marker (same posture as <see cref="WrithingChrysalisFactory"/> /
///   Sowing Mycospawn).</item>
///
///   <item><b><c>{1}{G}</c>: Adapt 1 (CR 702.116)</b> — delegates to
///   <see cref="AdaptFactory.Build"/> with cost <c>{1}{G}</c>, N=1. The
///   helper handles the CR 702.116b "no +1/+1 counters" gate, stamps the
///   "Adapt 1" keyword marker, and routes the placement through
///   <see cref="CountersService.Add"/> so replacement effects (Hardened
///   Scales / Doubling Season) modify the count AND the post-commit
///   <see cref="CounterAddedEvent"/> publishes — which the token trigger
///   below subscribes to. Identical wiring to
///   <see cref="EmperorOfBonesFactory"/>'s Adapt 2.</item>
///
///   <item><b>Counters-placed trigger (CR 603.6c)</b> — "Whenever one or
///   more +1/+1 counters are put on this creature, you may create a 0/1
///   colorless Eldrazi Spawn creature token …" A
///   <see cref="TriggeredAbility"/> over a self-scoped
///   <see cref="CounterAddedEvent"/> (matches THIS card +
///   <see cref="CounterType.PlusOnePlusOne"/> — same condition shape as
///   Emperor of Bones' ability 3). Fires once per
///   <see cref="CountersService.Add"/> call regardless of how many
///   counters landed (the printed "one or more" floor is implicit — the
///   service only publishes when amount &gt; 0). On resolve the "you may"
///   (CR 117.5) is offered to the controller's
///   <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/>; v1 falls back to
///   "auto-create" (yes) when no agent is registered, since the token is
///   a pure-upside option with no cost (Animation Module's auto-pay
///   posture). The token is the shared 0/1 colourless Eldrazi Spawn from
///   <see cref="TokenFactory.CreateEldraziSpawn"/>.</item>
/// </list>
///
/// ## Wiring overloads
/// <list type="bullet">
///   <item><see cref="Create(Player)"/> — shape only; the trigger is
///   attached for shape / dispatcher tests but not registered with any
///   <see cref="TriggerManager"/>, and the Adapt ability has no live
///   <see cref="ReplacementBus"/> / <see cref="IEventBus"/>, so its
///   placement won't surface a <see cref="CounterAddedEvent"/>.</item>
///   <item><see cref="Create(Player, ZoneService?, TriggerManager?, ReplacementBus?, IEventBus?)"/>
///   — fully wired; the Adapt placement routes through the replacement +
///   event buses so the counters-placed trigger fires, and the Spawn
///   token's ETB routes through <paramref name="zones"/> so its
///   <see cref="CardMovedEvent"/> publishes.</item>
/// </list>
///
/// ## Deferred (v1 gaps)
/// <list type="bullet">
///   <item><b>"Sacrifice this token: Add {C}." cost</b> on the Eldrazi
///   Spawn token: <see cref="ManaAbility"/> doesn't carry a sac cost yet
///   (same gap as Eldrazi Skyspawner's Scion / Writhing Chrysalis /
///   Treasure / Food). The Spawn produces {C} without enforcing the
///   sacrifice — see <see cref="TokenFactory.CreateEldraziSpawn"/>.</item>
/// </list>
/// </summary>
[CardName("Basking Broodscale")]
public static class BaskingBroodscaleFactory
{
    public const string CardName = "Basking Broodscale";
    public const string Slug = "basking-broodscale";
    public const string AdaptCost = "{1}{G}";
    public const int AdaptAmount = 1;

    private const string DevoidKeyword = "Devoid";

    /// <summary>
    /// Construct Basking Broodscale with no live wiring. The
    /// counters-placed trigger is attached for shape observability but not
    /// registered with any <see cref="TriggerManager"/>; the Adapt ability
    /// has no replacement / event bus, so its placement won't publish a
    /// <see cref="CounterAddedEvent"/>. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Basking Broodscale with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Eldrazi Spawn token's ETB
    /// routes through <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="CardMovedEvent"/> publishes for zone-change
    /// subscribers.</param>
    /// <param name="triggers">When supplied, the counters-placed trigger
    /// registers so a qualifying <see cref="CounterAddedEvent"/>
    /// automatically queues it (CR 603.2).</param>
    /// <param name="replacements">Routed through
    /// <see cref="CountersService.Add"/> for the Adapt placement (Hardened
    /// Scales / Doubling Season bumps).</param>
    /// <param name="eventBus">Publishes the <see cref="CounterAddedEvent"/>
    /// that the token trigger listens for. When null, Adapt still places
    /// the counter but no event surfaces (suitable for shape tests).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi + Lizard subtypes, {1}{G}, 2/2). The JSON carries no
        // abilities — Devoid / Adapt / the token trigger are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        card.SetController(owner);

        // CR 702.114 — Devoid. Stamp IsDevoid so CardColors.GetColors
        // returns empty despite the {G} pip, plus a keyword marker for
        // scans (same posture as Writhing Chrysalis / Sowing Mycospawn).
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        // CR 702.116 — "{1}{G}: Adapt 1." Delegate to AdaptFactory. The
        // helper stamps the "Adapt 1" keyword marker, enforces the
        // "no +1/+1 counters" gate at resolution time (CR 702.116b), and
        // routes the placement through CountersService.Add so the
        // post-commit CounterAddedEvent publishes — which the token
        // trigger below subscribes to.
        var adaptAbility = AdaptFactory.Build(
            card, AdaptCost, AdaptAmount, replacements, eventBus);
        card.AddAbility(adaptAbility);

        // ----------------------------------------------------------------
        // Counters-placed trigger — CR 603.6c.
        //   "Whenever one or more +1/+1 counters are put on this creature,
        //    you may create a 0/1 colorless Eldrazi Spawn creature token
        //    with \"Sacrifice this token: Add {C}.\""
        //
        // Self-scoped CounterAddedEvent (THIS card + +1/+1 counters). Fires
        // once per CountersService.Add call — the printed "one or more"
        // floor is implicit (the service only publishes when amount > 0).
        // Same condition shape as Emperor of Bones' ability 3.
        // ----------------------------------------------------------------
        var tokenEffect = new Effect(
            $"{CardName}: you may create a 0/1 colourless Eldrazi Spawn creature token with \"Sacrifice this token: Add {{C}}.\"",
            () =>
            {
                // CR 603.6c — the source need not still be on the
                // battlefield for this trigger (it's not a leaves-the-
                // battlefield trigger), but the token is created under the
                // controller's control regardless.
                var controller = card.Controller ?? owner;

                // "You may create …" (CR 117.5) — consult the controller's
                // agent. v1 falls back to "auto-create" (yes) when no agent
                // is registered, since the token is a pure-upside option
                // with no cost (Animation Module's auto-pay posture).
                var agent = AgentRegistry.Get(controller);
                bool create = agent == null
                    || agent.ChooseYesNoAsync(
                        "Create a 0/1 colorless Eldrazi Spawn creature token?",
                        BotIntent.Token).GetAwaiter().GetResult();

                if (!create) return;

                // CR 111.10 — 0/1 colourless Eldrazi Spawn with the
                // (deferred-cost) "Sacrifice this token: Add {C}." mana
                // ability. Shared helper — see class xmldoc gap note.
                TokenFactory.CreateEldraziSpawn(controller, zones);
            });

        var tokenTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CounterAddedEvent>(
                (e, _) => ReferenceEquals(e.Target, card)
                          && e.CounterType == CounterType.PlusOnePlusOne),
            effects: new IEffect[] { tokenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(tokenTrigger);
        triggers?.RegisterTriggeredAbility(tokenTrigger);

        return card;
    }
}
