using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Risk Factor (Guilds of Ravnica, {2}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target opponent may have Risk Factor deal 4 damage to them. If that
///    player doesn't, you draw three cards.
///    Jump-start (You may cast this card from your graveyard by discarding a
///    card in addition to paying its other costs. Then exile this card.)"
///
/// A two-sided "punisher" in the Browbeat family (CR 119 / CR 121.1), but
/// the choice is offered to a SINGLE targeted opponent rather than "any
/// player", and the caster (not a chosen target) draws when the opponent
/// declines. The Jump-start keyword (CR 702.133) lets the caster recast it
/// once from the graveyard for its printed cost plus a discarded card, then
/// exiles it.
///
/// ## Card shape (from JSON)
/// The base shape (name, Instant, {2}{R}, red) is materialised from the
/// embedded JSON definition (<c>risk-factor.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The cast-time spell body lives
/// in <see cref="BuildSpellDefinition"/> because the runtime needs both the
/// caller's target resolver and the live caster reference, which the JSON
/// <c>AbilityDefinition</c> schema does not express.
///
/// ## Target request
/// One target — "target opponent" (min 1 / max 1, CR 601.2c). The spell
/// always has a legal target in a two-player game (it does not say "may"),
/// so the target is chosen on cast even though the draw is conditional
/// (CR 608.2g — the draw is gated behind "if that player doesn't").
///
/// ## Resolution (CR 608.2)
/// On resolution the targeted opponent is asked, "Have Risk Factor deal 4
/// damage to you?" via <see cref="IPlayerAgent.ChooseYesNoAsync"/>
/// (CR 601-style "may" choice; classified <see cref="BotIntent.LoseLife"/> |
/// <see cref="BotIntent.CostToDecline"/> so the default heuristic agent
/// declines — taking 4 to deny the caster three cards is situational). If the
/// opponent accepts, Risk Factor (CR 119) deals 4 damage to them and the
/// caster draws nothing. "If that player doesn't" — only when the opponent
/// DECLINES does the CASTER draw three cards (CR 121.1).
///
/// ## Jump-start (CR 702.133)
/// <see cref="BuildJumpStartCost"/> returns the alternative-cost pair used to
/// recast Risk Factor from the graveyard: a <see cref="FlashbackAlternativeCost"/>
/// keyed to the printed mana cost (CR 702.133a — Jump-start uses the printed
/// cost, unlike Flashback's bespoke cost; both exile the card after it leaves
/// the stack, CR 702.133b) plus a <see cref="DiscardACardAdditionalCost"/>
/// (CR 702.133a — "by discarding a card in addition to paying its other
/// costs"). Callers wire the returned <see cref="FlashbackAlternativeCost"/>
/// as the <c>alternativeCost</c> and the
/// <see cref="DiscardACardAdditionalCost"/> as an <c>additionalCost</c> to
/// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>; the existing
/// alt-cost + additional-cost paths handle payment and the post-resolution
/// exile. (The printed-cost flashback + discard composition is exactly the
/// engine surface Jump-start requires — no new mechanic.)
///
/// ## Bot intent
/// For the caster this is card advantage with an opponent escape valve:
/// either they draw three or the opponent burns 4 life. The opponent's
/// prompt is downside (lose 4 life), so the default agent declines and the
/// caster draws three. The target request is tagged
/// <see cref="BotIntent.Draw"/> so the heuristic bot still values it.
///
/// ## Deferred (v1 gaps)
/// - <b>Damage-source tracking</b>: <see cref="Fx.DealDamage"/> does not
///   thread Risk Factor through as the damage source, matching the rest of
///   the "deal N damage to a player" family.
/// </summary>
[CardName("Risk Factor")]
public static class RiskFactorFactory
{
    public const string CardName = "Risk Factor";
    public const string Slug = "risk-factor";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>Damage the accepting opponent has Risk Factor deal to them (CR 119).</summary>
    public const int DamageAmount = 4;

    /// <summary>Cards the caster draws when the opponent declines (CR 121.1).</summary>
    public const int DrawAmount = 3;

    /// <summary>
    /// Build a Risk Factor Instant owned by <paramref name="owner"/> from the
    /// embedded JSON definition (name, Instant, {2}{R}, red). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to — shape only;
    /// the cast-time body is supplied by <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Risk Factor is cast.
    ///
    /// Single "target opponent" request (1..1). On resolution the targeted
    /// opponent is asked whether to have Risk Factor deal 4 damage to them;
    /// if they accept they take 4 damage (CR 119) and the caster draws
    /// nothing. If they decline, the CASTER draws three cards (CR 121.1).
    /// </summary>
    /// <param name="caster">The player who cast Risk Factor — the one who
    /// draws three when the opponent declines.</param>
    /// <param name="targetResolver">Resolves the chosen target token to the
    /// live <see cref="Player"/> (pass <c>o =&gt; o</c> in tests that supply
    /// direct references).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // CR 601.2c — one opponent target, required.
                new TargetRequest("target opponent", 1, 1, Array.Empty<object>(), BotIntent.Draw),
            },
            EffectFactory: chosen =>
            {
                var resolved = targetResolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        "Risk Factor — target opponent may take 4 damage; if they don't, you draw three",
                        () =>
                        {
                            if (resolved is not Player opponent) return;

                            // "Target opponent may have Risk Factor deal 4
                            // damage to them." CR 601-style "may" choice,
                            // offered only to the targeted opponent. Default
                            // agent declines (downside life loss).
                            var agent = AgentRegistry.Get(opponent);
                            var accepts = agent?
                                .ChooseYesNoAsync(
                                    $"Have {CardName} deal {DamageAmount} damage to you?",
                                    BotIntent.LoseLife | BotIntent.CostToDecline)
                                .GetAwaiter().GetResult()
                                ?? false; // No agent → decline.

                            if (accepts)
                            {
                                // CR 119 — Risk Factor deals 4 damage to the
                                // accepting opponent. The caster draws nothing.
                                Fx.DealDamage(opponent, DamageAmount);
                                return;
                            }

                            // "If that player doesn't, you draw three cards."
                            // CR 608.2g / CR 121.1 — the CASTER draws three.
                            // Empty library mid-draw marks the SBA loss
                            // (CR 704.5b).
                            for (var i = 0; i < DrawAmount; i++)
                            {
                                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                                if (top == null)
                                {
                                    caster.MarkTriedToDrawFromEmptyLibrary();
                                    break;
                                }
                                caster.Zones.Library.RemoveCard(top);
                                caster.Zones.Hand.AddCard(top);
                                top.SetZone(ZoneType.Hand);
                            }
                        }),
                };
            });
    }

    /// <summary>
    /// Build the Jump-start (CR 702.133) cost pair used to recast Risk Factor
    /// from the graveyard. Returns:
    /// <list type="bullet">
    ///   <item><description>a <see cref="FlashbackAlternativeCost"/> keyed to
    ///   the printed mana cost (CR 702.133a — Jump-start pays the card's
    ///   printed cost, and CR 702.133b exiles the card after it leaves the
    ///   stack, exactly as Flashback does); pass as the
    ///   <c>alternativeCost</c>.</description></item>
    ///   <item><description>a <see cref="DiscardACardAdditionalCost"/>
    ///   (CR 702.133a — "by discarding a card in addition to paying its other
    ///   costs"); pass inside the <c>additionalCosts</c> list.</description></item>
    /// </list>
    /// Both are consumed by <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>.
    /// </summary>
    public static (FlashbackAlternativeCost GraveyardCast, DiscardACardAdditionalCost Discard)
        BuildJumpStartCost()
    {
        var graveyardCast = new FlashbackAlternativeCost(ManaCost.Parse(PrintedManaCost));
        var discard = new DiscardACardAdditionalCost();
        return (graveyardCast, discard);
    }
}
