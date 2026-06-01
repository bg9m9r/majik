using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fiery Temper (Torment / reprints, {1}{R}{R}).
///
/// Instant. Oracle text:
///   "Fiery Temper deals 3 damage to any target.
///    Madness {R} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Implemented (v1)
/// - {1}{R}{R} Instant. Damage body resolves through the spell pipeline.
/// - <b>Madness {R} (CR 702.35)</b> — wired through the shared reusable
///   mechanic: <see cref="Effects.MadnessReplacement"/> (discard → exile) +
///   <see cref="Costs.MadnessAlternativeCost"/> (cast from exile for {R}) +
///   <see cref="Keywords.MadnessHelper"/> (the cast-or-graveyard window). The
///   factory registers the discard-replacement on the supplied
///   <see cref="ReplacementBus"/> and exposes the madness cost.
/// </summary>
[CardName("Fiery Temper")]
public static class FieryTemperFactory
{
    public const string CardName = "Fiery Temper";
    public const string PrintedManaCost = "{1}{R}{R}";
    public const string MadnessCost = "{R}";
    public const int Damage = 3;

    /// <summary>The madness alternative cost for casting from exile (CR 702.35).</summary>
    public static Costs.MadnessAlternativeCost MadnessAltCost { get; } =
        new(ManaCost.Parse(MadnessCost));

    public static Instant Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Fiery Temper. When <paramref name="replacements"/> is supplied,
    /// the Madness discard → exile replacement is registered so discarding this
    /// card sends it to exile (castable for {R}) instead of the graveyard.
    /// </summary>
    public static Instant Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        replacements?.Register<ZoneMoveIntent>(new MadnessReplacement(card));
        return card;
    }
}
