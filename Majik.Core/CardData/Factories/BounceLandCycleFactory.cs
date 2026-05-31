using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Parametric named-card factory for the 10-member Ravnica + Ravnica
/// Allegiance "bounce land" cycle (a.k.a. Karoos).
///
/// Each member shares the same oracle shape — only the produced colour
/// pair differs — so one factory class handles all ten:
/// <code>
/// [CardName("Azorius Chancery",   "W", "U")]
/// [CardName("Boros Garrison",     "R", "W")]
/// [CardName("Dimir Aqueduct",     "U", "B")]
/// [CardName("Golgari Rot Farm",   "B", "G")]
/// [CardName("Gruul Turf",         "R", "G")]
/// [CardName("Izzet Boilerworks",  "U", "R")]
/// [CardName("Orzhov Basilica",    "W", "B")]
/// [CardName("Rakdos Carnarium",   "B", "R")]
/// [CardName("Selesnya Sanctuary", "G", "W")]
/// [CardName("Simic Growth Chamber","G", "U")]
/// </code>
///
/// Args layout (forwarded by the source generator at dispatch time):
/// <c>[0] = printed card name</c>,
/// <c>[1] = produced mana colour A (single-letter Scryfall code)</c>,
/// <c>[2] = produced mana colour B (single-letter Scryfall code)</c>.
///
/// ## Oracle (canonical, all 10)
/// <code>
/// This land enters tapped.
/// When this land enters, return a land you control to its owner's hand.
/// {T}: Add {A}{B}.
/// </code>
///
/// ## Implemented (v1)
/// - <b>Land identity</b> — plain <see cref="Land"/>, no supertype, no
///   printed subtype (Karoos are non-basic, non-typed lands).
/// - <b>ETB tapped (CR 614.1c)</b> — registered as a
///   <see cref="ConditionalEntersTappedReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> with an always-false "enters untapped"
///   predicate; the bounce land enters tapped unconditionally. Mirrors
///   the unconditional-tapped posture of Bojuka Bog's
///   <see cref="EntersTappedReplacement"/> path, deliberately routed
///   through the conditional surface so the replacement composes cleanly
///   with any future "enters untapped unless …" rider primitives.
/// - <b>ETB bounce trigger (CR 603.6a)</b> — <see cref="TriggeredAbility"/>
///   wired off <see cref="Triggers.OnEnterBattlefieldSelf"/>. Resolution
///   asks <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> with
///   <see cref="BotIntent.Bounce"/> for one land the controller controls
///   (excluding this land — by the time the trigger resolves the bounce
///   land is on the battlefield, so the controller could in principle
///   pick it, but the printed effect is read as "another land you
///   control"; v1 enforces the not-self filter on the candidate list).
///   Deterministic first-fallback when no agent is registered (mirrors
///   <see cref="StoneforgeMysticFactory"/> / <see cref="AnnihilatorFactory"/>).
///   Empty candidate set is a clean no-op (CR 608.2b — no legal target →
///   nothing happens). Bounce routes through
///   <see cref="Primitives.Fx.BounceToHand"/>, which prefers
///   <see cref="ZoneService.MoveCard"/> when supplied so LTB triggers /
///   replacement effects on the returned land fire.
/// - <b>{T}: Add {A}{B}</b> — single <see cref="ManaAbility"/> producing
///   one of each colour (CR 605.1 — mana ability, never goes on the
///   stack). <see cref="ManaCost.Parse"/> accumulates single-letter
///   colour pips, so <c>Parse("WU")</c> yields one White + one Blue.
///
/// ## Deferred (v1 gaps)
/// - The printed oracle says "return a land you control to its owner's
///   hand" — strict reading allows the bouncing player to pick the
///   bounce land itself (since it's already on the battlefield at
///   resolution). Practical paper play and bot policy both target some
///   OTHER land. v1 filters out self from the candidate list to match
///   the standard interpretation; the agent surface remains as the
///   single hook to evolve this later.
/// - "If controller has no lands" — there's no legal pick at resolution.
///   v1 no-ops the trigger; the bounce land sits on the battlefield with
///   no other land returned (matches CR 608.2b — no legal target → no
///   effect).
/// </summary>
[CardName("Azorius Chancery",    "W", "U")]
[CardName("Boros Garrison",      "R", "W")]
[CardName("Dimir Aqueduct",      "U", "B")]
[CardName("Golgari Rot Farm",    "B", "G")]
[CardName("Gruul Turf",          "R", "G")]
[CardName("Izzet Boilerworks",   "U", "R")]
[CardName("Orzhov Basilica",     "W", "B")]
[CardName("Rakdos Carnarium",    "B", "R")]
[CardName("Selesnya Sanctuary",  "G", "W")]
[CardName("Simic Growth Chamber","G", "U")]
public static class BounceLandCycleFactory
{
    /// <summary>
    /// Fallback overload — only reachable when constructed by hand.
    /// Default-builds Azorius Chancery (W/U).
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, new[] { "Azorius Chancery", "W", "U" });

    /// <summary>
    /// Construct the bounce land identified by <paramref name="args"/>.
    /// Single-arg dispatcher path — no <see cref="ReplacementBus"/>,
    /// <see cref="TriggerManager"/>, or <see cref="ZoneService"/> wired.
    /// The ETB-tapped replacement is omitted (matches every other
    /// always-tapped factory's shape-only posture); the ETB bounce
    /// trigger is attached structurally but not registered with a
    /// trigger manager.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="args">
    /// Source-generator-provided args. Layout:
    /// <c>[0] = printed card name</c>,
    /// <c>[1] = produced mana colour A (single letter)</c>,
    /// <c>[2] = produced mana colour B (single letter)</c>.
    /// </param>
    public static Land Create(Player owner, string[] args) =>
        Create(owner, args, zoneService: null, eventBus: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct the bounce land identified by <paramref name="args"/>
    /// with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="args">See single-overload xmldoc.</param>
    /// <param name="zoneService">When supplied, the ETB bounce routes
    /// through <see cref="ZoneService.MoveCard"/> so LTB triggers /
    /// replacements on the returned land fire.</param>
    /// <param name="eventBus">Currently unused — kept symmetric with the
    /// other land factories so future event-emitting upgrades (reveal /
    /// targeting prompts) can plug in without changing the call shape.</param>
    /// <param name="triggers">When supplied, the ETB bounce trigger is
    /// registered so a battlefield <see cref="CardMovedEvent"/> places
    /// it on the stack automatically (CR 603.6a).</param>
    /// <param name="replacements">When supplied, the unconditional
    /// "enters tapped" replacement is registered (CR 614.1c).</param>
    public static Land Create(
        Player owner,
        string[] args,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 3)
        {
            throw new ArgumentException(
                $"BounceLandCycleFactory needs args = [name, colourA, colourB] (got {args.Length}).",
                nameof(args));
        }

        _ = eventBus; // reserved (see param docs)

        var cardName = args[0];
        var colourA = args[1];
        var colourB = args[2];

        // Non-basic land, no supertype, no printed subtype.
        var land = new Land(cardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // Enters tapped (CR 614.1c) — unconditional.
        // Predicate returns false ⇒ the replacement always flips
        // EntersTapped = true. Threaded through
        // ConditionalEntersTappedReplacement so the surface composes with
        // future "unless …" riders.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new ConditionalEntersTappedReplacement(
                land,
                entersUntappedIf: (_, _) => false));
        }

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this land enters, return a land you control to its
        //    owner's hand."
        // 1..1 target via the agent-prompt surface
        // (ChooseFromBattlefieldAsync with BotIntent.Bounce). Candidate
        // list = lands the controller currently controls, excluding the
        // bounce land itself. Empty candidate set → clean no-op
        // (CR 608.2b).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{cardName}: return a land you control to its owner's hand",
            async ctx =>
            {
                var controller = land.Controller ?? owner;

                var candidates = controller.Zones.Battlefield.GetCards()
                    .Where(c => c.HasType(CardType.Land) && !ReferenceEquals(c, land))
                    .ToList();
                if (candidates.Count == 0) return; // CR 608.2b — no legal pick.

                ICard? pick;
                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                if (agent != null)
                {
                    pick = (await agent.ChooseFromBattlefieldAsync(
                            controller,
                            candidates,
                            BotIntent.Bounce).ConfigureAwait(false));
                    // Re-validate the agent's pick at resolution (CR 608.2b).
                    if (pick == null
                        || !candidates.Contains(pick))
                    {
                        pick = candidates[0];
                    }
                }
                else
                {
                    // Deterministic v1 fallback — first land.
                    pick = candidates[0];
                }

                Fx.BounceToHand(pick, zoneService);
            });

        var etbTrigger = new TriggeredAbility(
            source: land,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(land),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}: Add {A}{B}
        // CR 605.1 — mana ability, no stack. ManaCost.Parse accumulates
        // single-letter colour pips so "WU" → one White + one Blue.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse(colourA + colourB)));

        return land;
    }
}
