using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Etali, Primal Storm (Rivals of Ixalan, {4}{R}).
///
/// Legendary Creature — Elder Dinosaur 6/6. Oracle text:
///   "Whenever Etali, Primal Storm attacks, exile the top card of each
///    player's library, then you may cast any number of nonland cards
///    from among them without paying their mana costs."
///
/// ## Implemented (v1)
/// - 6/6 Legendary Creature — Elder Dinosaur, mana cost {4}{R}.
/// - <b>Attack triggered ability (CR 603.1 / 508.1f)</b>: fires on
///   <see cref="CreatureAttacksEvent"/> when Etali itself attacks. On
///   resolution:
///     1. Exile the top card of each player's library (CR 701.21 — moves
///        Library → Exile, mirroring <see cref="AmpedRaptorFactory"/>'s
///        exile shape). Empty libraries are skipped (CR 701.21 — "the
///        top card" never throws when none exists). Each exiled card
///        lands in its owner's exile zone — per-player exile in this
///        engine models the shared MTG exile zone (CR 406.1); the
///        free-cast routing reads from the owner-keyed exile via
///        <see cref="CastFromExileAlternativeCost"/> regardless of who
///        casts.
///     2. Filter the exiled pile to nonland cards — the candidate pool
///        for the controller's "may cast" choice (CR 305.1 — Land
///        excluded).
///     3. Ask the supplied <c>chooseSpells</c> picker which nonland
///        candidates (if any) to cast. Picker returns the subset to
///        cast; default = cast every nonland in the pile (auto-accept
///        "any number"). Tests / bots override.
///     4. Invoke the supplied <c>onAttackResolved</c> callback with the
///        <see cref="Result"/> so the host can drive the free casts
///        through <see cref="SpellCastFlow"/> +
///        <see cref="CastFromExileAlternativeCost"/>. Remaining exiled
///        cards stay in exile (printed oracle has no "return them"
///        clause — same posture as <see cref="AmpedRaptorFactory"/>).
///
/// ## Source closure injection
/// The engine doesn't yet expose a global "all players" enumerator from
/// inside an effect closure (mirrors <see cref="EngineeredExplosivesFactory"/>'s
/// activated-ability gap), so the factory accepts a
/// <c>Func&lt;IReadOnlyList&lt;Player&gt;&gt;</c> closure that callers
/// (Game / tests) populate with the live player list. When null, the
/// effect scans only the controller's library (degraded behaviour,
/// matching the same posture as EE).
///
/// ## Deferred (v1 gaps)
/// - <b>Auto-routed free cast</b>: the attack trigger does not own a
///   <see cref="SpellCastFlow"/> reference. Production code wires the
///   cast via the <c>onAttackResolved</c> callback (same posture as
///   <see cref="AmpedRaptorFactory"/> / <see cref="CrashingFootfallsFactory"/>).
///   When the engine grows a per-trigger SpellCastFlow injection point
///   this collapses to inline.
/// - <b>Trigger-on-stack timing</b>: the exile + may-cast resolution
///   runs synchronously when the effect fires. Real MTG semantics put
///   the trigger on the stack before blockers are declared; v1
///   collapses this. Observationally identical for the exile pile.
/// - <b>"Any number" prompt</b>: the picker collapses to a subset
///   choice; the agent-driven UI for "cast any number" lives in the
///   broader bot/agent layer that consumes the
///   <see cref="Result.Eligible"/> list. Default picker accepts every
///   eligible card.
/// - <b>Tribal / Battle / future card types</b>: the nonland filter
///   uses <see cref="CardType.Land"/> only — every other type is
///   castable in principle. Whether a "spell" can be cast at sorcery
///   speed in mid-combat depends on each spell's own rules (Flash etc.);
///   that gating lives in <see cref="SpellCastFlow"/>, not this factory.
/// </summary>
[CardName("Etali, Primal Storm")]
public static class EtaliPrimalStormFactory
{
    public const string CardName = "Etali, Primal Storm";
    public const string PrintedManaCost = "{4}{R}";
    public const int Power = 6;
    public const int Toughness = 6;

    /// <summary>
    /// Outcome of the attack-trigger resolution.
    /// <see cref="Exiled"/> is every card moved Library → Exile (one per
    /// player who had cards left). <see cref="Eligible"/> is the subset
    /// filtered to nonland cards — the "may cast" candidate pool.
    /// <see cref="Picked"/> is the subset the controller chose to cast;
    /// the caller drives the actual free casts via
    /// <see cref="CastFromExileAlternativeCost"/> + <see cref="SpellCastFlow"/>.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<ICard> Exiled,
        IReadOnlyList<ICard> Eligible,
        IReadOnlyList<ICard> Picked);

    /// <summary>
    /// Construct Etali, Primal Storm with no runtime services. The attack
    /// trigger is attached for shape inspection but is not registered
    /// with a TriggerManager and has no free-cast routing. Suitable for
    /// dispatcher / shape-only tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner,
            triggers: null,
            allPlayersResolver: null,
            chooseSpells: null,
            onAttackResolved: null);

    /// <summary>
    /// Construct Etali, Primal Storm with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the attack trigger is
    /// registered so a <see cref="CreatureAttacksEvent"/> for this card
    /// lands on the stack automatically.</param>
    /// <param name="allPlayersResolver">Closure returning every player
    /// at trigger-resolution time. When null, only the controller's
    /// library is exiled from (degraded behaviour).</param>
    /// <param name="chooseSpells">Picker invoked with the eligible
    /// nonland pile. Returns the subset to cast for free. Default =
    /// every eligible card (auto-accept "any number"). Tests / bots
    /// override.</param>
    /// <param name="onAttackResolved">Callback invoked with the
    /// <see cref="Result"/> after exile + pick. Production callers use
    /// this to drive the free casts of <see cref="Result.Picked"/> via
    /// <see cref="CastFromExileAlternativeCost"/> + <see cref="SpellCastFlow"/>.
    /// Tests use it to observe resolution.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        Func<IReadOnlyList<ICard>, IReadOnlyList<ICard>>? chooseSpells = null,
        Action<Result>? onAttackResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        chooseSpells ??= static pile => pile;

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Elder, CardSubtype.Dinosaur });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 603.1 / 508.1f — "Whenever ~ attacks, exile the top card of
        // each player's library, then you may cast any number of nonland
        // cards from among them without paying their mana costs."
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: exile top of each library, may cast nonland cards for free",
            () =>
            {
                var result = ResolveAttack(owner, allPlayersResolver, chooseSpells);
                onAttackResolved?.Invoke(result);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Execute Etali's attack-trigger body against the supplied player set.
    /// Public so tests and bots can drive resolution without going through
    /// TriggerManager. Exiles the top card of each player's library
    /// (skipping empty libraries), builds the eligible nonland pile, and
    /// asks <paramref name="chooseSpells"/> which cards to flag for free
    /// cast. The remaining exiled cards stay in exile (printed oracle —
    /// no return-to-library step).
    /// </summary>
    public static Result ResolveAttack(
        Player controller,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        Func<IReadOnlyList<ICard>, IReadOnlyList<ICard>>? chooseSpells = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        chooseSpells ??= static pile => pile;

        var players = allPlayersResolver?.Invoke()
            ?? (IReadOnlyList<Player>)new[] { controller };

        var exiled = new List<ICard>(players.Count);
        foreach (var p in players)
        {
            if (p == null) continue;

            // CR 701.21 — exile top card. Empty library = skip.
            var top = p.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) continue;

            p.Zones.Library.RemoveCard(top);
            // Exile to the card's owner's exile zone — per-player exile
            // in this engine models the shared MTG exile zone (CR 406.1).
            p.Zones.Exile.AddCard(top);
            top.SetZone(ZoneType.Exile);
            exiled.Add(top);
        }

        // Candidate pool — every nonland card in the exile pile (CR 305.1).
        var eligible = exiled.Where(c => !c.HasType(CardType.Land)).ToList();

        // "You may cast any number" — controller picks a subset.
        var pickedRaw = eligible.Count == 0
            ? (IReadOnlyList<ICard>)Array.Empty<ICard>()
            : chooseSpells(eligible) ?? Array.Empty<ICard>();

        // Defensive — drop any picks not in the eligible pile.
        var picked = pickedRaw.Where(c => eligible.Contains(c)).ToList();

        return new Result(
            Exiled: exiled,
            Eligible: eligible,
            Picked: picked);
    }
}
