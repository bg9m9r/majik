using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Galvanic Blast (Mirrodin Besieged, {R}).
///
/// Instant. Oracle text:
///   "Galvanic Blast deals 2 damage to any target.
///    Metalcraft — Galvanic Blast deals 4 damage to that target instead
///    if you control three or more artifacts."
///
/// ## Why a named factory
/// Galvanic Blast is the canonical Modern Affinity / artifact-shell burn
/// spell — a one-mana Lightning-Bolt-equivalent that scales to 4 damage
/// under Metalcraft (CR 702.95). The "instead" rider is the same
/// shape as <see cref="BurstLightningFactory"/>'s kicker branch — a
/// resolve-time count off the controller's battlefield decides between
/// the base (2) and upgraded (4) damage tier. Unlike Burst Lightning,
/// the branch gate is a *state read* at resolution time rather than a
/// cast-time payment — the controller need not opt in; the engine
/// inspects their battlefield as the spell resolves.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {R}.
/// - <b>Damage</b>: single 1..1 "any target"
///   <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Removal"/>) — same shape as
///   <see cref="GalvanicDischargeFactory"/> / Lightning Bolt. On
///   resolution deals <see cref="BaseDamage"/> (2) by default, or
///   <see cref="MetalcraftDamage"/> (4) when the controller controls
///   at least <see cref="MetalcraftThreshold"/> (3) artifact-type
///   permanents (CR 702.95).
/// - <b>Metalcraft gate (CR 702.95)</b>: counted via
///   <see cref="ControlsThreeOrMoreArtifacts"/> — every artifact-type
///   permanent on the controller's battlefield counts (artifact
///   creatures / artifact lands count too, matching Mox Opal's posture
///   in <see cref="MoxOpalFactory"/>). The check fires at resolution
///   time so transient artifacts (e.g. a Springleaf Drum that bounced
///   between cast and resolve) are not double-counted.
/// - <b>Planeswalker any-target routing</b>: damage goes through
///   <see cref="Fx.DealDamageAny"/> so Planeswalker targets convert
///   to loyalty removal (CR 306.7), matching Lightning Bolt / Pyrite
///   Spellbomb.
/// </summary>
[CardName("Galvanic Blast")]
public static class GalvanicBlastFactory
{
    public const string CardName = "Galvanic Blast";
    public const string PrintedManaCost = "{R}";

    public const int BaseDamage = 2;
    public const int MetalcraftDamage = 4;
    public const int MetalcraftThreshold = 3;

    /// <summary>CardDef DSL — card shape only. Metalcraft-conditional
    /// damage body is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Galvanic Blast
    /// is cast. Single 1..1 "any target" request; on resolution, deals
    /// <see cref="BaseDamage"/> (2) or <see cref="MetalcraftDamage"/>
    /// (4) when the controller has Metalcraft (CR 702.95 — three or
    /// more artifacts on the battlefield).
    /// </summary>
    /// <param name="controller">Spell controller — the player whose
    /// artifact count is sampled on resolution.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "any target", 1, 1, Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: deal damage (Metalcraft-conditional)",
                        () =>
                        {
                            // CR 702.95 — Metalcraft is active iff the
                            // controller controls three or more
                            // artifacts. The "instead" replacement is a
                            // resolve-time state read (not a cast-time
                            // opt-in), so the count happens here rather
                            // than during the cast flow.
                            var amount = ControlsThreeOrMoreArtifacts(controller)
                                ? MetalcraftDamage
                                : BaseDamage;
                            Fx.DealDamageAny(target, amount);
                        }),
                };
            });
    }

    /// <summary>
    /// CR 702.95 — count artifact-type permanents the
    /// <paramref name="controller"/> controls. Returns <c>true</c> when
    /// the count reaches <see cref="MetalcraftThreshold"/> (3). Counts
    /// every permanent whose type set includes
    /// <see cref="CardType.Artifact"/> (artifact creatures / artifact
    /// lands included — same posture as
    /// <see cref="MoxOpalFactory"/>).
    /// </summary>
    public static bool ControlsThreeOrMoreArtifacts(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var count = 0;
        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card.HasType(CardType.Artifact))
            {
                count++;
                if (count >= MetalcraftThreshold) return true;
            }
        }
        return false;
    }
}
