using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Flame of Anor (The Lord of the Rings: Tales of
/// Middle-earth, {1}{U}{R}).
///
/// Instant. Oracle text:
///   "Choose one. If you control a Wizard as you cast this spell, you may
///    choose two instead.
///     • Target player draws two cards.
///     • Destroy target artifact.
///     • Flame of Anor deals 5 damage to target creature."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Set: The Lord of the Rings: Tales of Middle-earth (ltr)</item>
///   <item>Mana cost: {1}{U}{R}; mana value 3</item>
///   <item>Type line: Instant; colors: U, R</item>
/// </list>
///
/// ## Implemented (v1)
/// The card shape is loaded from the embedded JSON definition
/// (<c>flame-of-anor.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as the other
/// data-backed factories. Resolve-time behaviour is supplied by
/// <see cref="BuildDefinition"/>.
///
/// CR 700.2d/700.2e — modal "Choose one (or two)" spell. The conditional
/// pick count is decided as the spell is cast (CR 601.2b — the caster chooses
/// modes while casting): if the caster controls a Wizard at that moment they
/// may choose two modes instead of one. <see cref="PickCount(Player)"/>
/// computes the cap by sampling the caster's battlefield. The
/// <see cref="SpellDefinition.EffectFactory"/> enforces the same cap on the
/// chosen-mode list (each mode at most once, CR 700.2d).
///
/// Three modes, all built from shipped primitives:
///   Mode 0 — "Target player draws two cards" — two top-of-library draws
///     (CR 121.1) for the chosen player (mirrors
///     <see cref="ArchmagesCharmFactory"/>'s draw mode).
///   Mode 1 — "Destroy target artifact" — resolution-time legality re-check
///     then <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///     with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7), so indestructible
///     (CR 702.12) and regeneration (CR 701.15) gates apply
///     (mirrors <see cref="AncientGrudgeFactory"/>).
///   Mode 2 — "Flame of Anor deals 5 damage to target creature" — delegates
///     to <see cref="OracleSpellBinder.DealDamage(object, int)"/> (same as
///     the burn modes on Izzet / Kolaghan's Command).
///
/// ## Rules citations
/// - CR 601.2b — modes (and thus the "choose two" eligibility) are locked in
///   as the spell is cast, based on the board at that time.
/// - CR 700.2d/700.2e — modal "choose one (or two)" spell; each mode chosen
///   at most once.
/// - CR 121.1 — draw a card.
/// - CR 701.7 — Destroy.
/// - CR 608.2b — illegal target at resolution → that part does nothing.
/// </summary>
[CardName("Flame of Anor")]
public static class FlameOfAnorFactory
{
    public const string CardName = "Flame of Anor";
    public const string Slug = "flame-of-anor";

    public const int ModeDraw = 0;
    public const int ModeDestroyArtifact = 1;
    public const int ModeDamage = 2;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Mode 2 deals this much damage to the target creature.</summary>
    public const int DamageAmount = 5;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Target player draws two cards.",
        "Destroy target artifact.",
        "Flame of Anor deals 5 damage to target creature.",
    };

    /// <summary>
    /// CR 601.2b / 700.2 — number of modes the caster may choose as the
    /// spell is cast. Base is one; if <paramref name="caster"/> controls a
    /// Wizard at cast time they may choose two instead.
    /// </summary>
    public static int PickCount(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return ControlsWizard(caster) ? 2 : 1;
    }

    /// <summary>True iff <paramref name="caster"/> controls a creature with
    /// the Wizard subtype on the battlefield (CR 205.3m).</summary>
    private static bool ControlsWizard(Player caster) =>
        caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Any(c => c.HasSubtype(CardSubtype.Wizard));

    /// <summary>
    /// Build the card shape from the embedded JSON definition. Behaviour is
    /// supplied at resolution via <see cref="BuildDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build the Flame of Anor <see cref="SpellDefinition"/>. All three modes
    /// are wired; only the chosen mode's target slot is filled at cast time
    /// (MinTargets=0 so unchosen modes don't gate the cast — mirrors
    /// <see cref="ArchmagesCharmFactory"/> / <see cref="KolaghansCommandFactory"/>).
    /// </summary>
    /// <param name="caster">The casting player. Determines the pick-count cap
    /// (CR 601.2b — "choose two" iff a Wizard is controlled at cast time).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// objects directly.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2b — the "choose two" eligibility is fixed as the spell is
        // cast, based on the board at that moment.
        var pickCount = PickCount(caster);

        // CR 601.2c — one target request per mode that takes a target.
        // MinTargets=0 so unchosen modes don't gate the cast.
        var targetRequests = new[]
        {
            // Mode 0 — target player draws two cards.
            new TargetRequest("target player", 0, 1, Array.Empty<object>(), BotIntent.Draw),
            // Mode 1 — destroy target artifact.
            new TargetRequest(
                "target artifact", 0, 1, Array.Empty<object>(), BotIntent.Removal,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Artifact))
                    .Cast<object>()
                    .ToList()),
            // Mode 2 — 5 damage to target creature.
            new TargetRequest(
                "target creature", 0, 1, Array.Empty<object>(), BotIntent.Burn,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Creature))
                    .Cast<object>()
                    .ToList()),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Draw,
                BotIntent.Removal,
                BotIntent.Burn,
            },
            EffectFactory: p =>
            {
                // Honor the multi-pick list when present; otherwise the
                // legacy scalar ModeIndex (Choose-one default).
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;          // CR 700.2d — each mode at most once
                    if (seen.Count > pickCount) break;     // CR 601.2b — pick-count cap

                    switch (raw)
                    {
                        case ModeDraw:
                            effectsOut.Add(BuildDrawEffect(p, targetResolver));
                            break;
                        case ModeDestroyArtifact:
                            effectsOut.Add(BuildDestroyArtifactEffect(p, targetResolver));
                            break;
                        case ModeDamage:
                            effectsOut.Add(BuildDamageEffect(p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildDrawEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Flame of Anor — target player draws two cards", () =>
        {
            if (p.Targets.Count <= ModeDraw) return;
            var slot = p.Targets[ModeDraw];
            if (slot.Count == 0) return;
            if (resolver(slot[0]) is not Player target) return;

            // CR 121.1 — two top-of-library draws.
            for (var i = 0; i < 2; i++)
            {
                var top = target.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 704.5b — drawing from an empty library flags loss.
                    target.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                target.Zones.Library.RemoveCard(top);
                target.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }
        });

    private static IEffect BuildDestroyArtifactEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Flame of Anor — destroy target artifact", () =>
        {
            if (p.Targets.Count <= ModeDestroyArtifact) return;
            var slot = p.Targets[ModeDestroyArtifact];
            if (slot.Count == 0) return;
            if (resolver(slot[0]) is not Permanent target) return;

            // CR 608.2b — resolution-time legality re-check.
            if (target.Zone != ZoneType.Battlefield) return;
            if (!target.HasType(CardType.Artifact)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and
            // regeneration (CR 701.15) handled by the Destroy-reason gate.
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });

    private static IEffect BuildDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Flame of Anor — deals 5 damage to target creature", () =>
        {
            if (p.Targets.Count <= ModeDamage) return;
            var slot = p.Targets[ModeDamage];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — only a creature is a legal target at resolution.
            if (resolved is not Creature) return;
            OracleSpellBinder.DealDamage(resolved, DamageAmount);
        });
}
