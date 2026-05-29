using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the ICE half of the split card Fire // Ice
/// (Apocalypse / various reprints, {1}{R} // {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Tap target permanent.
///    Draw a card."
///
/// Sister half — <see cref="FireFactory"/> ({1}{R}; "Fire deals 2 damage
/// divided as you choose among one or two targets.").
///
/// ## Split-card modelling (CR 712 / CR 709)
///
/// See <see cref="FireFactory"/> for the split-card posture. Each half is its
/// own <c>[CardName]</c>-dispatched factory; the combined seed row
/// <c>"Fire // Ice"</c> flips <c>IsImplemented</c> off the FRONT half
/// (<c>"Fire"</c>) via <see cref="EmbeddedCardRepository"/>. The Ice half
/// carries an <see cref="MdfcState"/> pre-flipped to the back half so the
/// front half's name (Fire) is still observable from the Ice object — the
/// same informational role MdfcState plays for the Soporific Springs back
/// face (<see cref="SoporificSpringsFactory"/>).
///
/// ## Implemented (v1)
/// - Instant identity at {1}{U} (blue, mana value 2), built from the embedded
///   JSON def (<c>ice.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back half (Ice).
/// - <b>Tap</b> — single 1..1 "target permanent" request (any permanent type,
///   including lands — CR 701.27); the candidate gatherer enumerates every
///   permanent on the battlefield (same broad pool as
///   <see cref="BoomerangFactory"/>). On resolution the live target is tapped
///   via <see cref="Fx.Tap"/> when it is still a permanent on the battlefield
///   (CR 608.2b illegal-target re-check); an illegal tap target is skipped.
/// - <b>Draw</b> — the CASTER then draws one card (CR 121.1) via
///   <see cref="Fx.DrawCards"/>. CR 608.2c — "Draw a card." is a separate,
///   untargeted instruction, so it resolves regardless of whether the tap
///   target is still legal. Empty-library draw flags the SBA loss
///   (CR 704.5b) via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///   rather than throwing.
/// </summary>
[CardName("Ice")]
public static class IceFactory
{
    public const string CardName = "Ice";
    public const string SisterName = "Fire";
    public const string Slug = "ice";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>
    /// Build the Ice half as an Instant from the embedded JSON def, with the
    /// <see cref="MdfcState"/> face tracker attached, pre-flipped to the back
    /// half so the front half's name (Fire) stays observable.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 712 — split-card face tracker. Front = Fire, back = Ice; this
        // half is constructed pre-flipped to the back (Ice). Informational
        // only, matching the SoporificSpringsFactory posture.
        var mdfc = new MdfcState(SisterName, CardName);
        mdfc.Transform(); // flip to the back half (Ice)
        card.MdfcState = mdfc;
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Ice is cast. One
    /// 1..1 "target permanent" request, no X. On resolution:
    ///   1. Tap the chosen permanent if it is still a legal target
    ///      (CR 701.27 / CR 608.2b).
    ///   2. The caster draws one card (CR 121.1) — an untargeted instruction
    ///      that resolves regardless of the tap target's legality (CR 608.2c).
    /// </summary>
    /// <param name="caster">The player casting Ice; receives the draw.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    Fx.Inline("Ice: tap target permanent, then draw a card", () =>
                    {
                        // CR 701.27 / CR 608.2b — tap the target if it is
                        // still a permanent on the battlefield.
                        var live = resolver(raw);
                        if (live is Permanent perm && perm.Zone == ZoneType.Battlefield)
                        {
                            Fx.Tap(perm);
                        }

                        // CR 121.1 / CR 608.2c — the untargeted draw resolves
                        // regardless of the tap target's legality.
                        Fx.DrawCards(caster, 1);
                    }),
                };
            });
    }
}
