using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Izzet Charm (Return to Ravnica, {U}{R}).
///
/// Instant. Oracle text:
///   "Choose one —
///     • Counter target noncreature spell unless its controller pays {2}.
///     • Izzet Charm deals 2 damage to any target.
///     • Draw two cards, then discard two cards."
///
/// CR 700.2d — modal "Choose one —" spell. Three <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so unchosen modes don't gate the cast).
///
/// Mode 0 — "counter unless pay {2}": mirrors DazeFactory's unless-pay
/// pattern with N=2. v1 auto-resolves: if the target spell's controller has
/// {2} available, it's spent and the counter no-ops (CR 118.4). Otherwise
/// the spell is removed from the stack and sent to the graveyard.
///
/// Mode 1 — "2 damage to any target": delegates to
/// <see cref="OracleSpellBinder.DealDamage"/> (same as Gut Shot / Unholy
/// Heat).
///
/// Mode 2 — "draw two, discard two": same draw-then-discard body as
/// <see cref="FaithlessLootingFactory.BuildResolveEffect"/> (deterministic
/// last-two-in-hand discard; empty library flags
/// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> per CR 704.5b).
///
/// Pattern mirrors <see cref="ArchmagesCharmFactory"/> for the modal
/// choose-one shape.
/// </summary>
public static class IzzetCharmFactory
{
    public const string CardName = "Izzet Charm";

    public const int ModeCounter = 0;
    public const int ModeDamage  = 1;
    public const int ModeLoot    = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Counter target noncreature spell unless its controller pays {2}.",
        "Izzet Charm deals 2 damage to any target.",
        "Draw two cards, then discard two cards.",
    };

    /// <summary>Construct Izzet Charm as an Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, "{U}{R}");
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Izzet Charm.
    /// All three modes are wired. The stack is required for mode 0 (counter).
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IReadOnlyList<Player> allPlayers,
        Majik.Core.Stack.Stack? stack,
        int chosenMode = ModeCounter)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(allPlayers);

        // CR 601.2c — target requests are emitted for every mode that
        // takes a target. MinTargets=0 so unchosen modes don't gate the
        // cast (mirrors ArchmagesCharmFactory / CrypticCommandFactory).
        var targetRequests = new[]
        {
            // Mode 0 — counter noncreature spell unless pay {2}.
            new TargetRequest("target noncreature spell", 0, 1, Array.Empty<object>(), BotIntent.Counter),
            // Mode 1 — 2 damage to any target.
            new TargetRequest("any target", 0, 1, Array.Empty<object>(), BotIntent.Burn),
            // Mode 2 — draw two, discard two (no target).
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Draw),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Counter,
                BotIntent.Burn,
                BotIntent.Draw,
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
                        case ModeDamage:
                            effectsOut.Add(BuildDamageEffect(p, targetResolver));
                            break;
                        case ModeLoot:
                            effectsOut.Add(BuildLootEffect(caster));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildCounterEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        new Effect("Izzet Charm — counter noncreature spell unless its controller pays {2}", () =>
        {
            if (stack == null) return;
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ISpell spell) return;

            // CR 608.2b — noncreature spell gate (enforce at resolution;
            // agent-side filter doesn't yet check this during targeting).
            if (spell.Card.HasType(CardType.Creature)) return;

            // CR 118.4 — "unless its controller pays {2}": v1 auto-resolves.
            // If the target's controller has {2} in pool they pay and
            // the counter no-ops (same posture as DazeFactory / Mana Leak).
            if (spell.Controller is not null
                && spell.Controller.PayMana(ManaCost.Zero.AddGenericCost(2)))
            {
                return;
            }

            OracleSpellBinder.RemoveFromStack(stack, spell);
            spell.Card.SetZone(ZoneType.Graveyard);
        });

    private static IEffect BuildDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Izzet Charm — deals 2 damage to any target", () =>
        {
            if (p.Targets.Count <= ModeDamage) return;
            var slot = p.Targets[ModeDamage];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            OracleSpellBinder.DealDamage(resolved, 2);
        });

    private static IEffect BuildLootEffect(Player caster) =>
        new Effect("Izzet Charm — draw two cards, then discard two cards", () =>
        {
            // ------------------------------------------------------------------
            // CR 121.1 — "Draw two cards." Two simple top-of-library draws.
            // Empty library mid-draw flags the player for SBA loss (CR 704.5b).
            // ------------------------------------------------------------------
            for (var i = 0; i < 2; i++)
            {
                var top = caster.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    caster.MarkTriedToDrawFromEmptyLibrary();
                    break;
                }
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }

            // ------------------------------------------------------------------
            // CR 701.16 — "Discard two cards." v1 deterministic last-two-in-hand
            // (mirrors FaithlessLootingFactory and ConniveAction). Real agent-
            // driven "choose 2 cards to discard" prompt is deferred — same queue
            // as Faithless Looting / Liliana / Connive.
            // ------------------------------------------------------------------
            for (var i = 0; i < 2; i++)
            {
                var pick = caster.Zones.Hand.GetCards().LastOrDefault();
                if (pick == null) break;
                caster.Zones.Hand.RemoveCard(pick);
                caster.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            }
        });
}
