using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Oblivion Sower (Battle for Zendikar, {6}).
///
/// Creature — Eldrazi 5/8. Oracle text (verified against Scryfall 2026-05-29):
///   "When you cast this spell, target opponent exiles the top four cards of
///    their library, then you may put any number of land cards that player
///    owns from exile onto the battlefield under your control."
///
/// ## Shape
/// The base card shape (name / Creature — Eldrazi / {6} / 5/8) is
/// materialised from the embedded JSON definition (<c>oblivion-sower.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single triggered ability is
/// layered on here because the JSON <c>AbilityDefinition</c> schema does not
/// express this "cast trigger → exile-N then conditional control-stealing
/// put-onto-battlefield" effect yet (same posture as
/// <see cref="ArdentPleaFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Cast trigger (CR 702.85-style "When you cast this spell")</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> keyed on
///   <c>ReferenceEquals(e.Spell.Card, card)</c>, with
///   <see cref="TriggeredAbility.ActiveZones"/> = { Stack } so it fires while
///   Oblivion Sower is on the stack as a spell (CR 601.2). Same shape as the
///   Cascade analogues (<see cref="ArdentPleaFactory"/> / Bloodbraid Elf).
/// - <b>Exile step (CR 701.21)</b>: the chosen target opponent exiles the top
///   four cards of their library (Library → Exile raw zone move, mirroring
///   <see cref="AmpedRaptorFactory.ResolveEtb"/>). Fewer than four if the
///   library is short — "the top N cards" never throws.
/// - <b>Land-steal step (CR 702.x put-onto-battlefield, CR 110.2 control)</b>:
///   from the cards now in the opponent's exile, the controller "may put any
///   number of land cards that player owns from exile onto the battlefield
///   under your control." When a <see cref="ZoneService"/> is supplied the
///   move funnels through <see cref="ZoneService.MoveCard"/> with the
///   controller as the new controller — owner stays the opponent, controller
///   becomes Oblivion Sower's controller (CR 110.2 — a permanent's owner is
///   the player who started the game with it in their deck; its controller is
///   set by the put-onto-battlefield effect). ETB triggers / replacements on
///   the lands fire (CR 603.6a / CR 614). With no service the move falls back
///   to raw zone manipulation (test path), still setting controller.
///
/// ## Choices (resolution-time)
/// - <c>chooseLands</c>: from the land cards the opponent owns in exile (the
///   four just exiled — and only those, see below), pick the subset to put
///   onto the battlefield. "any number" — the default takes ALL eligible land
///   cards; a custom picker returns the subset (empty = decline the "may",
///   CR 605.1).
/// - The eligible pool is restricted to the cards THIS resolution exiled
///   (<see cref="Result.Exiled"/>) — the printed clause "land cards that
///   player owns from exile" technically reads the whole exile zone, but
///   scoping to the cards we just exiled matches the card's intent (the cards
///   it just put there) and avoids stealing lands an unrelated effect parked
///   in exile. This is the conservative reading every "put from exile" hook
///   in this repo takes.
///
/// ## Single-arg dispatcher path
/// <see cref="Create(Player)"/> attaches the trigger structurally with no
/// opponent / service / picker wiring, so the trigger body is a no-op when
/// invoked. Suitable for shape / <see cref="NamedCardFactory"/> dispatch tests.
/// Production callers use the full overload and drive
/// <see cref="ResolveCastTrigger"/>.
/// </summary>
[CardName("Oblivion Sower")]
public static class OblivionSowerFactory
{
    public const string CardName = "Oblivion Sower";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "oblivion-sower";

    public const string PrintedManaCost = "{6}";
    public const int Power = 5;
    public const int Toughness = 8;
    public const int ExileCount = 4;

    /// <summary>
    /// Outcome of the cast trigger. <see cref="Exiled"/> is every card the
    /// opponent moved Library → Exile (top of library first).
    /// <see cref="EligibleLands"/> is the subset of those that are land cards
    /// (the "any number of land cards that player owns from exile" pool).
    /// <see cref="Stolen"/> is the subset actually put onto the battlefield
    /// under the controller's control.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<ICard> Exiled,
        IReadOnlyList<ICard> EligibleLands,
        IReadOnlyList<ICard> Stolen);

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches the cast trigger structurally; the body is a no-op (no
    /// opponent / service wired). No TriggerManager wiring.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, opponentSelector: null,
            zoneService: null, chooseLands: null, onResolved: null);

    /// <summary>
    /// Fully-wired construction.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the cast trigger
    /// against. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="opponentSelector">Closure returning the chosen "target
    /// opponent" (CR 115 — target chosen as the trigger goes on the stack).
    /// May be null — body is a no-op without a target.</param>
    /// <param name="zoneService">Optional zone service. When supplied the
    /// exile → battlefield move funnels through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers / replacements on
    /// the stolen lands fire (CR 603.6a / CR 614) and the control change is
    /// applied.</param>
    /// <param name="chooseLands">Picker invoked with the eligible land pile;
    /// returns the subset to put onto the battlefield ("any number"). Default
    /// = put all eligible lands. Empty subset declines the "may".</param>
    /// <param name="onResolved">Receives the <see cref="Result"/> after the
    /// trigger resolves. Tests use it to observe the resolution.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<Player?>? opponentSelector,
        ZoneService? zoneService = null,
        Func<IReadOnlyList<ICard>, IReadOnlyList<ICard>>? chooseLands = null,
        Action<Result>? onResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Creature — Eldrazi / {6} / 5/8) from embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // CR 702.85-style cast trigger — "When you cast this spell, …".
        // Keyed on ReferenceEquals(e.Spell.Card, card); ActiveZones =
        // { Stack } so it is live while Oblivion Sower is on the stack as a
        // spell. Same shape as the Cascade analogues.
        // ----------------------------------------------------------------
        var castEffect = new Effect(
            $"{CardName} — target opponent exiles top {ExileCount}, then you may put any " +
            "number of land cards that player owns from exile onto the battlefield under your control",
            () =>
            {
                var opponent = opponentSelector?.Invoke();
                if (opponent == null) return; // no target → no-op (CR 608.2b)

                var result = ResolveCastTrigger(
                    controller: card.Controller ?? owner,
                    opponent: opponent,
                    zoneService: zoneService,
                    chooseLands: chooseLands);
                onResolved?.Invoke(result);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>(
                (e, _) => ReferenceEquals(e.Spell.Card, card)),
            effects: new IEffect[] { castEffect },
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }

    /// <summary>
    /// Execute Oblivion Sower's cast-trigger body: <paramref name="opponent"/>
    /// exiles the top four cards of their library, then <paramref name="controller"/>
    /// may put any number of land cards the opponent owns (from those just
    /// exiled) onto the battlefield under their control.
    ///
    /// Public so tests and bots can drive the resolution without going through
    /// TriggerManager. Always exiles up to <see cref="ExileCount"/> cards
    /// (fewer if the library is short, CR 701.21), then asks
    /// <paramref name="chooseLands"/> which land cards to steal (default =
    /// all eligible).
    /// </summary>
    public static Result ResolveCastTrigger(
        Player controller,
        Player opponent,
        ZoneService? zoneService = null,
        Func<IReadOnlyList<ICard>, IReadOnlyList<ICard>>? chooseLands = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(opponent);
        chooseLands ??= static lands => lands;

        var library = opponent.Zones.Library;
        var exile = opponent.Zones.Exile;

        // CR 701.21 — opponent exiles the top N (or fewer if short).
        var exiled = new List<ICard>(ExileCount);
        for (int i = 0; i < ExileCount; i++)
        {
            var top = library.GetCards().FirstOrDefault();
            if (top == null) break;

            library.RemoveCard(top);
            exile.AddCard(top);
            top.SetZone(ZoneType.Exile);
            exiled.Add(top);
        }

        // "land cards that player owns from exile" — scope to what we just
        // exiled (the conservative reading; see class doc). CR 110.4a — land
        // cards.
        var eligibleLands = exiled.Where(c => c.HasType(CardType.Land)).ToList();

        // "you may put any number" — controller picks the subset (CR 605.1
        // decline = empty).
        var chosen = eligibleLands.Count == 0
            ? Array.Empty<ICard>()
            : (chooseLands(eligibleLands) ?? Array.Empty<ICard>());

        var stolen = new List<ICard>(chosen.Count);
        foreach (var land in chosen)
        {
            // Defensive — only put cards the engine actually deemed eligible
            // and that are still in exile (a mis-wired picker would otherwise
            // route a card the rules never allowed).
            if (!eligibleLands.Contains(land)) continue;
            if (land.Zone != ZoneType.Exile) continue;

            // CR 110.2 — owner stays the opponent; controller becomes
            // Oblivion Sower's controller. ZoneService.MoveCard sets the new
            // controller on a Battlefield destination and fires ETB
            // triggers / replacements (CR 603.6a / CR 614).
            if (zoneService != null)
            {
                zoneService.MoveCard(land, ZoneType.Exile, ZoneType.Battlefield, controller);
            }
            else
            {
                exile.RemoveCard(land);
                controller.Zones.Battlefield.AddCard(land);
                land.SetZone(ZoneType.Battlefield);
                land.SetController(controller);
            }

            stolen.Add(land);
        }

        return new Result(
            Exiled: exiled,
            EligibleLands: eligibleLands,
            Stolen: stolen);
    }
}
