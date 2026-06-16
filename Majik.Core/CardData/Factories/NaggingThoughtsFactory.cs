using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nagging Thoughts (Shadows over Innistrad, {1}{U}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Look at the top two cards of your library. Put one of them into your hand
///    and the other into your graveyard.
///    Madness {1}{U} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{U}, owner / controller (CardDef DSL).
/// - <b>Look at top two, one to hand, the other to graveyard</b> —
///   <see cref="BuildDefinition"/> declares a single
///   <see cref="LookTopNOneToHandRestToGraveyardEffectDef"/>(Amount: 2) and
///   hands it to <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>
///   (the shared declarative <c>look_top_n_one_to_hand_rest_to_graveyard</c>
///   verb). With two cards the controller's agent picks which goes to hand; the
///   other is milled to the graveyard (CR 701.16 reveal-less look + CR 401.4
///   library order). A library of one card puts that single card into hand
///   (nothing to graveyard); an empty library is a clean no-op. In PROD the cast
///   path binds the oracle text via <see cref="OracleSpellBinder"/> (the
///   look-at-top-N-put-one-in-hand-rest-to-graveyard template).
///
/// ## Madness {1}{U} (CR 702.35) — intrinsic, NOT wired here
/// "Nagging Thoughts" = {1}{U} is catalogued in
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/>; the central discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> routes the discarded card
/// to exile + offers it for its madness cost. No factory code needed.
/// </summary>
[CardName("Nagging Thoughts")]
public static class NaggingThoughtsFactory
{
    public const string CardName = "Nagging Thoughts";
    public const string PrintedManaCost = "{1}{U}";
    private const int LookCount = 2;

    /// <summary>CardDef DSL — card shape only. The look-top SpellDefinition
    /// lives in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "look at the top two cards of your library; put one into your
    /// hand and the other into your graveyard" <see cref="SpellDefinition"/>
    /// declaratively (the <c>look_top_n_one_to_hand_rest_to_graveyard</c> verb).
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new LookTopNOneToHandRestToGraveyardEffectDef { Amount = LookCount },
            });
}
