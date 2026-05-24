using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Amped Raptor (Modern Horizons 3, {1}{R}).
///
/// Creature — Dinosaur 3/1. Oracle text:
///   "Trample
///    When Amped Raptor enters, exile the top four cards of your library.
///    You may cast a spell with mana value 2 or less from among them
///    without paying its mana cost."
///
/// ## Implemented (v1)
/// - 3/1 Creature — Dinosaur at {1}{R}.
/// - Trample keyword marker (CR 702.19) wired via <see cref="KeywordAbility"/>
///   — combat helpers read these directly the same way they do for every
///   other trample-bearing factory in this repo.
/// - ETB triggered ability (CR 603.6a — "when ~ enters"):
///     1. Exile the top four cards of the controller's library (raw zone
///        moves Library → Exile, mirroring <see cref="CascadeAction"/>'s
///        exile shape). If the library is short, the trigger exiles as
///        many as remain (CR 701.21 — "the top N cards" never throws).
///     2. Filter the exiled pile to spells (Instant / Sorcery) with mana
///        value ≤ 2 — the "may cast a spell with mana value 2 or less"
///        candidate pool (CR 202.3 reads mana value off the printed cost).
///     3. Ask the supplied <c>chooseSpell</c> picker which candidate (if
///        any) to cast. The picker returns <c>null</c> to decline the
///        "may" (CR 603.6c — printed "may" is a controller choice on
///        resolution). Default = always pick the first candidate.
///     4. Invoke the supplied <c>onEtbResolved</c> callback with the
///        exile <see cref="Result"/> so the host can drive the free cast
///        through <see cref="SpellCastFlow"/> with a
///        <see cref="CastFromExileAlternativeCost"/> at <see cref="ManaCost.Zero"/>
///        (mirrors <see cref="CrashingFootfallsFactory"/>'s
///        <c>onCascadeResolved</c> hook). The remaining exiled cards
///        stay in exile — printed oracle does NOT return them to the
///        library, unlike Cascade.
///
/// ## Single-arg dispatcher path
/// The <see cref="Create(Player)"/> overload attaches Trample + the ETB
/// trigger structurally (so the card shape is correct for tests and the
/// <see cref="NamedCardFactory"/> dispatch path). With no <c>chooseSpell</c>
/// / <c>onEtbResolved</c> wiring the trigger effect is a no-op when
/// invoked — there is no <see cref="SpellCastFlow"/> bound to drive the
/// free cast. Production callers use the full overload.
///
/// ## Deferred (v1 gaps)
/// - <b>Auto-routed free cast</b>: the ETB trigger does not own a
///   <see cref="SpellCastFlow"/> reference. Production code wires the
///   cast via the <c>onEtbResolved</c> callback (same posture as
///   Crashing Footfalls / Living End / Tibalt's Trickery). When the
///   engine grows a per-trigger SpellCastFlow injection point this
///   collapses to inline.
/// - <b>Surface-time TargetRequest</b>: the candidate pool is built
///   inside the effect (it depends on which cards happened to be
///   exiled), so a <see cref="Targeting.TargetRequest"/> attached to
///   the <see cref="TriggeredAbility"/> at construction can't capture
///   it. The picker callback stands in for the prompt — the agent /
///   bot probe drives the choice the same way the EV-search policies
///   do for Library Pick today.
/// </summary>
[CardName("Amped Raptor")]
public static class AmpedRaptorFactory
{
    public const string CardName = "Amped Raptor";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 3;
    public const int Toughness = 1;
    public const int ExileCount = 4;
    public const int MaxCastableManaValue = 2;

    /// <summary>
    /// Outcome of the ETB trigger. <see cref="Exiled"/> is every card the
    /// ETB moved Library → Exile (top of library first), <see cref="Eligible"/>
    /// is the subset filtered to Instant / Sorcery with mana value ≤ 2,
    /// and <see cref="Picked"/> is the card the controller chose to cast
    /// (or <c>null</c> when the "may" was declined / no candidate
    /// existed). The picked card sits in exile for the caller to drive
    /// through <see cref="SpellCastFlow"/> with a
    /// <see cref="CastFromExileAlternativeCost"/>.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<ICard> Exiled,
        IReadOnlyList<ICard> Eligible,
        ICard? Picked);

    /// <summary>
    /// Construct Amped Raptor with no runtime services. The ETB trigger
    /// is attached for shape inspection but is not registered with a
    /// TriggerManager and has no free-cast routing. Suitable for
    /// dispatcher / shape-only tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, chooseSpell: null, onEtbResolved: null);

    /// <summary>
    /// Construct Amped Raptor with optional TriggerManager wiring +
    /// agent-driven choice / free-cast callback.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger is
    /// registered so the <see cref="CardMovedEvent"/> → Battlefield
    /// for this card lands on the stack automatically.</param>
    /// <param name="chooseSpell">Picker invoked with the eligible pile
    /// (Instant / Sorcery with MV ≤ 2 among the four exiled cards).
    /// Returns the card to cast for free, or <c>null</c> to decline.
    /// Default = first eligible candidate (auto-accept "may"). Tests
    /// override to pin selection.</param>
    /// <param name="onEtbResolved">Callback invoked with the
    /// <see cref="Result"/> after the exile + pick step. Production
    /// callers use this to drive the free cast of
    /// <see cref="Result.Picked"/> via
    /// <see cref="CastFromExileAlternativeCost"/> + <see cref="SpellCastFlow"/>.
    /// Tests use it to observe the resolution.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<ICard>, ICard?>? chooseSpell = null,
        Action<Result>? onEtbResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        chooseSpell ??= static pile => pile.FirstOrDefault();

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Dinosaur });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.19 — Trample. Keyword marker, read directly by the
        // combat helpers in Majik.Core.Combat.CombatAbilities.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // ETB trigger (CR 603.6a):
        //   "When Amped Raptor enters, exile the top four cards of your
        //    library. You may cast a spell with mana value 2 or less
        //    from among them without paying its mana cost."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName} — exile top {ExileCount}, may cast a spell with mana value " +
            $"≤ {MaxCastableManaValue} from among them for free",
            () =>
            {
                var result = ResolveEtb(owner, chooseSpell);
                onEtbResolved?.Invoke(result);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Execute Amped Raptor's ETB body against <paramref name="controller"/>'s
    /// library / exile. Public so tests and bots can drive the resolution
    /// without going through TriggerManager. Always exiles up to
    /// <see cref="ExileCount"/> cards (fewer if the library is short),
    /// builds the eligible pile (Instant / Sorcery with MV ≤
    /// <see cref="MaxCastableManaValue"/>), and asks <paramref name="chooseSpell"/>
    /// which card (if any) to cast for free.
    ///
    /// The picked card stays in exile so the caller can route it through
    /// <see cref="SpellCastFlow"/> with a
    /// <see cref="CastFromExileAlternativeCost"/> at
    /// <see cref="ManaCost.Zero"/>. The remaining exiled cards also stay
    /// in exile (printed oracle — no return-to-library step).
    /// </summary>
    public static Result ResolveEtb(
        Player controller,
        Func<IReadOnlyList<ICard>, ICard?>? chooseSpell = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        chooseSpell ??= static pile => pile.FirstOrDefault();

        var library = controller.Zones.Library;
        var exile = controller.Zones.Exile;

        // CR 701.21 — exile top N (or fewer if the library is short).
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

        // Candidate pool — Instant / Sorcery with MV ≤ 2 (CR 202.3).
        var eligible = exiled.Where(IsCastable).ToList();

        // "You may" — controller chooses one or declines (returns null).
        var picked = eligible.Count == 0 ? null : chooseSpell(eligible);

        // Defensive — never return a pick that isn't actually in the
        // eligible pile (mis-wired chooser would otherwise drive a free
        // cast against a card the engine never deemed legal).
        if (picked != null && !eligible.Contains(picked))
        {
            picked = null;
        }

        return new Result(
            Exiled: exiled,
            Eligible: eligible,
            Picked: picked);
    }

    /// <summary>
    /// Convenience builder for the cast-from-exile alt cost used to "cast
    /// a spell …  without paying its mana cost" (CR 118.9 / CR 117.11).
    /// The cost is <see cref="ManaCost.Zero"/> — the spell is cast for
    /// free. Production callers feed this into <see cref="SpellCastFlow"/>
    /// alongside <see cref="Result.Picked"/>.
    /// </summary>
    public static CastFromExileAlternativeCost BuildAlternativeCost(ICard exiledCard)
    {
        ArgumentNullException.ThrowIfNull(exiledCard);
        return new CastFromExileAlternativeCost(
            description: $"{CardName} — cast {exiledCard.Name} from exile without paying its mana cost",
            cost: ManaCost.Zero);
    }

    /// <summary>
    /// Castability predicate: Instant or Sorcery with mana value ≤
    /// <see cref="MaxCastableManaValue"/>. Lands / creatures /
    /// planeswalkers / enchantments / artifacts are excluded — printed
    /// oracle says "cast a spell", which on a sorcery-speed ETB resolution
    /// (CR 603.3a — ETB triggers resolve on the stack, instant-speed) is
    /// effectively limited to instants and sorceries at the v1 castable-
    /// at-instant-speed shape. The card-data-driven loader will catch
    /// permanent spells when they ship with a cast-at-instant-speed
    /// alt-cost (Flash etc.) — same gap as every "cast for free from
    /// exile" hook today.
    /// </summary>
    private static bool IsCastable(ICard card)
    {
        if (card.HasType(CardType.Land)) return false;
        if (!card.HasType(CardType.Instant) && !card.HasType(CardType.Sorcery)) return false;

        int mv = card is Card concrete
            ? concrete.ManaCostValue.TotalValue
            : ManaCost.Parse(card.ManaCost ?? string.Empty).TotalValue;

        return mv <= MaxCastableManaValue;
    }
}
