using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Repeal (Ravnica: City of Guilds, {X}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-02):
///   "Return target nonland permanent with mana value X to its owner's hand.
///    Draw a card."
///
/// Repeal is the X-scaled, single-target bounce-cantrip cousin of
/// <see cref="EchoingTruthFactory"/>: it shares the "return target nonland
/// permanent to its owner's hand" core (CR 701.10) but (a) hits a SINGLE
/// permanent rather than sweeping all same-name copies, (b) restricts the
/// legal target to one whose mana value equals the cast X (CR 115.4), and
/// (c) adds a Peek-style cantrip draw (CR 121.1).
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {X}{U}. The base shape
///   (name / Instant type / {X}{U} cost) is materialised from the embedded
///   JSON definition (<c>repeal.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="EchoingTruthFactory"/>.
/// - <b>HasVariableX</b> — the <see cref="SpellDefinition"/> sets
///   <see cref="SpellDefinition.HasVariableX"/> true so the cast flow prompts
///   for X as Repeal is cast (CR 601.2f). The chosen X arrives at resolution
///   as <see cref="ChosenSpellParams.X"/>.
/// - <b>Return target nonland permanent with mana value X</b> — a single
///   1..1 "target nonland permanent" <see cref="TargetRequest"/>. The live
///   <c>CandidateGatherer</c> walks every battlefield, yielding permanents
///   whose card-type set does NOT include <see cref="CardType.Land"/>
///   (CR 305 — Land is a card type).
///
///   The "mana value X" restriction (CR 115.4) is NOT applied at gather time:
///   <see cref="GameContext"/> exposes no chosen-X, and CR 601.2 locks targets
///   (601.2c) BEFORE X (601.2f), so at target-choice X is not yet known. It is
///   instead enforced as a resolution-time illegal-target gate (CR 608.2b) —
///   the same posture as <see cref="DrownInTheLochFactory"/>'s mv-≤-X gate. If
///   the target's mana value != X at resolution it is an illegal target.
/// - <b>...to its owner's hand</b> — on resolution the single target is
///   returned to its owner's hand (CR 701.10) via raw zone manipulation,
///   mirroring <see cref="EchoingTruthFactory.ReturnToOwnersHand"/>.
/// - <b>Draw a card</b> — a Peek-style top-of-library draw for the caster
///   (CR 121.1); an empty library flags the SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
///
/// ## Rules notes
/// - <b>Single target, no sweep.</b> Unlike Echoing Truth, Repeal returns only
///   the one chosen permanent.
/// - <b>All-or-nothing fizzle (CR 608.2b).</b> Repeal has exactly one target.
///   If at resolution that target is illegal (off the battlefield, now a land,
///   or mana value != X) the spell has no legal targets and does not resolve —
///   so NEITHER the bounce NOR the cantrip draw happens. The draw guard mirrors
///   <see cref="PeekFactory"/>'s fizzle posture: the draw is part of the same
///   non-resolving spell, not an independent instruction.
/// </summary>
[CardName("Repeal")]
public static class RepealFactory
{
    public const string CardName = "Repeal";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "repeal";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {X}{U}) from the
    /// embedded JSON definition. Resolve behaviour (return target nonland
    /// permanent with mana value X to its owner's hand, then draw) is built on
    /// demand via <see cref="BuildDefinition"/>, mirroring
    /// <see cref="EchoingTruthFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "return target nonland permanent with mana value X to its
    /// owner's hand; draw a card" <see cref="SpellDefinition"/>. On resolve:
    /// <list type="number">
    ///   <item>CR 608.2b re-check: the target must still be a nonland
    ///     <see cref="Permanent"/> on the Battlefield whose mana value equals
    ///     the cast X (CR 115.4); otherwise the target is illegal, the spell
    ///     does not resolve, and neither the bounce nor the draw happens.</item>
    ///   <item>CR 701.10 — return the target to its owner's hand.</item>
    ///   <item>CR 121.1 — the caster draws a card.</item>
    /// </list>
    /// </summary>
    /// <param name="caster">The Repeal spell's controller — the player who
    /// draws the cantrip card.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonland permanent with mana value X",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // Gather every nonland permanent on any battlefield (CR 305 —
                    // Land is a card type). The mana-value-X restriction
                    // (CR 115.4) is enforced at resolution (CR 608.2b), not here:
                    // X is not yet chosen when targets are locked (CR 601.2c
                    // precedes 601.2f), and GameContext carries no chosen-X.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => !c.HasType(CardType.Land))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                var raw = targetResolver(chosen.Targets[0][0]);

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: return target nonland permanent with mana value {x} "
                        + "to its owner's hand, then draw a card.",
                        () => Resolve(raw, x, caster)),
                };
            });
    }

    private static void Resolve(object resolved, int x, Player caster)
    {
        // CR 608.2b — resolution-time legality re-check. Repeal has exactly one
        // target; if it is illegal the spell does not resolve at all, so the
        // cantrip draw is skipped too (the draw is part of the same
        // non-resolving spell, not an independent instruction).
        if (resolved is not Permanent target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.HasType(CardType.Land)) return;          // CR 305 — nonland only.
        if (target.ManaCostValue.TotalValue != x) return;   // CR 115.4 — mana value must equal X.

        // CR 701.10 — return the single target to its owner's hand. Raw zone
        // manipulation, mirroring EchoingTruthFactory.ReturnToOwnersHand.
        var owner = target.Owner;
        if (owner == null) return;
        var controller = target.Controller ?? owner;

        controller.Zones.Battlefield.RemoveCard(target);
        owner.Zones.Hand.AddCard(target);
        target.SetZone(ZoneType.Hand);
        target.SetController(owner);

        // CR 121.1 — "Draw a card." Simple top-of-library draw for the caster;
        // an empty library flags the SBA-driven loss (CR 704.5b) via
        // MarkTriedToDrawFromEmptyLibrary (same posture as PeekFactory).
        var top = caster.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            caster.MarkTriedToDrawFromEmptyLibrary();
            return;
        }
        caster.Zones.Library.RemoveCard(top);
        caster.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
