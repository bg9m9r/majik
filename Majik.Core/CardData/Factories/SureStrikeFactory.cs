using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sure Strike (Bloomburrow / reprints, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature gets +3/+0 and gains first strike until end of turn.
///    (It deals combat damage before creatures without first strike.)"
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>sure-strike.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>.
///
/// The resolve-time body lives in <see cref="BuildSpellDefinition"/> because it
/// declares one 1..1 "target creature" request and needs the caller's target
/// resolver — neither is expressible in the data-only JSON schema. No modes,
/// no X. Same posture as <see cref="ViolentUrgeFactory"/> (the +1/+0 +
/// first-strike analogue) minus the delirium rider.
///
/// On resolution (CR 608.2b — resolve-time target legality is enforced by
/// <see cref="SpellCastFlow"/>; the closure additionally guards against a
/// non-Creature resolver result / a missing continuous-effects service so the
/// effect is a clean no-op rather than an NRE):
///   1. CR 613.1c Layer 7c — register a <see cref="PumpUntilEndOfTurnEffect"/>
///      granting +3/+0.
///   2. CR 613.1c Layer 6 — register a
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting First strike
///      (CR 702.7).
/// Both expire at the cleanup step (CR 514.2).
/// </summary>
[CardName("Sure Strike")]
public static class SureStrikeFactory
{
    public const string CardName = "Sure Strike";
    public const string Slug = "sure-strike";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>+P pump magnitude. Sure Strike prints +3/+0.</summary>
    public const int PumpPower = 3;

    /// <summary>+T pump magnitude. Sure Strike prints +3/+0.</summary>
    public const int PumpToughness = 0;

    /// <summary>Granted keyword — CR 702.7 First strike.</summary>
    public const string GrantedFirstStrike = "First strike";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Sure Strike is cast.
    /// Single 1..1 "target creature" request; on resolution the targeted
    /// creature gets +3/+0 and gains First strike until end of turn.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Sure Strike: +3/+0 and first strike until end of turn", () =>
                    {
                        // CR 608.2b — illegal-target defensive guard. If the
                        // resolver returns a non-Creature (zone-change /
                        // type-loss / wrong resolver), or the target has no
                        // live continuous-effects service wired (shape-only
                        // tests), the spell does nothing.
                        if (raw is not Creature target) return;
                        if (target.ActiveEffects == null) return;

                        // CR 613.1c Layer 7c — +3/+0 pump.
                        target.ActiveEffects.Register(
                            new PumpUntilEndOfTurnEffect(target, PumpPower, PumpToughness));

                        // CR 613.1c Layer 6 — keyword grant: First strike (CR 702.7).
                        target.ActiveEffects.Register(
                            new GrantKeywordUntilEndOfTurnEffect(target, GrantedFirstStrike));
                    }),
                };
            });
    }
}
