using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Surgical Extraction (New Phyrexia, <c>{B/P}</c>).
///
/// Instant. Oracle text:
///   "({B/P} can be paid with either {B} or 2 life.)
///    Choose target card in a graveyard other than a basic land card.
///    Search its owner's graveyard, hand, and library for any number of
///    cards with the same name as that card and exile them. Then that
///    player shuffles."
///
/// ## Implemented (v1)
/// - Instant card shape ({B/P} printed; engine treats the printed cost
///   as <c>{B}</c> — paying mana — and exposes
///   <see cref="PhyrexianAlternativeCost"/> for the 2-life alternative).
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   * Validates that the chosen target is in a graveyard and is NOT a
///     basic land card (CR 601.2c — illegal targets prevent the cast).
///   * Collects every card sharing the target's name across the target's
///     owner's graveyard + hand + library, exiles them all, and then
///     shuffles that player's library (CR 701.19c).
/// - Card-name match is case-insensitive whitespace-insensitive
///   <see cref="StringComparison.OrdinalIgnoreCase"/>; the target itself
///   is included in the exile sweep (it shares its own name).
/// - <see cref="PhyrexianAlternativeCost"/> static returns a freshly-built
///   <see cref="Majik.Core.Costs.PhyrexianManaAlternativeCost"/> for
///   callers (cast dispatcher / tests) that want to pay 2 life.
///
/// ## Deferred (v1 gaps)
/// - Bot probe / heuristics for choosing between mana payment and life
///   payment — the engine currently relies on the caller to pass the
///   alternative cost explicitly.
/// - Sub-pip selectivity (paying 1 mana + 1 phyrexian-as-life on multi-
///   pip costs): n/a for Surgical (single pip), but
///   <see cref="Majik.Core.Costs.PhyrexianManaAlternativeCost"/> only
///   models "pay every pip with life", not per-pip mixing.
/// </summary>
[CardName("Surgical Extraction")]
public static class SurgicalExtractionFactory
{
    public const string CardName = "Surgical Extraction";

    /// <summary>
    /// Printed mana cost. The {B/P} symbol is parsed into a phyrexian pip
    /// on the ManaCost value object; for runtime payment in the v1 engine
    /// we treat the cost as <c>{B}</c> (mana-pay) and the 2-life option
    /// via <see cref="PhyrexianAlternativeCost"/>.
    /// </summary>
    public const string PrintedManaCost = "{B}";

    /// <summary>Construct the printed instant card.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition. The target is a card in a graveyard
    /// other than a basic land card. <paramref name="allGraveyardCards"/>
    /// supplies the legal-candidate set (every card in every graveyard,
    /// excluding basic lands). At resolution the chosen card's name is
    /// used to sweep the target's owner's graveyard, hand, and library.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        IReadOnlyList<ICard> allGraveyardCards) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target card in a graveyard other than a basic land card",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: allGraveyardCards
                        .Where(c => c.Zone == ZoneType.Graveyard
                                    && !IsBasicLand(c))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                // EffectFactory runs synchronously inside SpellCastFlow.CastAsync
                // BEFORE the spell is pushed onto the stack — so throwing here
                // aborts the cast (CR 601.2c — illegal target = cast rewound).
                var rawTarget = p.Targets.Count > 0 && p.Targets[0].Count > 0
                    ? p.Targets[0][0]
                    : null;
                if (rawTarget is not ICard target)
                {
                    throw new InvalidOperationException(
                        "Surgical Extraction requires a card target.");
                }
                if (target.Zone != ZoneType.Graveyard)
                {
                    throw new InvalidOperationException(
                        "Surgical Extraction's target must be in a graveyard.");
                }
                if (IsBasicLand(target))
                {
                    throw new InvalidOperationException(
                        "Surgical Extraction cannot target a basic land card.");
                }
                if (target.Owner == null)
                {
                    throw new InvalidOperationException(
                        "Surgical Extraction's target has no owner.");
                }
                var targetOwner = target.Owner;
                var targetName = target.Name;

                return new IEffect[]
                {
                    new Effect(
                        $"Surgical Extraction — exile all '{targetName}' from {targetOwner.Name}'s graveyard/hand/library + shuffle",
                        () =>
                        {
                            var sweep = new List<ICard>();
                            sweep.AddRange(targetOwner.Zones.Graveyard.GetCards()
                                .Where(c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase)));
                            sweep.AddRange(targetOwner.Zones.Hand.GetCards()
                                .Where(c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase)));
                            sweep.AddRange(targetOwner.Zones.Library.GetCards()
                                .Where(c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase)));

                            foreach (var card in sweep)
                            {
                                var from = card.Zone;
                                switch (from)
                                {
                                    case ZoneType.Graveyard:
                                        targetOwner.Zones.Graveyard.RemoveCard(card);
                                        break;
                                    case ZoneType.Hand:
                                        targetOwner.Zones.Hand.RemoveCard(card);
                                        break;
                                    case ZoneType.Library:
                                        targetOwner.Zones.Library.RemoveCard(card);
                                        break;
                                }
                                targetOwner.Zones.Exile.AddCard(card);
                                card.SetZone(ZoneType.Exile);
                            }

                            // CR 701.19c — shuffle the searched library.
                            ShuffleLibrary(targetOwner);
                        }),
                };
            });

    /// <summary>
    /// Build the phyrexian alternative cost (2 life instead of {B}) for a
    /// just-created Surgical Extraction instance. Caller passes this to
    /// <c>SpellCastFlow.CastAsync(..., alternativeCost: ...)</c>.
    /// </summary>
    public static Majik.Core.Costs.PhyrexianManaAlternativeCost
        PhyrexianAlternativeCost() =>
        Majik.Core.Costs.PhyrexianManaAlternativeCost.ForPrintedCost(
            Majik.Core.ValueObjects.ManaCost.Parse("{B/P}"));

    private static bool IsBasicLand(ICard card) =>
        card.HasType(CardType.Land) && card.HasSupertype(CardSupertype.Basic);

    /// <summary>
    /// CR 701.20a — library shuffle via the shared primitive.
    /// </summary>
    private static void ShuffleLibrary(Player player)
    {
        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "surgical-extraction");
    }
}
