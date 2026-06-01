using Majik.Core.Abilities;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Khalni Ambush // Khalni Territory (Zendikar Rising, {2}{G}).
///
/// Instant. Oracle text (front):
///   "Target creature you control fights target creature you don't control.
///    (Each deals damage equal to its power to the other.)"
///
/// Back face — <see cref="KhalniTerritoryFactory"/> (Land —
/// "This land enters tapped." / "{T}: Add {G}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face modelled by two independent <c>[CardName]</c>-dispatched
/// factories — same architecture as <see cref="BalaGedRecoveryFactory"/> /
/// <see cref="BalaGedSanctuaryFactory"/> (MDFC spell-front + tapland-back).
/// Casting the front face resolves "Khalni Ambush" → this factory → an
/// <see cref="Instant"/> with the fight effect. Playing the back face
/// resolves "Khalni Territory" → <see cref="KhalniTerritoryFactory"/> → a
/// simple tapland.
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{2}{G}</c>, mono-green, owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Khalni Ambush",
///   back = "Khalni Territory"); starts on the front face.
/// - Two 1..1 creature target requests — "target creature you control" +
///   "target creature you don't control". Control-filter legality is the
///   cast flow's concern (same posture as
///   <see cref="SpellTemplates.Templates.Damage.FightTemplate"/>); the
///   resolved effect does not re-enforce it.
/// - Resolution — CR 701.13 Fight: each creature deals damage equal to its
///   power to the other simultaneously (CR 701.13a). Both powers are read
///   BEFORE any damage applies so a power-reducing interaction on one does
///   not change the other's incoming damage. Mirrors
///   <see cref="SpellTemplates.Templates.Damage.FightTemplate"/>'s body.
/// - CR 608.2b — a target that is no longer a creature on the battlefield at
///   resolution causes the fight to do nothing (clean no-op).
///
/// ## References
///
/// - <see cref="SpellTemplates.Templates.Damage.FightTemplate"/> — the
///   CR 701.13 single-clause fight primitive (Prey Upon, Pit Fight, Pounce);
///   this factory cribs its mutual-damage resolution body.
/// - <see cref="BalaGedRecoveryFactory"/> / <see cref="BalaGedSanctuaryFactory"/>
///   — companion MDFC spell-front + tapland-back pair showing the same
///   two-factory architecture.
/// </summary>
[CardName("Khalni Ambush")]
public static class KhalniAmbushFactory
{
    public const string CardName = "Khalni Ambush";
    public const string BackName = "Khalni Territory";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "Target creature you control fights target creature you don't control. " +
        "(Each deals damage equal to its power to the other.)";

    /// <summary>
    /// Construct the front face of Khalni Ambush as an Instant with owner /
    /// controller wired and the <see cref="MdfcState"/> face tracker attached
    /// (starts on the front face). Suitable for identity / shape / dispatcher
    /// tests. The resolve-time <see cref="SpellDefinition"/> is built on demand
    /// via <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed back-face
        // name (Khalni Territory) is observable from the front-face card
        // object. Starts on the front face.
        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (deferral #3, real cast-either-face). The
        // back face is the LAND back face played with no stack; MdfcCastFlow
        // offers the controller a face choice at cast time and materializes
        // a fresh back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                KhalniTerritoryFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);
        return card;
    }

    /// <summary>
    /// Build the resolve-time "target creature you control fights target
    /// creature you don't control" <see cref="SpellDefinition"/> (CR 701.13).
    ///
    /// Two 1..1 creature target requests; the first is the controller's
    /// creature, the second the opponent's. Control-filter legality is
    /// validated by the cast flow, not re-checked here (same posture as
    /// <see cref="SpellTemplates.Templates.Damage.FightTemplate"/>).
    /// </summary>
    /// <param name="resolver">Maps each agent-supplied raw target token to the
    /// live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
                new TargetRequest(
                    Description: "target creature you don't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            },
            EffectFactory: chosen =>
            {
                object? rawA = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? chosen.Targets[0][0]
                    : null;
                object? rawB = chosen.Targets.Count > 1 && chosen.Targets[1].Count > 0
                    ? chosen.Targets[1][0]
                    : null;

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: fight",
                        () => ResolveFight(rawA, rawB, resolver)),
                };
            });
    }

    /// <summary>
    /// CR 701.13a — each creature deals damage equal to its power to the other
    /// simultaneously. Both powers are read BEFORE any damage applies so a
    /// power-reducing effect on one creature does not change the other's
    /// incoming damage. A target that is no longer a battlefield creature at
    /// resolution causes the whole fight to do nothing (CR 608.2b).
    /// </summary>
    private static void ResolveFight(
        object? rawA,
        object? rawB,
        Func<object, object> resolver)
    {
        if (rawA == null || rawB == null) return;

        var a = resolver(rawA);
        var b = resolver(rawB);

        // CR 608.2b — both targets must still be creatures on the battlefield;
        // otherwise the fight does nothing.
        if (a is not Creature ca || ca.Zone != ZoneType.Battlefield) return;
        if (b is not Creature cb || cb.Zone != ZoneType.Battlefield) return;

        // CR 701.13a — read both powers up front, then apply simultaneously.
        var aPower = ca.Power;
        var bPower = cb.Power;
        if (aPower > 0) cb.TakeDamage(aPower);
        if (bPower > 0) ca.TakeDamage(bPower);
    }
}
