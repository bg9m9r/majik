using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Welcome to the Fold (Eldritch Moon, {2}{U}{U}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-16):
///   "Madness {X}{U}{U} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)
///    Gain control of target creature if its toughness is 2 or less. If this
///    spell's madness cost was paid, instead gain control of that creature if
///    its toughness is X or less."
///
/// ## The pay-down — madness-paid resolution-flag seam
/// This is the conditional-madness-X half of the deferral: the control gate's
/// THRESHOLD widens from a fixed 2 to the madness {X} when the spell was cast
/// for its madness cost (CR 702.35c). The cast path stamps
/// <see cref="Card.WasCastForMadnessCost"/> + the chosen madness
/// <see cref="Card.MadnessCastX"/> at madness-cost PAY time (TurnDriver's
/// <c>PayCastMana</c>); the resolution effect reads them off
/// <see cref="ResolutionContext.SourceCard"/> (the spell path's analogue of the
/// ability-path <see cref="ResolutionContext.Source"/>), the same seam
/// Prismatic Ending reads <see cref="Card.PendingCastColors"/> through.
///
/// ## Card identity comes from JSON
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>welcome-to-the-fold.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="HelpingHandFactory"/>.
///
/// ## Implemented (v1)
/// - Sorcery shape at printed cost {2}{U}{U}.
/// - <see cref="BuildSpellDefinition"/> — a single 1..1 "target creature"
///   request (Intent <see cref="BotIntent.Removal"/>). On resolution: if the
///   spell was cast for madness (flag on <see cref="ResolutionContext.SourceCard"/>),
///   the gain-control toughness gate is the madness X
///   (<see cref="Card.MadnessCastX"/>); otherwise it is the printed 2. When the
///   chosen creature's toughness is within the gate, register a permanent
///   <see cref="ControlChangeEffect"/> (Mind-Control-style, CR 613.2) on the
///   live <see cref="ContinuousEffectsService"/> — the same primitive the
///   declarative <c>gain_control</c> verb's permanent path uses
///   (<c>ControlSpellFactory.GainControlSpell</c>). CR 608.2b — an illegal /
///   too-tough target at resolution makes the spell do nothing.
///
/// ## Madness (intrinsic, NOT wired here)
/// Madness {X}{U}{U} works for every catalogued card via
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> consulted by the central
/// discard funnel <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>; "Welcome
/// to the Fold" is catalogued at {X}{U}{U}, so the madness line itself needs no
/// factory code — only the "if its madness cost was paid, instead … X" GATE
/// (the seam above) is bespoke.
/// </summary>
[CardName("Welcome to the Fold")]
public static class WelcomeToTheFoldFactory
{
    public const string CardName = "Welcome to the Fold";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "welcome-to-the-fold";

    /// <summary>Printed (non-madness) toughness gate (CR 702.35c).</summary>
    public const int PrintedToughnessGate = 2;

    /// <summary>Materialise the Sorcery card shape from the embedded JSON
    /// definition. Resolve behaviour is built on demand via
    /// <see cref="BuildSpellDefinition"/>, mirroring
    /// <see cref="HelpingHandFactory"/>.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the resolve-time "gain control of target creature if its toughness
    /// is 2 or less; if this spell's madness cost was paid, instead if its
    /// toughness is X or less" <see cref="SpellDefinition"/>. The effect reads
    /// the per-cast madness stamp off <see cref="ResolutionContext.SourceCard"/>.
    /// </summary>
    /// <param name="caster">Spell controller — the player who gains control.</param>
    /// <param name="resolver">Maps the agent-supplied raw target token to the
    /// live engine object. Pass <c>o =&gt; o</c> for tests that hand cards
    /// directly.</param>
    /// <param name="effects">Live per-game continuous-effects service the control
    /// change registers on (CR 613.2). Required — with none, the spell can't
    /// install the control change.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ContinuousEffectsService effects)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(effects);

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
                    Intent: BotIntent.Removal),
            },
            EffectFactory: chosen => new IEffect[]
            {
                new Effect(
                    $"{CardName}: gain control of target creature within the (madness-X-widened) toughness gate",
                    rc =>
                    {
                        ResolveFromContext(caster, chosen, resolver, effects, rc);
                        return ValueTask.CompletedTask;
                    }),
            });
    }

    /// <summary>
    /// Resolve reading the madness stamp off the live
    /// <see cref="ResolutionContext.SourceCard"/> (production spell path).
    /// </summary>
    private static void ResolveFromContext(
        Player caster,
        ChosenSpellParams chosen,
        Func<object, object> resolver,
        ContinuousEffectsService effects,
        ResolutionContext rc)
        => Resolve(caster, chosen, resolver, effects, rc.SourceCard as Card);

    /// <summary>
    /// Resolve the conditional gain-control. CR 702.35c — when
    /// <paramref name="spell"/> records <see cref="Card.WasCastForMadnessCost"/>,
    /// the toughness gate is its <see cref="Card.MadnessCastX"/> (the madness X);
    /// otherwise the printed <see cref="PrintedToughnessGate"/> (2). On a passing
    /// gate the target's control changes permanently (CR 613.2). The madness
    /// stamp is consumed (cleared) so a later non-cast entry never reuses it.
    /// Exposed for unit tests that drive the resolve directly (mirrors
    /// <see cref="HelpingHandFactory"/>).
    /// </summary>
    public static void Resolve(
        Player caster,
        ChosenSpellParams chosen,
        Func<object, object> resolver,
        ContinuousEffectsService effects,
        Card? spell)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0) return;

        // CR 702.35c — the threshold: madness X when the madness cost was paid,
        // else the printed 2. The madness stamp is consumed here.
        var gate = PrintedToughnessGate;
        if (spell?.WasCastForMadnessCost == true)
        {
            gate = spell.MadnessCastX ?? 0;
        }
        spell?.ClearCastForMadness();

        var live = resolver(chosen.Targets[0][0]);

        // CR 608.2b — illegal-on-resolution checks: must still be a creature on
        // the battlefield. "if its toughness is N or less" (CR 702.35c) reads
        // the creature's effective toughness (CR 208.3).
        if (live is not Creature creature) return;
        if (creature.Zone != ZoneType.Battlefield) return;
        if (creature.GetEffectiveToughness() > gate) return;

        // CR 613.2 — permanent (Mind-Control-style) control change. Same
        // primitive ControlSpellFactory.GainControlSpell installs.
        effects.Register(new ControlChangeEffect(creature, caster));
    }
}
