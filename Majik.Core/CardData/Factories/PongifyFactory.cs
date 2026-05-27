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
/// Named-card factory for Pongify (Time Spiral, {U}).
///
/// Instant. Oracle text:
///   "Destroy target creature. It can't be regenerated. Its controller
///    creates a 3/3 green Ape creature token."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {U}, owner / controller.
/// - <b>Destroy target creature — can't be regenerated</b> — single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolution the
///   target is destroyed (CR 701.7) via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
///   so Indestructible (CR 702.12) still cancels but the regeneration
///   shield (CR 701.15) is bypassed.
/// - <b>Its controller creates a 3/3 green Ape creature token</b> — the
///   controller at the moment of resolution (CR 608.2b — last-known
///   information) receives the token via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. CR 111.4 — green
///   colour identity stamped explicitly via
///   <see cref="TokenFactory.TokenSpec.Colors"/>; <see cref="CardSubtype.Ape"/>
///   already exists in the subtype enum.
/// - If the target is illegal at resolution (CR 608.2b) neither the
///   destroy nor the token occur — matches Beast Within's "token half is
///   gated on the destroy half" treatment for spells where the token
///   clause is parented to the destroy clause's "its controller".
/// </summary>
[CardName("Pongify")]
public static class PongifyFactory
{
    public const string CardName = "Pongify";
    public const string PrintedManaCost = "{U}";

    private static readonly TokenFactory.TokenSpec ApeTokenSpec =
        new(Name: "Ape", Power: 3, Toughness: 3,
            Subtypes: new[] { CardSubtype.Ape },
            // CR 105 / CR 111.4 — printed "3/3 green Ape creature token".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.Green });

    /// <summary>CardDef DSL — card shape only. Destroy + Ape-token body
    /// lives in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Pongify is cast.
    /// Single 1..1 "target creature" request; on resolution:
    /// <list type="number">
    ///   <item>Confirms the target is still a Creature on the battlefield
    ///     (CR 608.2b — illegal target → whole effect does nothing).</item>
    ///   <item>Snapshots the controller (CR 608.2b last-known-info — "its
    ///     controller" at the moment of resolution).</item>
    ///   <item>Destroys the target via
    ///     <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    ///     <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
    ///     (CR 701.7 + CR 701.15 — regeneration shield bypassed).</item>
    ///   <item>The destroyed creature's controller creates a 3/3 green Ape
    ///     creature token (CR 111.4 / CR 111.6 / <see cref="TokenFactory"/>).</item>
    /// </list>
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
                        $"{CardName}: destroy target creature + create 3/3 Ape token",
                        () =>
                        {
                            if (raw is not Creature target) return;

                            // CR 608.2b — resolution-time legality check.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Snapshot controller BEFORE moving — "its
                            // controller" = controller at the moment of
                            // resolution (CR 608.2b last-known-info).
                            var targetController = target.Controller ?? target.Owner;

                            // CR 701.7 — Destroy. "It can't be regenerated"
                            // rider honoured via DestroyNoRegeneration:
                            // indestructible (CR 702.12) still cancels;
                            // regeneration shield (CR 701.15) is bypassed.
                            OracleSpellBinder.MoveToGraveyard(target, Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);

                            // CR 111.4 / CR 111.6 — token creation. The
                            // controller of the destroyed creature gets a
                            // 3/3 green Ape creature token.
                            if (targetController == null) return;
                            TokenFactory.CreateOnBattlefield(ApeTokenSpec, targetController);
                        }),
                };
            });
    }
}
