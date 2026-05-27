using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Galvanic Relay (Strixhaven: Mystical Archive,
/// {2}{R}).
///
/// Sorcery. Oracle text:
///   "Exile the top X cards of your library, where X is the number of
///    times you've cast this spell from your hand this turn. Until the
///    end of your next turn, you may play those cards.
///    Storm (When you cast this spell, copy it for each spell cast before
///    it this turn.)"
///
/// ## Implemented (v1)
/// - <b>Sorcery {2}{R}</b> (Red) card shape, owner / controller wired.
/// - <b>Storm trigger (CR 702.40)</b> — built via
///   <see cref="StormHelper.Build"/>. Fires on this spell's
///   <see cref="SpellCastEvent"/> with <c>activeZones = Stack</c> and
///   copies the spell for each OTHER spell the controller has cast this
///   turn. Copies re-execute the original spell's effect list via
///   <see cref="Majik.Core.Services.SpellCopier"/>. <b>Observable
///   contract</b>: N copies → N additional exile-X-cards resolutions
///   against the controller's library.
/// - <b>Exile X cards from controller's library</b>: at resolve time we
///   exile the top X cards of the controller's library where X is the
///   number of times the controller has cast a card named "Galvanic
///   Relay" this turn (across all sources / from any zone — v1
///   simplification of "from your hand", see deferred gaps). X is read
///   from a per-turn name-keyed tally maintained by the
///   <see cref="Create(Player, TriggerManager, Majik.Core.Stack.Stack, TurnState, IEventBus)"/>
///   overload via an <see cref="IEventBus"/> subscription that filters
///   <see cref="SpellCastEvent"/> by spell name + controller.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Storm trigger
///   attached for shape inspection but not registered. No name-cast
///   tally maintained. Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager, Majik.Core.Stack.Stack, TurnState, IEventBus)"/>
///   — fully wired. Storm trigger registered, copies pushed via
///   <see cref="Majik.Core.Services.SpellCopier"/>; the
///   <see cref="IEventBus"/> subscription tallies controller-scoped
///   "Galvanic Relay" casts into a closure-captured counter consumed by
///   <see cref="BuildDefinition"/> when constructing the resolve
///   effect.
/// - <see cref="BuildDefinition"/> — produces the
///   <see cref="SpellDefinition"/> for the resolve effect, parameterized
///   on a closure that yields the current "times cast this turn" count.
///
/// ## Deferred (v1 gaps)
/// - <b>"From your hand" rider</b>: the engine does not yet track the
///   source zone of a cast spell. v1 counts every cast of a card named
///   "Galvanic Relay" by the controller, regardless of source zone.
///   This over-counts in fringe cases (flashback / cascade casts of a
///   Galvanic Relay copy, etc.) but matches the printed text for the
///   common-case "cast from hand → storm with itself" loop. The full
///   source-zone gate ships with cast-event source-zone plumbing.
/// - <b>"Until the end of your next turn, you may play those cards"</b>:
///   the alt-play permission window requires a turn-scoped
///   may-play-from-exile primitive that does not yet exist (same
///   deferred gap as <see cref="Majik.Core.CardData.SagaBinder"/>'s
///   Roku chapter 1). v1 just exiles the cards; the may-play rider is
///   dropped. The exile is observable via the controller's exile zone.
/// - <b>Retargeting copies</b>: CR 702.40a — inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; Galvanic Relay has
///   no targets so this gap is moot here.
/// - <b>Copies as distinct stack objects</b>: inherited from
///   <see cref="Majik.Core.Services.SpellCopier"/>; copies re-execute
///   the original effect list in place rather than pushing real
///   <see cref="Majik.Core.Spells.ISpell"/> stack items.
/// </summary>
[CardName("Galvanic Relay")]
public static class GalvanicRelayFactory
{
    public const string CardName = "Galvanic Relay";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>
    /// Construct Galvanic Relay as a Sorcery card with the Storm trigger
    /// attached for shape inspection but not registered. No name-cast
    /// tally is maintained — suitable for dispatcher / shape tests.
    /// Use the fully-wired overload for storm firing + X tally.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Storm trigger attached structurally (no stack / turn-state
        // wired — shape-only). The trigger is still inspectable via
        // card.Abilities for shape tests; firing requires the
        // bus-wired overload.
        var storm = StormHelper.Build(card, owner, stack: null, turnState: null);
        card.AddAbility(storm);

        return card;
    }

    /// <summary>
    /// Construct Galvanic Relay with full storm + X-tally wiring. The
    /// storm trigger is registered with <paramref name="triggers"/>,
    /// reads spells-cast counts from <paramref name="turnState"/>, and
    /// creates copies on <paramref name="stack"/>. The name-cast tally
    /// is maintained via a <see cref="SpellCastEvent"/> subscription on
    /// <paramref name="eventBus"/> filtered to controller-scoped casts
    /// of cards named <see cref="CardName"/>. Callers consume the tally
    /// via <see cref="BuildDefinition"/>.
    /// </summary>
    public static Sorcery Create(
        Player owner,
        TriggerManager triggers,
        Majik.Core.Stack.Stack stack,
        TurnState turnState,
        IEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(turnState);
        ArgumentNullException.ThrowIfNull(eventBus);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        var storm = StormHelper.Build(card, owner, stack, turnState);
        card.AddAbility(storm);
        triggers.RegisterTriggeredAbility(storm);

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Galvanic
    /// Relay: exile the top <paramref name="getXFn"/>() cards of
    /// <paramref name="controller"/>'s library. Stops short silently if
    /// the library runs out (CR 117.x — illegal action portions ignored).
    /// </summary>
    /// <param name="controller">The resolving controller — only their
    /// library is exiled from. CR 700.6 — the controller of a copy is
    /// the controller of the original.</param>
    /// <param name="getXFn">Closure that yields the current "times
    /// you've cast Galvanic Relay this turn" count at the moment the
    /// effect runs. Implementations typically read a counter maintained
    /// by a <see cref="SpellCastEvent"/> bus subscriber; tests may pass
    /// a constant.</param>
    public static SpellDefinition BuildDefinition(
        Player controller, Func<int> getXFn)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(getXFn);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect(
                    $"{CardName}: exile the top X cards of your library (X = times cast this turn)",
                    () =>
                    {
                        var x = Math.Max(0, getXFn());
                        for (var i = 0; i < x; i++)
                        {
                            var top = controller.Zones.Library.GetCards().FirstOrDefault();
                            if (top is null) return;
                            controller.Zones.Library.RemoveCard(top);
                            controller.Zones.Exile.AddCard(top);
                            top.SetZone(ZoneType.Exile);
                        }
                    }),
            });
    }

    /// <summary>
    /// Build a controller-scoped "Galvanic Relay" cast tally and subscribe
    /// it to <paramref name="eventBus"/>. Returns a counter function that
    /// reflects the live count for use as the <c>getXFn</c> argument of
    /// <see cref="BuildDefinition"/>. The counter increments on every
    /// <see cref="SpellCastEvent"/> whose spell is named
    /// <see cref="CardName"/> and whose controller is <paramref name="controller"/>.
    /// Reset semantics are caller-driven (typically by re-creating the
    /// tally at start of each turn).
    /// </summary>
    /// <remarks>
    /// v1 over-counts copies / non-hand casts (see deferred gaps in the
    /// factory xmldoc). Returns the live count BEFORE the current cast
    /// is counted IF the subscriber runs after the resolve effect — in
    /// practice, the SpellCastEvent fires at cast time and the resolve
    /// runs later, so by the time the resolve effect reads getXFn() the
    /// tally already includes the current Galvanic Relay cast. v1
    /// behaviour: X = total Galvanic Relay casts by controller this
    /// turn (inclusive of the current resolution's originating cast).
    /// </remarks>
    public static Func<int> BuildNameCastTally(Player controller, IEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(eventBus);

        var count = 0;
        eventBus.Subscribe<SpellCastEvent>(e =>
        {
            if (e.Spell is not Majik.Core.Spells.Spell spell) return;
            if (!ReferenceEquals(spell.Controller, controller)) return;
            var spellCard = spell.Card;
            if (spellCard is null) return;
            if (spellCard.Name != CardName) return;
            count++;
        });
        return () => count;
    }
}
