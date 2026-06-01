using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Vastwood Fortification // Vastwood Thicket (Modern Horizons 3, {G}).
///
/// Instant. Oracle text (front, verified against Scryfall):
///   "Put a +1/+1 counter on target creature."
///
/// Back face — <see cref="VastwoodThicketFactory"/> (Land —
/// "This land enters tapped." / "{T}: Add {G}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="BalaGedRecoveryFactory"/> / <see cref="BalaGedSanctuaryFactory"/>
/// (MDFC spell-front + tapland-back). Casting the front face resolves
/// "Vastwood Fortification" → this factory → an <see cref="Instant"/> with the
/// +1/+1-counter effect; playing the back face resolves "Vastwood Thicket" →
/// <see cref="VastwoodThicketFactory"/> → a simple {G} tapland.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>vastwood-fortification.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time effect are attached in code (the JSON schema
/// models neither MDFC faces nor target-and-place-counter resolution).
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{G}</c>, mono-green, owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Vastwood Fortification",
///   back = "Vastwood Thicket"); starts on the front face.
/// - <see cref="BuildDefinition"/> returns a no-X <see cref="SpellDefinition"/>
///   with one 1..1 "target creature" request (CR 115.1 / CR 700.6) whose
///   single effect closure puts one +1/+1 counter on the chosen creature
///   (CR 122 — <see cref="Fx.PlaceCounter"/>).
/// - CR 608.2b — an illegal-on-resolution target (no longer a creature on the
///   battlefield) is a clean no-op.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real agent-driven target prompt</b>: production callers wire the
///   chosen target from an agent prompt before the spell resolves; the
///   resolver maps the agent-supplied token to the live object (same posture
///   as <see cref="BalaGedRecoveryFactory"/>).
///
/// ## References
///
/// - <see cref="BalaGedRecoveryFactory"/> / <see cref="BalaGedSanctuaryFactory"/>
///   — companion MDFC spell-front + tapland-back pair showing the same
///   two-factory architecture (this factory mirrors its JSON-identity +
///   MdfcState shape).
/// - <see cref="TurntimberSymbiosisFactory"/> — same
///   <see cref="Fx.PlaceCounter"/> +1/+1-counter primitive.
/// </summary>
[CardName("Vastwood Fortification")]
public static class VastwoodFortificationFactory
{
    public const string CardName = "Vastwood Fortification";
    public const string BackName = "Vastwood Thicket";

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText = "Put a +1/+1 counter on target creature.";

    /// <summary>
    /// Construct Vastwood Fortification as an Instant (identity from JSON)
    /// with the <see cref="MdfcState"/> face tracker attached (starts on the
    /// front face). The resolve-time <see cref="SpellDefinition"/> is built
    /// on demand via <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("vastwood-fortification");
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Vastwood Thicket) is observable from the front-face
        // card object. Starts on the front face.
        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (deferral #3, real cast-either-face). The
        // back face is the LAND back face played with no stack; MdfcCastFlow
        // offers the controller a face choice at cast time and materializes
        // a fresh back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                VastwoodThicketFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time "put a +1/+1 counter on target creature"
    /// <see cref="SpellDefinition"/>.
    ///
    /// Single 1..1 "target creature" request (CR 115.1). On resolution one
    /// +1/+1 counter is placed on the chosen creature (CR 122); an
    /// illegal-on-resolution target (not a creature on the battlefield) is a
    /// clean no-op (CR 608.2b).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            },
            EffectFactory: chosen =>
            {
                object? rawTarget = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? chosen.Targets[0][0]
                    : null;

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: put a +1/+1 counter on target creature",
                        () => ResolvePlaceCounter(rawTarget, targetResolver)),
                };
            });
    }

    /// <summary>
    /// Resolution helper. Resolves the chosen target to a live creature and
    /// places one +1/+1 counter on it (CR 122). CR 608.2b — a target that is
    /// not a creature on the battlefield at resolution is a clean no-op.
    /// </summary>
    private static void ResolvePlaceCounter(
        object? rawTarget,
        Func<object, object> targetResolver)
    {
        if (rawTarget == null) return;

        var live = targetResolver(rawTarget);

        // CR 608.2b — the target must still be a creature on the battlefield.
        if (live is Creature creature && creature.Zone == ZoneType.Battlefield)
        {
            Fx.PlaceCounter(creature, CounterType.PlusOnePlusOne, 1);
        }
    }
}
