using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the CALL half of the split/fuse card Beck // Call
/// (Dragon's Maze, {G}{U} // {4}{W}{U}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-03):
///   "Create four 1/1 white Bird creature tokens with flying.
///    Fuse (You may cast one or both halves of this card from your hand.)"
///
/// Sister half — <see cref="BeckFactory"/> ({G}{U}; "Whenever a creature
/// enters this turn, you may draw a card. Fuse ...").
///
/// ## Split-card modelling (CR 712 / CR 709)
///
/// A split card is a single physical card with two halves; the caster picks
/// one half on cast and casts only that half (CR 712.4a). v1 models each
/// printed half as its own <c>[CardName]</c>-dispatched factory — the same
/// minimal posture used for Wear // Tear (<see cref="WearFactory"/> /
/// <see cref="TearFactory"/>). The combined seed row <c>"Beck // Call"</c>
/// flips <c>IsImplemented</c> via the front-face check in
/// <see cref="EmbeddedCardRepository"/> because the front half <c>"Beck"</c>
/// is in the <see cref="ImplementedCardNames"/> registry;
/// <see cref="BeckCallFactory"/> also dispatches the combined name directly.
///
/// ## Implemented (v1)
/// - Sorcery identity at {4}{W}{U} (white/blue, mana value 6), built from the
///   embedded JSON def (<c>call.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="MdfcState"/> attached (back half — Call; sister = Beck).
/// - <b>Create four 1/1 white Bird tokens with flying</b> via
///   <see cref="TokenFactory.CreateOnBattlefield"/> (CR 111 / CR 111.4),
///   each carrying the Flying keyword (CR 702.9). Mirrors
///   <see cref="CaptainsCallFactory"/> (three Soldiers) and the Bird-token
///   shape used by <see cref="BattleScreechFactory"/>.
///
/// ## Deferred (v1 gap — shared with Wear // Tear)
/// - <b>Fuse</b> (CR 702.102) — casting BOTH halves from hand as one split
///   spell. The engine has no split-cast / fuse cast surface yet, so the Fuse
///   keyword is informational only; each half is castable independently via
///   its own <c>[CardName]</c> factory.
/// </summary>
[CardName("Call")]
public static class CallFactory
{
    public const string CardName = "Call";
    public const string SisterName = "Beck";
    public const string Slug = "call";
    public const string PrintedManaCost = "{4}{W}{U}";

    public const int TokensCreated = 4;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Build the Call half as a Sorcery from the embedded JSON def, with the
    /// <see cref="MdfcState"/> face tracker attached (back half — Call).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(def, owner);

        // CR 712 — attach the split-card face tracker so the sister half's
        // printed name (Beck) is observable from the Call object. Informational
        // only, matching the Wear // Tear posture.
        card.MdfcState = new MdfcState(CardName, SisterName);
        return card;
    }

    /// <summary>
    /// Build Call's resolve effect — create four 1/1 white Bird creature tokens
    /// with flying under <paramref name="caster"/> (CR 111 / CR 111.4 /
    /// CR 702.9).
    /// </summary>
    /// <param name="caster">The resolving caster — token controller.</param>
    /// <param name="zoneService">Optional zone service so each spawned Bird
    /// token publishes <see cref="Majik.Core.Events.CardMovedEvent"/> on ETB
    /// (so e.g. a Beck repeating delayed trigger sees each Bird enter). When
    /// null, tokens use raw zone moves.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create {TokensCreated} 1/1 white Bird tokens with flying",
                () =>
                {
                    for (var i = 0; i < TokensCreated; i++)
                    {
                        CreateBirdToken(caster, zoneService);
                    }
                }),
        };
    }

    /// <summary>
    /// CR 111 / CR 111.4 / CR 702.9 — create one 1/1 white Bird creature token
    /// with flying under <paramref name="controller"/>.
    /// </summary>
    public static Creature CreateBirdToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Bird",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Bird },
            Keywords: new[] { "Flying" },
            Colors: new[] { ManaColor.White });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
