using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Call to the Netherworld (Eldritch Moon, {B}).
///
/// Sorcery. Oracle text:
///   "Return target black creature card from your graveyard to your hand.
///    Madness {0} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Implemented (v1)
/// - {B} Sorcery. Return-from-graveyard body resolves through the spell pipeline.
/// - <b>Madness {0} (CR 702.35)</b> — wired through the shared reusable
///   mechanic (<see cref="Effects.MadnessReplacement"/> +
///   <see cref="Costs.MadnessAlternativeCost"/> + <see cref="Keywords.MadnessHelper"/>).
///   The free madness cost ({0}) makes this the canonical "discard, then re-cast
///   for nothing" engine that powers Hollow One / Anje shells.
/// </summary>
[CardName("Call to the Netherworld")]
public static class CallToTheNetherworldFactory
{
    public const string CardName = "Call to the Netherworld";
    public const string PrintedManaCost = "{B}";
    public const string MadnessCost = "{0}";

    /// <summary>The madness alternative cost for casting from exile (free — {0}).</summary>
    public static Costs.MadnessAlternativeCost MadnessAltCost { get; } =
        new(ManaCost.Parse(MadnessCost));

    public static Sorcery Create(Player owner) => Create(owner, replacements: null);

    /// <summary>Construct Call to the Netherworld with optional Madness wiring.</summary>
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
