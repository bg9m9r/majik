using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Warping Wail (Oath of the Gatewatch, {1}{C}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "({C} represents colorless mana.)
///    Choose one —
///     • Exile target creature with power or toughness 1 or less.
///     • Counter target sorcery spell.
///     • Create a 1/1 colorless Eldrazi Scion creature token. It has
///       \"Sacrifice this token: Add {C}.\""
///
/// CR 700.2d — modal "Choose one —" spell (PickCount = 1) with three modes.
/// Same overall shape as <see cref="KozileksCommandFactory"/> (modal Eldrazi
/// instant using the <see cref="Fx"/> primitives) and
/// <see cref="IzzetCharmFactory"/> (the choose-one pick-count cap).
///
/// Targets are addressed by index into <see cref="ChosenSpellParams.Targets"/>:
///   Targets[0] — target creature with power or toughness 1 or less (mode 0).
///   Targets[1] — target sorcery spell on the stack (mode 1 — counter).
///   Mode 2 takes no target (token creation).
///
/// ## v1 notes / deferrals
/// - Mode 0's "power or toughness 1 or less" gate is checked at resolution
///   (CR 608.2b — resolution-time legality), mirroring
///   <see cref="KozileksCommandFactory.BuildExileCreatureEffect"/>'s mana-value gate.
/// - Mode 2 reuses <see cref="EldraziSkyspawnerFactory.CreateEldraziScionToken"/>
///   for the 1/1 colourless Eldrazi Scion with "Sacrifice this creature: Add
///   {C}." — that helper carries the documented v1 sac-cost-on-mana-ability
///   deferral (same gap as Eldrazi Spawn / Treasure / Food).
/// </summary>
[CardName("Warping Wail")]
public static class WarpingWailFactory
{
    public const string CardName = "Warping Wail";

    // CR 107.4c — {C} is the colorless mana symbol. Printed cost {1}{C}.
    public const string PrintedManaCost = "{1}{C}";

    public const int ModeExileCreature = 0;
    public const int ModeCounterSorcery = 1;
    public const int ModeCreateScion = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Exile target creature with power or toughness 1 or less.",
        "Counter target sorcery spell.",
        "Create a 1/1 colorless Eldrazi Scion creature token. It has \"Sacrifice this token: Add {C}.\"",
    };

    /// <summary>Construct Warping Wail as an Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Warping Wail. All three modes are wired.
    /// The stack is required for mode 1 (counter target sorcery).
    /// </summary>
    /// <param name="caster">The casting player (controls the Scion token).</param>
    /// <param name="targetResolver">Resolves targets at effect time.</param>
    /// <param name="allPlayers">All players (present for parity with the
    /// modal-factory contract; not consulted by Warping Wail's modes).</param>
    /// <param name="stack">The shared stack — required for mode 1's counter.</param>
    /// <param name="zones">When supplied, the Scion token's ETB routes through
    /// <see cref="Services.ZoneService.MoveCardTo"/> so its CardMovedEvent
    /// publishes for downstream subscribers.</param>
    /// <param name="chosenMode">Default mode index when none is supplied via
    /// <see cref="ChosenSpellParams.ModeIndex"/> / ModeIndexes.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IReadOnlyList<Player> allPlayers,
        Majik.Core.Stack.Stack? stack,
        Majik.Core.Services.ZoneService? zones = null,
        int chosenMode = ModeExileCreature)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(allPlayers);

        // CR 601.2c — a target request is emitted for every mode that takes a
        // target. MinTargets=0 so unchosen modes don't gate the cast (mirrors
        // KozileksCommandFactory / IzzetCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — exile target creature with power or toughness 1 or less.
            new TargetRequest("target creature with power or toughness 1 or less", 0, 1, Array.Empty<object>(), BotIntent.Removal),
            // Mode 1 — counter target sorcery spell.
            new TargetRequest("target sorcery spell", 0, 1, Array.Empty<object>(), BotIntent.Counter),
            // Mode 2 — create a Scion token (no target).
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Token),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Removal,
                BotIntent.Counter,
                BotIntent.Token,
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex; finally
                // fall back to the supplied default mode.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : new[] { chosenMode });

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break;  // CR 700.2d — pick count cap

                    switch (raw)
                    {
                        case ModeExileCreature:
                            effectsOut.Add(BuildExileCreatureEffect(p, targetResolver));
                            break;
                        case ModeCounterSorcery:
                            effectsOut.Add(BuildCounterSorceryEffect(p, targetResolver, stack));
                            break;
                        case ModeCreateScion:
                            effectsOut.Add(BuildCreateScionEffect(caster, zones));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    // -----------------------------------------------------------------------
    // Mode bodies
    // -----------------------------------------------------------------------

    private static IEffect BuildExileCreatureEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        Fx.Inline(
            $"{CardName}: exile target creature with power or toughness 1 or less",
            () =>
            {
                if (p.Targets.Count <= ModeExileCreature) return;
                var slot = p.Targets[ModeExileCreature];
                if (slot.Count == 0) return;
                if (resolver(slot[0]) is not Creature creature) return;

                // CR 608.2b — resolution-time legality check.
                if (creature.Zone != ZoneType.Battlefield) return;
                // "power or toughness 1 or less" — current characteristics
                // (CR 613) at resolution; a single matching stat qualifies.
                if (creature.Power > 1 && creature.Toughness > 1) return;

                // CR 701.20 — exile (not "destroy"); Indestructible does not
                // protect against exile.
                Fx.MoveToExile(creature);
            });

    private static IEffect BuildCounterSorceryEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        Fx.Inline(
            $"{CardName}: counter target sorcery spell",
            () =>
            {
                if (stack == null) return;
                if (p.Targets.Count <= ModeCounterSorcery) return;
                var slot = p.Targets[ModeCounterSorcery];
                if (slot.Count == 0) return;
                if (resolver(slot[0]) is not ISpell spell) return;

                // CR 608.2b — sorcery-spell gate enforced at resolution (the
                // agent-side target filter does not yet check the spell's
                // type during targeting).
                if (!spell.Card.HasType(CardType.Sorcery)) return;

                // CR 701.5 — remove from stack and send the card to its
                // owner's graveyard (Fx.Counter honours uncounterable spells).
                Fx.Counter(stack, spell);
            });

    private static IEffect BuildCreateScionEffect(
        Player caster,
        Majik.Core.Services.ZoneService? zones) =>
        Fx.Inline(
            $"{CardName}: create a 1/1 colourless Eldrazi Scion creature token with \"Sacrifice this creature: Add {{C}}.\"",
            () =>
            {
                // CR 111 / CR 111.4 — one 1/1 colourless Eldrazi Scion under
                // the caster's control. Reuses Eldrazi Skyspawner's helper
                // (carries the documented v1 sac-cost-on-mana-ability gap).
                EldraziSkyspawnerFactory.CreateEldraziScionToken(caster, zones);
            });
}
