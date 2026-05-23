using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tibalt's Trickery (Kaldheim, {R}).
///
/// Instant. Oracle text:
///   "Counter target spell. Its controller mills three cards, then exiles
///    cards from the top of their library until they exile a nonland card
///    that shares a card type with it. They may cast that card without
///    paying its mana cost. Then they put all cards exiled this way that
///    weren't cast on the bottom of their library in a random order."
///
/// ## Implemented (v1)
/// - Instant shape at printed cost {R} (red).
/// - Resolve-time <see cref="SpellDefinition"/> built by
///   <see cref="BuildSpellDefinition"/>:
///   * Counter target spell (CR 701.5 — same idiom as
///     <see cref="SpellTemplates.Templates.Counter.CounterSpellFactory"/>).
///   * The countered spell's controller mills three cards (CR 701.13,
///     <see cref="MillAction.Apply"/>).
///   * Exile-from-top-of-controller's-library until exiling a nonland card
///     that shares a card type with the countered spell (CR 308.2 — types
///     are compared as the set of <see cref="CardType"/> values).
///   * Random-order bottom for every exiled card that wasn't cast
///     (<see cref="GameRandom.Shuffle"/>).
///
/// ## Deferred (v1 gap)
/// - <b>"May cast that card without paying its mana cost"</b>: the actual
///   alternative-cost free cast is driven by the caller via
///   <see cref="Costs.CastFromExileAlternativeCost"/> + <see cref="SpellCastFlow"/>,
///   mirroring how <see cref="CrashingFootfallsFactory"/> drives cascade's
///   free-cast through an <c>onTrickeryResolved</c> callback. The default
///   (no callback) leaves the eligible card in exile briefly, then bottoms
///   it alongside the rest — i.e. the "structural" reading where no cast
///   happens. Production callers wire the callback to spawn the free cast
///   through SpellCastFlow.
/// </summary>
public static class TibaltsTrickeryFactory
{
    public const string CardName = "Tibalt's Trickery";
    public const string PrintedManaCost = "{R}";
    public const int MillCount = 3;

    /// <summary>
    /// Construct the Tibalt's Trickery card shape with no resolve
    /// definition attached. The resolve-time <see cref="SpellDefinition"/>
    /// is built on demand via <see cref="BuildSpellDefinition"/> at the
    /// SpellCastFlow wire-up site, so the dispatcher path produces a clean
    /// shape-only card.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Outcome of resolving Tibalt's Trickery against a particular target
    /// spell. Exposed so callers (production cast-for-free path, tests)
    /// can observe the exile/bottom pile and the eligible card without
    /// re-walking the library.
    /// </summary>
    public sealed record TrickeryResolution(
        ISpell CounteredSpell,
        IReadOnlyList<ICard> Exiled,
        ICard? Eligible,
        IReadOnlyList<ICard> Bottomed);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Targets one
    /// spell; on resolution counters it, mills 3 cards off its controller's
    /// library, exiles until a shared-type nonland, then optionally casts
    /// that card free (via <paramref name="onResolved"/>) and bottoms the
    /// rest in random order.
    /// </summary>
    /// <param name="resolver">SpellBindContext-style target resolver — maps
    /// the raw chosen-target token to the resolved <see cref="ISpell"/>.</param>
    /// <param name="stack">Live stack — used to remove the countered spell
    /// (CR 701.5).</param>
    /// <param name="onResolved">Optional callback invoked with the
    /// <see cref="TrickeryResolution"/> after the exile walk completes but
    /// BEFORE the bottom-in-random-order step. Production callers wire this
    /// to drive the optional free cast of <see cref="TrickeryResolution.Eligible"/>
    /// through <see cref="Costs.CastFromExileAlternativeCost"/> +
    /// <see cref="SpellCastFlow"/>. Tests use it to observe the eligible
    /// pile. When the callback moves the eligible card out of exile, the
    /// bottom step skips it automatically.</param>
    /// <param name="random">Optional RNG for the random-order bottom step.
    /// Tests pin order by passing a seeded instance.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack,
        Action<TrickeryResolution>? onResolved = null,
        GameRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = resolver(raw);
                return new IEffect[]
                {
                    new Effect("Tibalt's Trickery — counter + mill 3 + reveal-until-matching", () =>
                        Resolve(resolved, stack, onResolved, random)),
                };
            });
    }

    private static void Resolve(
        object resolvedTarget,
        Majik.Core.Stack.Stack? stack,
        Action<TrickeryResolution>? onResolved,
        GameRandom? random)
    {
        if (stack == null || resolvedTarget is not ISpell spell) return;

        // Snapshot the countered spell's card-type set BEFORE removing it
        // from the stack — once the spell moves to the graveyard the card
        // is still the same object, but the snapshot avoids any future
        // type-set mutation surprises (CR 308.2 / CR 700.2).
        var counteredTypes = SnapshotTypes(spell.Card);
        var controller = spell.Controller;

        // CR 701.5 — counter target spell.
        OracleSpellBinder.RemoveFromStack(stack, spell);
        spell.Card.SetZone(ZoneType.Graveyard);

        if (controller == null) return;

        // CR 701.13 — countered spell's controller mills three cards.
        MillAction.Apply(controller, MillCount);

        // CR 701.20 (reveal) / CR 701.21 (exile) — exile from the top of
        // that player's library until a nonland card sharing a card type
        // with the countered spell is exiled (or the library runs out).
        random ??= new GameRandom();
        var library = controller.Zones.Library;
        var exile = controller.Zones.Exile;

        var exiled = new List<ICard>();
        ICard? eligible = null;

        while (true)
        {
            var top = library.GetCards().FirstOrDefault();
            if (top == null) break; // library empty.

            library.RemoveCard(top);
            exile.AddCard(top);
            top.SetZone(ZoneType.Exile);
            exiled.Add(top);

            if (IsEligible(top, counteredTypes))
            {
                eligible = top;
                break;
            }
        }

        // CR 702.85a-style "you may cast" hook — caller drives the actual
        // free cast through SpellCastFlow when supplied. Default = no cast.
        var resolution = new TrickeryResolution(
            CounteredSpell: spell,
            Exiled: exiled,
            Eligible: eligible,
            Bottomed: Array.Empty<ICard>());
        onResolved?.Invoke(resolution);

        // Bottom every still-in-exile card from this resolution in random
        // order. Cards the onResolved callback moved out of exile (e.g. by
        // casting them) are skipped here automatically via the zone check.
        var toBottom = exiled
            .Where(c => c.Zone == ZoneType.Exile)
            .ToList();
        random.Shuffle(toBottom);
        foreach (var card in toBottom)
        {
            exile.RemoveCard(card);
            library.AddCard(card); // AddCard appends — that's the bottom.
            card.SetZone(ZoneType.Library);
        }
    }

    private static IReadOnlyList<CardType> SnapshotTypes(ICard card) =>
        card.CardTypes?.ToArray() ?? Array.Empty<CardType>();

    /// <summary>CR 308.2 — two cards "share a card type" iff their card-type
    /// sets intersect. Lands are excluded per oracle text ("nonland card").</summary>
    private static bool IsEligible(ICard card, IReadOnlyList<CardType> counteredTypes)
    {
        if (card.HasType(CardType.Land)) return false;
        for (int i = 0; i < counteredTypes.Count; i++)
        {
            var t = counteredTypes[i];
            if (t == CardType.Land) continue;
            if (card.HasType(t)) return true;
        }
        return false;
    }
}
