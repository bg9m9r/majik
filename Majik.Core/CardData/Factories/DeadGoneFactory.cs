using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the split card Dead // Gone (Planar Chaos,
/// {R} // {2}{R}). Both faces are Instants.
///
/// ## Card text (verified against Scryfall 2026-06-02)
///   Dead {R} — Instant: "Dead deals 2 damage to target creature."
///   Gone {2}{R} — Instant: "Return target creature you don't control to its
///     owner's hand."
///
/// ## Split-card posture (CR 712.2 / 712.4)
///
/// A split card has two faces printed on one card. The caster chooses one
/// face to cast and only that face's mana cost / effect applies (CR 712.4a).
/// Neither face is a permanent — both halves are Instants here, so each
/// resolves as a one-shot effect that then heads to the graveyard.
///
/// The combined card name "Dead // Gone" is the <c>[CardName]</c> dispatch
/// key (matching the embedded seed row), mirroring the two-face posture of
/// <see cref="FireIceFactory"/>. The card SHAPE is materialised from the
/// embedded JSON definition (<c>dead-gone.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; each face's resolve-time
/// <see cref="SpellDefinition"/> is built on demand by
/// <see cref="BuildDeadDefinition"/> / <see cref="BuildGoneDefinition"/>.
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Instant, red, combined card name. The combined card
///   carries the front (Dead) face's {R} cost — the engine's split-cast
///   plumbing selects the per-face cost when each face is cast; the printed
///   front cost is the natural default for the single combined object
///   (same posture as <see cref="FireIceFactory"/> carrying the Fire cost).
/// - <b>Dead face</b> — 2 damage to a single <b>target creature</b>
///   (CR 119 damage; CR 608.2b illegal-target re-check — an off-battlefield
///   creature fizzles), routed through <see cref="Fx.DealDamage"/>. Unlike the
///   "any target" burn (<see cref="FireFactory"/>), Dead's printed text is
///   creature-only, so players and planeswalkers are NOT legal recipients.
/// - <b>Gone face</b> — return a <b>target creature you don't control</b> to
///   its owner's hand (CR 701.10). The CandidateGatherer scopes to creatures
///   controlled by a player OTHER than the caster (CR 109.5 — "you don't
///   control" excludes your own creatures); resolution re-checks legality
///   (CR 608.2b) — a creature that left the battlefield, became yours, or is
///   no longer a creature fizzles cleanly. Same opponent-restricted bounce
///   posture as Petty Theft (<see cref="BrazenBorrowerFactory"/>), creature-only.
///
/// ## Deferred (v1 gaps)
/// - <b>Per-face cast cost selection.</b> The combined object exposes the Dead
///   front cost; selecting {2}{R} for Gone is the split-card cast-plumbing's
///   job. The per-face resolve definitions here are independent of how the
///   cast cost is chosen — identical deferral to <see cref="FireIceFactory"/>.
/// </summary>
[CardName("Dead // Gone")]
public static class DeadGoneFactory
{
    public const string CardName = "Dead // Gone";
    public const string Slug = "dead-gone";

    /// <summary>CR 712 — Dead (front face) printed cost.</summary>
    public const string DeadManaCost = "{R}";

    /// <summary>CR 712 — Gone (back face) printed cost.</summary>
    public const string GoneManaCost = "{2}{R}";

    /// <summary>CR 119 — Dead deals exactly this much damage.</summary>
    public const int DeadDamage = 2;

    /// <summary>
    /// Build the combined card shape from the embedded JSON definition
    /// (Instant, red, combined name "Dead // Gone"). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to; per-face resolve
    /// behaviour is built on demand via <see cref="BuildDeadDefinition"/> /
    /// <see cref="BuildGoneDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time definition for the Dead face: "Dead deals 2
    /// damage to target creature." A single 1..1 "target creature" request
    /// whose resolve deals 2 damage via <see cref="Fx.DealDamage"/>, re-checking
    /// the creature is still on the battlefield at resolution (CR 608.2b).
    /// </summary>
    /// <param name="resolver">Resolves a chosen target token to the live game
    /// object.</param>
    public static SpellDefinition BuildDeadDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

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
                    Intent: BotIntent.Burn,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    Fx.Inline("Dead: deal 2 damage to target creature", () =>
                    {
                        // CR 608.2b — only a creature still on the battlefield
                        // is a legal target; otherwise the spell fizzles.
                        var live = resolver(raw);
                        if (live is Creature creature
                            && creature.Zone == ZoneType.Battlefield)
                        {
                            Fx.DealDamage(creature, DeadDamage);
                        }
                    }),
                };
            });
    }

    /// <summary>
    /// Build the resolve-time definition for the Gone face: "Return target
    /// creature you don't control to its owner's hand." A single 1..1 "target
    /// creature you don't control" request whose resolve returns the chosen
    /// creature to its owner's hand (CR 701.10).
    /// </summary>
    /// <param name="caster">The player casting Gone. Used to scope the
    /// candidate pool to creatures the caster does NOT control (CR 109.5).</param>
    /// <param name="resolver">Resolves a chosen target token to the live game
    /// object.</param>
    public static SpellDefinition BuildGoneDefinition(
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
                    Description: "target creature you don't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // CR 109.5 — "you don't control" = controlled by a player
                    // OTHER than the caster.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, caster))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                return new IEffect[]
                {
                    Fx.Inline("Gone: return target creature you don't control to its owner's hand", () =>
                    {
                        var live = resolver(raw);
                        ResolveBounce(live, caster);
                    }),
                };
            });
    }

    /// <summary>
    /// CR 608.2b resolution-time legality re-check + CR 701.10 bounce. The
    /// chosen target must still be a <see cref="Creature"/> on the battlefield
    /// controlled by a player OTHER than the caster, else the spell does
    /// nothing.
    /// </summary>
    private static void ResolveBounce(object live, Player caster)
    {
        if (live is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        var controller = target.Controller;
        // CR 109.5 — must NOT be controlled by the caster at resolution.
        if (controller == null || ReferenceEquals(controller, caster)) return;

        var owner = target.Owner;
        if (owner == null) return;

        // CR 701.10 — return to its owner's hand.
        controller.Zones.Battlefield.RemoveCard(target);
        owner.Zones.Hand.AddCard(target);
        target.SetZone(ZoneType.Hand);
        target.SetController(owner);
    }
}
