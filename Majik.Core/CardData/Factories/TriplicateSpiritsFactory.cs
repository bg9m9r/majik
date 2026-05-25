using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Triplicate Spirits (Khans of Tarkir, {4}{W}).
///
/// Sorcery. Oracle text (Scryfall, verbatim):
///   "Convoke (Your creatures can help cast this spell. Each creature you
///    tap while casting this spell pays for {1} or one mana of that
///    creature's color.)
///    Create three 1/1 white Spirit creature tokens with flying."
///
/// ## Implemented (v1)
///
/// - Sorcery shape, mana cost <c>{4}{W}</c>.
/// - <see cref="KeywordAbility"/> marker for Convoke (CR 702.51) attached
///   to the card so dispatch / shape inspectors see the keyword.
/// - <see cref="BuildAdditionalCost"/> surfaces Convoke as a
///   <see cref="ConvokeAdditionalCost"/> consumed by
///   <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
///   <c>additionalCosts</c> rail (mirrors
///   <see cref="ChordOfCallingFactory.BuildAdditionalCost"/>). The cast
///   flow taps the chosen untapped creatures and folds the per-tap
///   reduction (generic OR a coloured pip matching the creature's colour,
///   per CR 702.51b) into the mana payment.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>): create three
///   1/1 white Spirit creature tokens with Flying (CR 111.4 / CR 603.6a).
///   Tokens are explicitly white (single colour stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/>) and carry the Flying
///   keyword as a <see cref="KeywordAbility"/>.
///
/// ## Deferred (v1 gaps)
///
/// - Agent-driven prompt for "which creatures to tap for Convoke" — same
///   posture as Chord of Calling. Bots / tests pre-select the creature list.
/// </summary>
[CardName("Triplicate Spirits")]
public static class TriplicateSpiritsFactory
{
    public const string CardName = "Triplicate Spirits";
    public const string PrintedManaCost = "{4}{W}";
    public const int TokenCount = 3;

    /// <summary>
    /// Build a Triplicate Spirits sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can splice
    /// it into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>. The Convoke
    /// keyword marker is attached inline so the dispatcher / shape
    /// inspection path sees it without separate wiring.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.51 — Convoke keyword marker. The marker is purely
        // descriptive; the cost machinery lives on the IAdditionalCost
        // returned by BuildAdditionalCost.
        card.AddAbility(new KeywordAbility("Convoke", card, owner));

        return card;
    }

    /// <summary>
    /// CR 702.51 — build the Convoke additional cost for this Triplicate
    /// Spirits spell with the caller-selected untapped creatures. Same
    /// shape as <see cref="ChordOfCallingFactory.BuildAdditionalCost"/>:
    /// the caller threads the returned cost through
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter; the cast flow taps the chosen
    /// creatures and folds the per-tap reduction into the mana payment.
    /// </summary>
    public static ConvokeAdditionalCost BuildAdditionalCost(
        ICard card, IReadOnlyList<Creature> tappedCreatures) =>
        new(card, tappedCreatures);

    /// <summary>
    /// Build Triplicate Spirits' resolve effect — create three 1/1 white
    /// Spirit creature tokens with Flying (CR 603.6a / CR 111.4).
    /// </summary>
    /// <param name="caster">Spell controller — the player whose battlefield
    /// the tokens enter under.</param>
    /// <param name="zones">Optional <see cref="ZoneService"/>. When supplied
    /// spawned tokens route through
    /// <see cref="TokenFactory.CreateOnBattlefield"/> using the service so
    /// each token publishes <see cref="Events.CardMovedEvent"/> on
    /// battlefield entry (downstream ETB listeners — Soul Warden,
    /// Bitterblossom-style triggers — fire).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create three 1/1 white Spirit creature tokens with flying",
                () => CreateSpiritTokens(caster, TokenCount, zones)),
        };
    }

    /// <summary>
    /// CR 603.6a / CR 111.4 — create <paramref name="count"/> 1/1 white
    /// Spirit creature tokens with Flying under <paramref name="controller"/>'s
    /// control. Promoted to an internal helper so
    /// <see cref="LingeringSoulsFactory"/> can call the same builder (both
    /// cards create the same token shape).
    /// </summary>
    internal static IReadOnlyList<Creature> CreateSpiritTokens(
        Player controller, int count, ZoneService? zones)
    {
        if (count <= 0) return Array.Empty<Creature>();

        var spec = new TokenFactory.TokenSpec(
            Name: "Spirit",
            Power: 1,
            Toughness: 1,
            Subtypes: new[] { CardSubtype.Spirit },
            Keywords: new[] { "Flying" },
            Colors: new[] { ManaColor.White });

        var tokens = new Creature[count];
        for (var i = 0; i < count; i++)
        {
            tokens[i] = TokenFactory.CreateOnBattlefield(spec, controller, zones);
        }
        return tokens;
    }
}
