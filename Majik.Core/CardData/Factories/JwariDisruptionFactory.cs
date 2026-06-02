using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Jwari Disruption // Jwari Ruins (Zendikar Rising, {1}{U}).
///
/// Instant. Oracle text (front, verified against Scryfall):
///   "Counter target spell unless its controller pays {1}."
///
/// Back face — <see cref="JwariRuinsFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {U}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6) — real cast-either-face
///
/// Cast-either-face is modelled exactly like the structurally identical
/// Zendikar Rising blue instant // tapland MDFC
/// <see cref="SilundiVisionFactory"/> / <see cref="SilundiIsleFactory"/> and
/// the Bloomburrow <see cref="SinkIntoStuporFactory"/> /
/// <see cref="SoporificSpringsFactory"/>: two independent
/// <c>[CardName]</c>-dispatched factories. The front-face card built here
/// carries an <see cref="MdfcState"/> with a castable <see cref="MdfcFace"/>
/// back-face descriptor; at cast time <see cref="MdfcCastFlow"/> offers the
/// controller a face choice and, when the back (land) face is chosen,
/// materializes a fresh <see cref="JwariRuinsFactory"/> land instance with no
/// stack (CR 305). No transform happens (CR 712.4).
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>jwari-disruption.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time counter behaviour are attached in code (the
/// JSON schema models neither MDFC faces nor targeted counter effects).
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{1}{U}</c>, mono-blue (one {U} pip),
///   owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Jwari Disruption",
///   back = "Jwari Ruins"); starts on the front face.
/// - <b>Counter target spell unless its controller pays {1}</b> — same
///   "auto-pay-if-able" posture as <see cref="QuenchFactory"/> /
///   <see cref="ManaLeakFactory"/> / <see cref="MysticalDisputeFactory"/>:
///   at resolution the engine checks whether the target spell's controller
///   has {1} available in their mana pool; if yes, it is spent automatically
///   and the counter no-ops (CR 118.4 — "unless" cost). If no, the spell is
///   countered via <see cref="OracleSpellBinder.RemoveFromStack"/> and its
///   card goes to the graveyard (CR 701.5).
///
/// ## Deferred (v1 gaps)
///
/// - Real "do you want to pay {1}?" agent prompt — same queue as Quench /
///   Daze / Mana Leak / Mystical Dispute. v1 is deterministic: "pay if able."
///
/// ## References
///
/// - <see cref="QuenchFactory"/> — the functionally identical "{1}{U}:
///   Counter target spell unless its controller pays {1}" body this directly
///   cribs.
/// - <see cref="SilundiVisionFactory"/> — companion Zendikar Rising blue
///   instant // tapland MDFC front face with the same MdfcState shape.
/// </summary>
[CardName("Jwari Disruption")]
public static class JwariDisruptionFactory
{
    public const string CardName = "Jwari Disruption";
    public const string BackName = "Jwari Ruins";

    /// <summary>Pay-or-counter rider (CR 118.4 — "unless its controller pays {1}").</summary>
    public const int UnlessPayGeneric = 1;

    /// <summary>
    /// Construct Jwari Disruption as an Instant (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached, carrying a castable
    /// back-face land descriptor. The resolve-time counter
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("jwari-disruption");
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at cast time and materializes a fresh
        // back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                JwariRuinsFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Targets a single
    /// spell; on resolution checks whether the target's controller can pay {1}
    /// — if so they pay it automatically and the spell resolves normally; if
    /// not, the spell is countered (CR 701.5) and its card goes to the
    /// graveyard. CR 608.2b — illegal target at resolution is handled by the
    /// pre-resolve target-legality check; this body assumes the resolved
    /// target is still a live <see cref="ISpell"/>.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var unlessCost = ManaCost.Zero.AddGenericCost(UnlessPayGeneric);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Jwari Disruption — counter target spell unless its controller pays {1}", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 118.4 — target's controller may pay {1} to prevent
                        // the counter. v1 auto-pays when able (same posture as
                        // Quench / Mana Leak / Daze / Mystical Dispute).
                        if (spell.Controller is not null
                            && spell.Controller.PayMana(unlessCost))
                        {
                            return;
                        }

                        // Controller couldn't pay — counter the spell (CR 701.5).
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
