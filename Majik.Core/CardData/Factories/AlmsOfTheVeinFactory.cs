using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alms of the Vein (Shadows over Innistrad, {2}{B}).
///
/// Sorcery. Oracle text:
///   "Target opponent loses 3 life and you gain 3 life.
///    Madness {B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Implemented (v1)
/// - {2}{B} Sorcery. Drain body (opponent loses 3, you gain 3) resolves through
///   the spell pipeline.
/// - <b>Madness {B} (CR 702.35)</b> — wired through the shared reusable
///   mechanic (<see cref="Effects.MadnessReplacement"/> +
///   <see cref="Costs.MadnessAlternativeCost"/> + <see cref="Keywords.MadnessHelper"/>).
/// </summary>
[CardName("Alms of the Vein")]
public static class AlmsOfTheVeinFactory
{
    public const string CardName = "Alms of the Vein";
    public const string PrintedManaCost = "{2}{B}";
    public const string MadnessCost = "{B}";
    public const int LifeSwing = 3;

    /// <summary>The madness alternative cost for casting from exile (CR 702.35).</summary>
    public static Costs.MadnessAlternativeCost MadnessAltCost { get; } =
        new(ManaCost.Parse(MadnessCost));

    public static Sorcery Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Alms of the Vein with optional Madness wiring.</summary>
    public static Sorcery Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        replacements?.Register<ZoneMoveIntent>(new MadnessReplacement(card));
        return card;
    }
}
