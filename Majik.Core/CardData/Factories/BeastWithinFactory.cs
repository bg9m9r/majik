using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Beast Within (New Phyrexia / various reprints,
/// {2}{G}).
///
/// Instant. Oracle text:
///   "Destroy target permanent. Its controller creates a 3/3 green Beast
///    creature token."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{G}, owner / controller.
/// - <b>Destroy target permanent</b> — a single 1..1 "target permanent"
///   <see cref="TargetRequest"/>. Any permanent is a legal target —
///   the caster may target their own permanents, unlike Assassin's Trophy
///   (CR 115.1 / CR 608.2b).
/// - <b>Destroyed permanent's controller creates a 3/3 Beast token</b> —
///   the controller at the moment of resolution (CR 608.2b
///   last-known-information) receives the token via
///   <see cref="TokenFactory.CreateOnBattlefield"/>. The token is a
///   3/3 green Beast creature token (CR 111.4 — token characteristics
///   include colour; token "green" colour identity deferred — same gap as
///   Crashing Footfalls' green Rhinos; token enters with
///   <c>HasSummoningSickness = true</c> via <see cref="TokenFactory"/>).
///   <see cref="CardSubtype.Beast"/> already exists in the subtype enum.
/// - If the target is illegal at resolution (CR 608.2b), neither the
///   destroy nor the token occur.
///
/// ## Deferred (v1 gaps)
/// - <b>Token colour (green)</b>: TokenFactory does not yet model token
///   colour identity (same gap as Pact of the Titan's "red" Giant token,
///   Crashing Footfalls' "green" Rhino tokens).
/// - <b>Indestructible / regeneration</b>: the destroy call moves the
///   permanent to the graveyard without checking for Indestructible or
///   an active regeneration shield (same gap as every other single-target
///   destroy template — Terminate, Abrupt Decay, Slaughter Pact).
/// </summary>
public static class BeastWithinFactory
{
    public const string CardName = "Beast Within";
    public const string PrintedManaCost = "{2}{G}";

    private static readonly TokenFactory.TokenSpec BeastTokenSpec =
        new(Name: "Beast", Power: 3, Toughness: 3,
            Subtypes: new[] { CardSubtype.Beast });

    /// <summary>
    /// Construct the Beast Within card shape (Instant, {2}{G}).
    /// Resolve behaviour is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Beast Within is
    /// cast. Single 1..1 "target permanent" request; on resolution:
    /// <list type="number">
    ///   <item>Confirms the target is still on the battlefield (CR 608.2b
    ///     — if the target is illegal at resolution, the whole spell does
    ///     nothing).</item>
    ///   <item>Snapshots the target's controller (CR 608.2b
    ///     last-known-information — "its controller" at resolution).</item>
    ///   <item>Destroys the target via
    ///     <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7).</item>
    ///   <item>The destroyed permanent's controller creates a 3/3 Beast
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
                        $"{CardName}: destroy target permanent + create 3/3 Beast token",
                        () =>
                        {
                            if (raw is not Permanent target) return;

                            // CR 608.2b — resolution-time legality check.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Snapshot controller BEFORE moving the permanent —
                            // "its controller" refers to the controller at the
                            // moment of resolution (CR 608.2b last-known-info).
                            var targetController = target.Controller ?? target.Owner;

                            // CR 701.7 — Destroy. Indestructible / regeneration
                            // rider deferred (same gap as Terminate / Abrupt Decay /
                            // Slaughter Pact).
                            OracleSpellBinder.MoveToGraveyard(target);

                            // "Its controller creates a 3/3 green Beast creature
                            // token." (CR 111.4 / CR 111.6). Token colour (green)
                            // deferred — same gap as Pact of the Titan / Crashing
                            // Footfalls.
                            if (targetController == null) return;
                            TokenFactory.CreateOnBattlefield(BeastTokenSpec, targetController);
                        }),
                };
            });
    }
}
