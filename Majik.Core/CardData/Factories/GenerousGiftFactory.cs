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
/// Named-card factory for Generous Gift (Modern Horizons / reprints,
/// {2}{W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target permanent. Its controller creates a 3/3 green
///    Elephant creature token."
///
/// ## Why it gets its own factory
/// Generous Gift is the white analogue of Beast Within
/// (<see cref="BeastWithinFactory"/>): the exact same "destroy any
/// permanent, that permanent's controller gets a 3/3 green vanilla token"
/// template, only the token is an Elephant instead of a Beast and the
/// spell is white ({2}{W}) instead of green ({2}{G}). It reuses the
/// already-shipping destroy + <see cref="TokenFactory.CreateOnBattlefield"/>
/// primitives — no new engine mechanic is required.
/// <see cref="CardSubtype.Elephant"/> already exists in the subtype enum.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{W}, white. Card shape comes from the
///   embedded JSON (<c>generous-gift.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Destroy target permanent</b> — a single 1..1 "target permanent"
///   <see cref="TargetRequest"/>. Any permanent is a legal target — the
///   caster may target their own permanents, since the printed text has
///   no "opponent controls" restriction (CR 115.1 / CR 608.2b).
/// - <b>Destroyed permanent's controller creates a 3/3 Elephant token</b> —
///   the controller at the moment of resolution (CR 608.2b
///   last-known-information) receives the token via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. The token is a 3/3
///   green Elephant creature token (CR 111.4 — green stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/>; the token enters with
///   <c>HasSummoningSickness = true</c> via <see cref="TokenFactory"/>).
/// - If the target is illegal at resolution (CR 608.2b), neither the
///   destroy nor the token occur.
///
/// Indestructible (CR 702.12) and regeneration (CR 701.15) are honoured
/// at the destroy site via
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/>. The token
/// half of the spell is unconditional per printed wording and fires even
/// when the destroy is cancelled.
/// </summary>
[CardName("Generous Gift")]
public static class GenerousGiftFactory
{
    public const string CardName = "Generous Gift";
    public const string Slug = "generous-gift";
    public const string PrintedManaCost = "{2}{W}";

    private static readonly TokenFactory.TokenSpec ElephantTokenSpec =
        new(Name: "Elephant", Power: 3, Toughness: 3,
            Subtypes: new[] { CardSubtype.Elephant },
            // CR 105 / CR 111.4 — printed "3/3 green Elephant creature token".
            Colors: new[] { Majik.Core.ValueObjects.ManaColor.Green });

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Generous Gift is
    /// cast. Single 1..1 "target permanent" request; on resolution:
    /// <list type="number">
    ///   <item>Confirms the target is still on the battlefield (CR 608.2b
    ///     — if the target is illegal at resolution, the whole spell does
    ///     nothing).</item>
    ///   <item>Snapshots the target's controller (CR 608.2b
    ///     last-known-information — "its controller" at resolution).</item>
    ///   <item>Destroys the target via
    ///     <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7).</item>
    ///   <item>The destroyed permanent's controller creates a 3/3 Elephant
    ///     creature token (CR 111.6 / <see cref="TokenFactory"/>).</item>
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
                    "target permanent",
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
                        $"{CardName}: destroy target permanent + create 3/3 Elephant token",
                        () =>
                        {
                            if (raw is not Permanent target) return;

                            // CR 608.2b — resolution-time legality check.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Snapshot controller BEFORE moving the permanent —
                            // "its controller" refers to the controller at the
                            // moment of resolution (CR 608.2b last-known-info).
                            var targetController = target.Controller ?? target.Owner;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) are honoured by
                            // MoveToGraveyard via the Destroy-reason gate.
                            // The token half is unconditional per the printed
                            // oracle text — it fires even when indestructible
                            // cancels the destroy.
                            OracleSpellBinder.MoveToGraveyard(target, Majik.Core.Zones.ZoneMoveReason.Destroy);

                            // "Its controller creates a 3/3 green Elephant
                            // creature token." (CR 111.4 / CR 111.6). Green
                            // colour identity is stamped on the spec
                            // (CR 105 / CR 903.4).
                            if (targetController == null) return;
                            TokenFactory.CreateOnBattlefield(ElephantTokenSpec, targetController);
                        }),
                };
            });
    }
}
