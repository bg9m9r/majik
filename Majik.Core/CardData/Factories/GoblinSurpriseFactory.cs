using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Surprise (Onslaught, {2}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Creatures you control get +2/+0 until end of turn.
///     • Create two 1/1 red Goblin creature tokens."
///
/// CR 700.2d — modal "Choose one —" spell. Neither mode takes a target, so
/// both <see cref="TargetRequest"/>s carry MinTargets=MaxTargets=0 and the
/// cast never prompts for a target regardless of the chosen mode (CR 601.2c).
/// The "Choose one" pick count is enforced inside the EffectFactory (each
/// mode at most once, capped at <see cref="PickCount"/>), mirroring the
/// modal-charm shape (<see cref="BorosCharmFactory"/> / Izzet Charm).
///
/// Mode 0 — "Creatures you control get +2/+0 until end of turn":
///   Snapshots the caster's battlefield creatures at resolution (CR 608.2 —
///   effects resolve against current game state) and registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+2, 0) on each (CR 613.1c Layer
///   7c, EOT cleanup CR 514.2). Same posture as the +X/+0 team-pump half of
///   <see cref="ViolentOutburstFactory.ApplyPumpAndHaste"/> — minus the haste
///   grant Goblin Surprise doesn't print. Creatures without a live
///   <see cref="ContinuousEffectsService"/> wired silently no-op (shape-only
///   safety).
///
/// Mode 1 — "Create two 1/1 red Goblin creature tokens":
///   Calls <see cref="TokenFactory.CreateOnBattlefield"/> twice with a 1/1
///   red Goblin <see cref="TokenFactory.TokenSpec"/> (CR 111 / 111.4) — the
///   identical token body as <see cref="KrenkosCommandFactory"/>.
///
/// The base shape (name, single Instant card type, {2}{R}) is materialised
/// from the embedded JSON definition (<c>goblin-surprise.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="WarleadersCallFactory"/>.
/// </summary>
[CardName("Goblin Surprise")]
public static class GoblinSurpriseFactory
{
    public const string CardName = "Goblin Surprise";
    public const string Slug = "goblin-surprise";

    public const int ModePump   = 0;
    public const int ModeTokens = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>+P pump magnitude. Goblin Surprise prints +2/+0.</summary>
    public const int PumpPower = 2;

    /// <summary>+T pump magnitude. Goblin Surprise prints +2/+0.</summary>
    public const int PumpToughness = 0;

    /// <summary>Tokens created by mode 1.</summary>
    public const int TokenCount = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        $"Creatures you control get +{PumpPower}/+{PumpToughness} until end of turn.",
        $"Create {TokenCount} 1/1 red Goblin creature tokens.",
    };

    /// <summary>
    /// Construct Goblin Surprise as an Instant owned by <paramref name="owner"/>
    /// from the embedded JSON definition. Card shape only — the resolve
    /// closures are produced by <see cref="BuildSpellDefinition"/>. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Instant)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the modal <see cref="SpellDefinition"/> for Goblin Surprise.
    /// "Choose one" of two modes; neither mode takes a target.
    /// </summary>
    /// <param name="caster">The player casting the spell — both modes resolve
    /// entirely on the caster's battlefield.</param>
    /// <param name="zones">Optional zone service for token placement (mode 1).
    /// May be null — <see cref="TokenFactory.CreateOnBattlefield"/> falls back
    /// to the caster's own zones.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        // CR 601.2c — both modes are targetless, so every mode's request is a
        // zero-target placeholder; the cast never prompts for a target.
        var targetRequests = new[]
        {
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Buff),
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Token),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Buff,
                BotIntent.Token,
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex — same
                // shape as BorosCharmFactory.
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
                        case ModePump:
                            effectsOut.Add(BuildPumpEffect(caster));
                            break;
                        case ModeTokens:
                            effectsOut.Add(BuildTokensEffect(caster, zones));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    /// <summary>
    /// Mode 0 — "Creatures you control get +2/+0 until end of turn."
    /// CR 608.2 — snapshot at resolution; CR 613.1c Layer 7c +2/+0,
    /// EOT cleanup CR 514.2.
    /// </summary>
    public static IEffect BuildPumpEffect(Player caster) =>
        new Effect(
            $"{CardName}: creatures you control get +{PumpPower}/+{PumpToughness} until end of turn",
            () =>
            {
                // Snapshot to a list before applying (CR 608.2) so any
                // same-step side effects don't disturb the enumeration.
                var creatures = caster.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .ToList();

                foreach (var creature in creatures)
                {
                    // Shape-only safety — without a live
                    // ContinuousEffectsService the pump silently no-ops
                    // rather than NRE'ing (mirrors Violent Outburst).
                    if (creature.ActiveEffects == null) continue;

                    creature.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));
                }
            });

    /// <summary>
    /// Mode 1 — "Create two 1/1 red Goblin creature tokens." CR 111 / 111.4.
    /// Identical token body to Krenko's Command.
    /// </summary>
    public static IEffect BuildTokensEffect(Player caster, ZoneService? zones = null) =>
        new Effect(
            $"{CardName}: create {TokenCount} 1/1 red Goblin creature tokens.",
            () =>
            {
                var spec = new TokenFactory.TokenSpec(
                    Name: "Goblin",
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Goblin },
                    Keywords: null,
                    // CR 111.4 — printed "1/1 red Goblin creature token".
                    Colors: new[] { ManaColor.Red });

                // CR 111 — one token per "create"; the card creates two.
                for (var i = 0; i < TokenCount; i++)
                {
                    TokenFactory.CreateOnBattlefield(spec, caster, zones);
                }
            });
}
