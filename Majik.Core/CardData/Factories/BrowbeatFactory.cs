using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Browbeat (Onslaught / Mystery Booster, {2}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Any player may have Browbeat deal 5 damage to them. If no one does,
///    target player draws three cards."
///
/// The archetypal red "punisher" / Browbeat-cycle spell — the caster forces
/// a choice between two downsides for the opponent: take 5 (CR 119) or let
/// the targeted player (typically the caster) draw three cards (CR 121.1).
///
/// ## Card shape (from JSON)
/// The base shape (name, Sorcery, {2}{R}, red) is materialised from the
/// embedded JSON definition (<c>browbeat.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The cast-time spell body lives
/// in <see cref="BuildSpellDefinition"/> because the runtime needs both the
/// caller's target resolver and the live "all players" enumeration, which
/// the JSON <c>AbilityDefinition</c> schema does not express.
///
/// ## Target request
/// One target — "target player" (min 1 / max 1, CR 601.2c). The target is
/// the player who draws three cards if no one accepts the damage. Because
/// the spell always has a legal target (it does not say "may"), the target
/// is chosen on cast even though the draw is conditional (CR 608.2c — an
/// effect with a target only does as much as possible; here the draw is
/// gated behind the "if no one does" condition, CR 608.2g).
///
/// ## Resolution (CR 608.2)
/// On resolution the spell walks "any player" — every player in the game,
/// the caster included — and asks each, in turn order, "Have Browbeat deal
/// 5 damage to you?" via <see cref="IPlayerAgent.ChooseYesNoAsync"/>
/// (CR 601-style "may" choice; classified <see cref="BotIntent.LoseLife"/>
/// | <see cref="BotIntent.CostToDecline"/> so the default heuristic agent
/// declines — taking 5 to deny the caster three cards is situational, and
/// the conservative default is to keep life). Every player who accepts has
/// Browbeat (CR 119) deal 5 damage to them. "If no one does" — only when NO
/// player accepted does the targeted player draw three cards (CR 121.1).
///
/// "Any player" is sourced from <see cref="ChosenSpellParams.AllPlayers"/>,
/// which the spell-cast flow populates from the live game context
/// (<c>SpellCastFlow</c> threads <c>GameContext.AllPlayers</c> in). When the
/// list is absent (e.g. a bare unit test that omits it) the punisher walk is
/// empty — no player can accept, so the "if no one does" branch fires and the
/// target draws three (the all-decline default).
///
/// ## Bot intent
/// For the caster this is card advantage with an opponent escape valve:
/// either they draw three or the opponent burns 5 life. The opponent's
/// prompt is downside (lose 5 life), so the default agent declines and the
/// caster draws three. The target request is tagged <see cref="BotIntent.Draw"/>
/// so the heuristic bot targets the most card-hungry friendly player.
///
/// ## Deferred (v1 gaps)
/// - <b>Damage-source tracking</b>: <see cref="Fx.DealDamage"/> does not thread
///   Browbeat through as the damage source, matching the rest of the
///   "deal N damage to a player" family. No source-watching trigger observes
///   the 5 damage yet.
/// </summary>
[CardName("Browbeat")]
public static class BrowbeatFactory
{
    public const string CardName = "Browbeat";
    public const string Slug = "browbeat";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>Damage an accepting player has Browbeat deal to them (CR 119).</summary>
    public const int DamageAmount = 5;

    /// <summary>Cards the target player draws when no one accepts (CR 121.1).</summary>
    public const int DrawAmount = 3;

    /// <summary>
    /// Build a Browbeat Sorcery owned by <paramref name="owner"/> from the
    /// embedded JSON definition (name, Sorcery, {2}{R}, red). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to — shape only;
    /// the cast-time body is supplied by <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Browbeat is cast.
    ///
    /// Single "target player" request (1..1). On resolution, every player
    /// returned by <paramref name="playersResolver"/> is asked, in turn,
    /// whether to have Browbeat deal 5 damage to them; each accepting player
    /// takes 5 damage (CR 119). If NO player accepts, the resolved target
    /// player draws three cards (CR 121.1).
    /// </summary>
    /// <param name="targetResolver">Resolves the chosen target token to the
    /// live <see cref="Player"/> (pass <c>o =&gt; o</c> in tests that supply
    /// direct references).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // CR 601.2c — one player target, required.
                new TargetRequest("target player", 1, 1, Array.Empty<object>(), BotIntent.Draw),
            },
            EffectFactory: chosen =>
            {
                var resolved = targetResolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        "Browbeat — any player may take 5 damage; if no one does, target player draws three",
                        () =>
                        {
                            // "Any player" = every player in the game, sourced
                            // from the cast-time AllPlayers snapshot.
                            var players = chosen.AllPlayers
                                ?? Array.Empty<Player>();

                            // "Any player may have Browbeat deal 5 damage to
                            // them." CR 601-style "may" choice, asked in turn
                            // order. Multiple players may accept; each that
                            // does takes 5 (CR 119). Track whether ANY did.
                            var anyAccepted = false;
                            foreach (var p in players)
                            {
                                var agent = AgentRegistry.Get(p);
                                var accepts = agent?
                                    .ChooseYesNoAsync(
                                        $"Have {CardName} deal {DamageAmount} damage to you?",
                                        BotIntent.LoseLife | BotIntent.CostToDecline)
                                    .GetAwaiter().GetResult()
                                    ?? false; // No agent → decline.

                                if (!accepts) continue;

                                anyAccepted = true;
                                // CR 119 — Browbeat deals 5 damage to the
                                // accepting player (player → life loss).
                                Fx.DealDamage(p, DamageAmount);
                            }

                            // "If no one does, target player draws three
                            // cards." CR 608.2g — the draw is gated behind the
                            // "no one accepted" condition. CR 121.1 — three
                            // top-of-library draws. Empty library mid-draw
                            // marks the SBA loss (CR 704.5b).
                            if (anyAccepted) return;
                            if (resolved is not Player target) return;

                            for (var i = 0; i < DrawAmount; i++)
                            {
                                var top = target.Zones.Library.GetCards().FirstOrDefault();
                                if (top == null)
                                {
                                    target.MarkTriedToDrawFromEmptyLibrary();
                                    break;
                                }
                                target.Zones.Library.RemoveCard(top);
                                target.Zones.Hand.AddCard(top);
                                top.SetZone(ZoneType.Hand);
                            }
                        }),
                };
            });
    }
}
