using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Twist Reality (Aetherdrift Commander / Duskmourn
/// reprint cycle, {1}{U}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Counter target spell.
///     • Manifest dread. (Look at the top two cards of your library. Put one
///       onto the battlefield face down as a 2/2 creature and the other into
///       your graveyard. Turn it face up any time for its mana cost if it's a
///       creature card.)"
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {1}{U}{U}; mana value 3</item>
///   <item>Type line: Instant; colors: U</item>
/// </list>
///
/// CR 700.2d — modal "Choose one —" spell. Two <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so the unchosen mode doesn't gate the cast). Same modal
/// shape as <see cref="IzzetCharmFactory"/>.
///
/// ## Implemented (v1)
/// - Instant shape, {1}{U}{U} (blue). The card shape is loaded from the
///   embedded JSON definition (<c>twist-reality.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
///   <see cref="CardDefinitionFactory"/> — same posture as the other
///   data-backed factories.
/// - <b>Mode 0 — "Counter target spell." (CR 701.5)</b>: the vanilla hard
///   counter, mirroring <see cref="CancelFactory.BuildSpellDefinition"/>. Any
///   spell on the stack is a legal target (no type filter, no "unless pays"
///   rider). On resolution the target spell is removed from the stack via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and moved to its owner's
///   graveyard.
/// - <b>Mode 1 — "Manifest dread." (CR 701.59)</b>: delegates to
///   <see cref="ManifestDreadEffect.Resolve(Player, ZoneService?)"/> for the
///   caster — look at the top two cards of the caster's library, manifest the
///   first as a face-down 2/2 <see cref="ManifestedCreature"/> on the
///   battlefield, and put the second into the graveyard. Same primitive
///   <see cref="AbhorrentOculusFactory"/>'s upkeep trigger uses. The granted
///   "turn face up for its mana cost if it's a creature card" activated ability
///   (CR 708.6) is wired by <see cref="ManifestDreadEffect"/>.
///
/// ## Deferred (v1 gaps — small, inherited from the shared primitives)
/// - <b>Manifest pick-one-of-two:</b> v1 deterministically manifests the
///   top-of-library card (the second goes to graveyard); the agent prompt to
///   pick which card goes where is queued behind the shared manifest-dread
///   deferral (see <see cref="AbhorrentOculusFactory"/>).
///
/// CR rule references: 700.2d (modal choose-one), 701.5 (counter), 701.59
/// (manifest dread), 708.2 / 708.6 (face-down permanents + turn-face-up).
/// </summary>
[CardName("Twist Reality")]
public static class TwistRealityFactory
{
    public const string CardName = "Twist Reality";
    public const string Slug = "twist-reality";

    public const int ModeCounter = 0;
    public const int ModeManifestDread = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Counter target spell.",
        "Manifest dread.",
    };

    /// <summary>
    /// Construct Twist Reality as an Instant owned by <paramref name="owner"/>.
    /// The base shape (name, Instant, {1}{U}{U}, blue) is materialised from the
    /// embedded JSON definition.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the SpellDefinition for Twist Reality. Both modes are wired.
    /// </summary>
    /// <param name="caster">The card's controller — manifest dread (mode 1)
    /// resolves against the caster's library.</param>
    /// <param name="targetResolver">Resolves the raw target token chosen by the
    /// caster to a live engine object (pass-through in tests; production callers
    /// route via a TargetResolver service).</param>
    /// <param name="stack">Active stack; required for mode 0 (counter). Null in
    /// pure-shape tests; the counter effect becomes a no-op.</param>
    /// <param name="zones">Optional <see cref="ZoneService"/> for event-routed
    /// manifest dread resolution (mode 1).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted per mode. Only mode 0
        // (counter) takes a target; mode 1 (manifest dread) takes none.
        // MinTargets=0 so the unchosen mode doesn't gate the cast (mirrors
        // IzzetCharmFactory / ArchmagesCharmFactory).
        var targetRequests = new[]
        {
            new TargetRequest("target spell", 0, 1, Array.Empty<object>(), BotIntent.Counter),
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Token),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Counter,
                BotIntent.Token,
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break;  // CR 700.2d — pick count cap

                    switch (raw)
                    {
                        case ModeCounter:
                            effectsOut.Add(BuildCounterEffect(p, targetResolver, stack));
                            break;
                        case ModeManifestDread:
                            effectsOut.Add(BuildManifestDreadEffect(caster, zones));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    /// <summary>
    /// Mode 0 — "Counter target spell." (CR 701.5). Vanilla hard counter:
    /// remove the target spell from the stack and move it to its owner's
    /// graveyard. Mirrors <see cref="CancelFactory"/>.
    /// </summary>
    private static IEffect BuildCounterEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        new Effect("Twist Reality — counter target spell", () =>
        {
            if (stack == null) return;
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ISpell spell) return;

            OracleSpellBinder.RemoveFromStack(stack, spell);
            spell.Card.SetZone(ZoneType.Graveyard);
        });

    /// <summary>
    /// Mode 1 — "Manifest dread." (CR 701.59). Delegates to the shared
    /// <see cref="ManifestDreadEffect.Resolve(Player, ZoneService?)"/> primitive
    /// for the caster.
    /// </summary>
    private static IEffect BuildManifestDreadEffect(Player caster, ZoneService? zones) =>
        new Effect("Twist Reality — manifest dread (CR 701.59)", () =>
            ManifestDreadEffect.Resolve(caster, zones));
}
