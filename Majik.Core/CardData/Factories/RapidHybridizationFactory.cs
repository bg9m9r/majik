using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rapid Hybridization (Gatecrash, {U}).
///
/// Instant. Oracle text:
///   "Destroy target creature. It can't be regenerated. Its controller
///    creates a 3/3 green Frog Lizard creature token."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, owner / controller.
/// - <b>Destroy target creature — can't be regenerated</b> — single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolution the
///   target is destroyed (CR 701.7) via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>:
///   indestructible (CR 702.12) still cancels but the regeneration shield
///   (CR 701.15) is bypassed. Same engine shape as <see cref="PongifyFactory"/>.
/// - <b>Its controller creates a 3/3 green Frog Lizard creature token</b>
///   — created via <see cref="TokenFactory.CreateOnBattlefield"/> with
///   subtypes <see cref="CardSubtype.Frog"/> + <see cref="CardSubtype.Lizard"/>
///   and explicit green colour (CR 105 / CR 111.4).
/// - If the target is illegal at resolution (CR 608.2b) neither the
///   destroy nor the token occur (mirrors Beast Within / Pongify).
/// </summary>
[CardName("Rapid Hybridization")]
public static class RapidHybridizationFactory
{
    public const string CardName = "Rapid Hybridization";
    public const string PrintedManaCost = "{U}";

    private static readonly TokenFactory.TokenSpec FrogLizardTokenSpec =
        new(Name: "Frog Lizard", Power: 3, Toughness: 3,
            Subtypes: new[] { CardSubtype.Frog, CardSubtype.Lizard },
            // CR 105 / CR 111.4 — printed "3/3 green Frog Lizard creature token".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.Green });

    /// <summary>CardDef DSL — card shape only. Destroy + Frog-Lizard-token
    /// body lives in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Rapid Hybridization
    /// is cast. Same shape as <see cref="PongifyFactory.BuildSpellDefinition"/>;
    /// the only delta is the token spec (Frog Lizard vs. Ape).
    /// </summary>
    /// <param name="resolver">Resolves the raw target token to a live
    /// engine object (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature + create 3/3 Frog Lizard token",
                        () =>
                        {
                            if (raw is not Creature target) return;

                            // CR 608.2b — resolution-time legality check.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 608.2b last-known-info — controller snapshot
                            // before the zone move.
                            var targetController = target.Controller ?? target.Owner;

                            // CR 701.7 — Destroy + "can't be regenerated" rider
                            // (CR 701.15 bypassed; CR 702.12 indestructible
                            // still cancels).
                            OracleSpellBinder.MoveToGraveyard(target, Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);

                            // CR 111.4 / CR 111.6 — token creation under the
                            // destroyed creature's controller.
                            if (targetController == null) return;
                            TokenFactory.CreateOnBattlefield(FrogLizardTokenSpec, targetController);
                        }),
                };
            });
    }
}
