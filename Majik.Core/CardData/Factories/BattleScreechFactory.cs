using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Battle Screech (Judgment, {2}{W}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Create two 1/1 white Bird creature tokens with flying.
///    Flashback—Tap three untapped white creatures you control. (You may
///    cast this card from your graveyard for its flashback cost. Then
///    exile it.)"
///
/// The base shape (name, Sorcery type, {2}{W}{W}) is materialised from the
/// embedded JSON definition (<c>battle-screech.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="IntangibleVirtueFactory"/>. The token-creation resolve body and
/// the non-mana flashback rider are layered on here because the JSON schema
/// doesn't express token creation or alternative costs.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{W}{W} (CR 202.3 — mana value 4).
/// - Resolve effect (<see cref="BuildResolveEffects"/>): create two 1/1 white
///   Bird creature tokens with Flying via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. Same looped-token
///   pattern as <see cref="SpectralProcessionFactory"/> /
///   <see cref="LingeringSoulsFactory"/> — explicit White colour stamp via
///   <see cref="TokenFactory.TokenSpec.Colors"/> (CR 105 / 111.4), Flying as a
///   granted <see cref="KeywordAbility"/> via the spec's Keywords list
///   (CR 702.9). Only the subtype differs (Bird, not Spirit / Soldier).
/// - <see cref="BuildSpellDefinition"/> wraps the resolve effects into a
///   <see cref="SpellDefinition.Vanilla"/> (no targets — CR 115.1) for
///   cast-flow integration.
/// - <b>Flashback (non-mana)</b>: Battle Screech's printed flashback cost is
///   "Tap three untapped white creatures you control" — a non-mana cost. The
///   engine's <see cref="FlashbackAlternativeCost"/> carries only the mana
///   portion (CR 118.9), so v1 splits the cost the same way Cabal Therapy
///   does: the alt-cost is <see cref="ManaCost.Zero"/>
///   (<see cref="BuildFlashbackCost"/>) and the tap-three-white-creatures
///   rider ships as a separate <see cref="TapWhiteCreaturesAdditionalCost"/>
///   (<see cref="BuildFlashbackAdditionalCosts"/>) that callers thread through
///   <see cref="SpellCastFlow"/>'s <c>additionalCosts</c> parameter when
///   flashbacking. The post-resolve exile (CR 702.34b) runs through the
///   alt-cost's <c>OnResolved</c> hook.
///
/// ## Deferred (v1 gaps)
/// - <b>Flashback-with-tap as a single cost</b>: the engine's
///   <see cref="IAlternativeCost"/> surface carries only the mana portion, so
///   the tap rider rides as a paired additional cost (same posture / deferral
///   as <see cref="CabalTherapyFactory"/>). Future work: extend
///   <see cref="IAlternativeCost"/> to carry a non-mana rider list so the cast
///   flow charges them together.
/// - <b>"Choose which white creatures to tap" prompt</b>: the rider
///   deterministically taps the first three eligible white creatures in
///   battlefield order (same posture as the rest of the tap-as-cost family).
/// </summary>
[CardName("Battle Screech")]
public static class BattleScreechFactory
{
    public const string CardName = "Battle Screech";
    public const string Slug = "battle-screech";

    public const int TokensCreated = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>Number of white creatures tapped to pay the flashback cost.</summary>
    public const int FlashbackTapCount = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct the Battle Screech sorcery shape (name, Sorcery, {2}{W}{W})
    /// from the embedded JSON definition. No resolve effect is bound — callers
    /// build the create-two-Birds body via <see cref="BuildSpellDefinition"/>
    /// or <see cref="BuildResolveEffects"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Sorcery)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build a <see cref="SpellDefinition"/> for Battle Screech. No target
    /// requests (CR 115.1). On resolution the caster creates two 1/1 white
    /// Bird creature tokens with flying.
    /// </summary>
    /// <param name="caster">The player casting Battle Screech.</param>
    /// <param name="zoneService">Optional zone service so spawned tokens
    /// publish <see cref="Majik.Core.Events.CardMovedEvent"/> on ETB.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return SpellDefinition.Vanilla(_ => BuildResolveEffects(caster, zoneService));
    }

    /// <summary>
    /// Build Battle Screech's resolve effects — create two 1/1 white Bird
    /// creature tokens with Flying under <paramref name="caster"/>'s control
    /// (CR 111 / 111.4). The same body is reused for the printed-cost cast and
    /// the flashback cast; the flashback alt-cost's
    /// <see cref="FlashbackAlternativeCost.OnResolved"/> performs the
    /// post-resolve exile (CR 702.34b), not this body.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffects(
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
    /// CR 111 / 111.4 — create one 1/1 white Bird creature token with Flying
    /// under <paramref name="controller"/>. White colour is stamped via
    /// <see cref="TokenFactory.TokenSpec.Colors"/>; Flying is a granted
    /// <see cref="KeywordAbility"/> via the spec's Keywords list (CR 702.9).
    /// Mirrors <see cref="LingeringSoulsFactory.CreateSpiritToken"/> — only the
    /// subtype (Bird) differs.
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

    /// <summary>
    /// Build the flashback alternative cost. Battle Screech's printed flashback
    /// cost is "Tap three untapped white creatures you control" — non-mana — so
    /// the returned cost carries <see cref="ManaCost.Zero"/>. The tap rider
    /// ships separately via <see cref="BuildFlashbackAdditionalCosts"/>;
    /// callers compose both when wiring the flashback cast through
    /// <see cref="SpellCastFlow"/>. Post-resolve exile (CR 702.34b) is handled
    /// by the cost's <c>OnResolved</c> hook (same as
    /// <see cref="CabalTherapyFactory"/>).
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost() =>
        new FlashbackAlternativeCost(ManaCost.Zero);

    /// <summary>
    /// Build the additional-cost rider that accompanies the flashback alt-cost
    /// — "Tap three untapped white creatures you control" as a non-mana cost
    /// (CR 601.2f / CR 702.34). Returned as a single-element list to match the
    /// shape <see cref="SpellCastFlow"/> threads through its
    /// <c>additionalCosts</c> parameter.
    /// </summary>
    public static IReadOnlyList<IAdditionalCost> BuildFlashbackAdditionalCosts() =>
        new IAdditionalCost[] { new TapWhiteCreaturesAdditionalCost(FlashbackTapCount) };
}
