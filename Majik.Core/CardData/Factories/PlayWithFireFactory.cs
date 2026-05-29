using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Play with Fire (Midnight Hunt, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Play with Fire deals 2 damage to any target. If a player is dealt
///    damage this way, scry 1. (Look at the top card of your library. You
///    may put that card on the bottom.)"
///
/// ## Implementation
///
/// Same <see cref="Fx.DealDamageAny"/> + <see cref="ScryAction"/> shape as
/// <see cref="MagmaJetFactory"/>, reduced to 2 damage / scry 1, with the
/// scry gated on "a player is dealt damage this way" (CR 608.2 — conditional
/// follow-up clause). Magma Jet scrys unconditionally; Play with Fire only
/// scrys when the chosen target is a player.
///
/// Card shape comes from the embedded JSON (<c>play-with-fire.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
///
/// On resolution (CR 608.2e — left-to-right clause ordering):
///   1. Deal 2 damage to the chosen target (creature, player, planeswalker,
///      battle) via <see cref="Fx.DealDamageAny(object, int)"/>
///      (CR 119 / CR 120.3).
///   2. CR 608.2 conditional — only if the target the damage was dealt to is
///      a <see cref="Player"/>, the caster scrys 1 (CR 701.20). A creature /
///      planeswalker / battle target means no player was dealt damage this
///      way, so the scry is skipped. The controller's registered
///      <see cref="IPlayerAgent"/> is consulted when present; the pre-agent
///      default sends the peeked card to the bottom of the library (matching
///      the <see cref="MagmaJetFactory"/> fallback posture).
/// </summary>
[CardName("Play with Fire")]
public static class PlayWithFireFactory
{
    public const string CardName = "Play with Fire";
    public const string Slug = "play-with-fire";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119 — fixed 2 damage to any target.</summary>
    public const int Damage = 2;

    /// <summary>CR 701.20 — scry 1 when a player was dealt the damage.</summary>
    private const int ScryAmount = 1;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Play with Fire is
    /// cast. Single 1..1 "any target" request, no X. On resolution:
    ///   1. Deals <see cref="Damage"/> (2) damage to the chosen target via
    ///      <see cref="Fx.DealDamageAny"/> (CR 120.3).
    ///   2. If that target was a player, the caster scrys 1 (CR 701.20).
    /// </summary>
    /// <param name="caster">The player who cast Play with Fire; receives the
    /// scry when a player was dealt damage.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Play with Fire: 2 damage to any target, scry 1 if a player was dealt damage", () =>
                    {
                        // CR 120.3 / CR 608.2e step 1 — deal 2 damage.
                        Fx.DealDamageAny(target, Damage);

                        // CR 608.2 conditional — "If a player is dealt damage
                        // this way, scry 1." Only a Player target satisfies
                        // this; creature / planeswalker / battle targets do
                        // not, so the scry is skipped.
                        if (target is not Player) return;

                        // CR 701.20 / CR 608.2e step 2 — scry 1 for the caster.
                        var peeked = ScryAction.Peek(caster, ScryAmount);
                        if (peeked.Count == 0)
                        {
                            return; // empty library — scry short-circuits cleanly.
                        }

                        var agent = AgentRegistry.Get(caster);
                        ScryAction.ScryDecision decision;
                        if (agent != null)
                        {
                            // TODO: drop sync-over-async once IEffect.Execute becomes async.
                            decision = agent.ChooseScryDecisionAsync(null, peeked)
                                .GetAwaiter().GetResult();
                        }
                        else
                        {
                            // Pre-agent default: send the peeked card to the bottom.
                            decision = new ScryAction.ScryDecision(
                                ToBottom: peeked.ToList(),
                                TopOrder: Array.Empty<ICard>());
                        }

                        ScryAction.Apply(caster, peeked.Count, decision);
                    }),
                };
            });
    }
}
