using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bant Charm (Shards of Alara, {G}{W}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Destroy target artifact.
///     • Put target creature on the bottom of its owner's library.
///     • Counter target instant spell."
///
/// CR 700.2d — modal "Choose one —" spell. Each of the three modes takes a
/// target. The bound <see cref="SpellDefinition"/> exposes three
/// <see cref="TargetRequest"/>s (one per mode); only the chosen mode's slot
/// is filled at cast time (MinTargets=0 so unchosen modes don't gate the
/// cast — mirrors <see cref="ArchmagesCharmFactory"/> /
/// <see cref="IzzetCharmFactory"/>).
///
/// The card's base shape (name, single Instant card type, {G}{W}{U}) is
/// materialised from the embedded JSON (<c>bant-charm.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same data-only posture as
/// <see cref="AncientGrudgeFactory"/>. The resolve-time behaviour lives in
/// <see cref="BuildDefinition"/> because a modal <see cref="SpellDefinition"/>
/// (target resolver + stack reference) isn't expressible in the JSON schema.
///
/// Mode 0 — "Destroy target artifact": re-checks the resolved target is
/// still a <see cref="Permanent"/> on the battlefield with type
/// <see cref="CardType.Artifact"/> (CR 608.2b illegal-target gate), then
/// destroys via
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
/// (CR 702.12) / regeneration (CR 701.15) shields are honoured. Identical
/// destroy shape to <see cref="AncientGrudgeFactory"/> / <see cref="ShatterFactory"/>.
///
/// Mode 1 — "Put target creature on the bottom of its owner's library":
/// re-checks the resolved target is still a battlefield <see cref="Creature"/>
/// (CR 608.2b), then moves it from the battlefield to the bottom of its
/// OWNER's library (CR 701 zone change; "owner's library" per the printed
/// text, not the controller's). Library index 0 is the top, so
/// <c>Library.AddCard</c> (which appends) lands the card on the bottom —
/// same ordering contract used by <see cref="Majik.Core.Keywords.ScryAction"/>.
///
/// Mode 2 — "Counter target instant spell": pops the targeted spell off the
/// stack via <see cref="OracleSpellBinder.RemoveFromStack"/> (CR 701.5,
/// honouring the CR 701.5b uncounterable veto) and sends the card to the
/// graveyard — gated on the spell being an Instant (CR 608.2b), mirroring
/// <see cref="IzzetCharmFactory"/>'s counter mode's resolution-time type
/// re-check.
/// </summary>
[CardName("Bant Charm")]
public static class BantCharmFactory
{
    public const string CardName = "Bant Charm";
    public const string Slug = "bant-charm";

    public const int ModeDestroyArtifact = 0;
    public const int ModeBottomCreature  = 1;
    public const int ModeCounterInstant  = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Destroy target artifact.",
        "Put target creature on the bottom of its owner's library.",
        "Counter target instant spell.",
    };

    /// <summary>Construct Bant Charm's base shape from the embedded JSON.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the modal "Choose one —" <see cref="SpellDefinition"/> for Bant
    /// Charm. The stack is required for mode 2 (counter); pass it from the
    /// caller's <see cref="Majik.Core.Game.GameContext"/>.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// objects directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted for every mode that takes a
        // target. MinTargets=0 so unchosen modes don't gate the cast (mirrors
        // ArchmagesCharmFactory / IzzetCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — destroy target artifact.
            new TargetRequest(
                Description: "target artifact",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Removal,
                // Agent-prompt: walk every battlefield, yield artifacts (CR 301).
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Artifact))
                    .Cast<object>()
                    .ToList()),

            // Mode 1 — put target creature on the bottom of its owner's library.
            new TargetRequest(
                Description: "target creature",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Bounce,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Creature))
                    .Cast<object>()
                    .ToList()),

            // Mode 2 — counter target instant spell.
            new TargetRequest(
                Description: "target instant spell",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Counter),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Removal,
                BotIntent.Bounce,
                BotIntent.Counter,
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
                        case ModeDestroyArtifact:
                            effectsOut.Add(BuildDestroyArtifactEffect(p, targetResolver));
                            break;
                        case ModeBottomCreature:
                            effectsOut.Add(BuildBottomCreatureEffect(p, targetResolver));
                            break;
                        case ModeCounterInstant:
                            effectsOut.Add(BuildCounterInstantEffect(p, targetResolver, stack));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildDestroyArtifactEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target artifact", () =>
        {
            if (p.Targets.Count <= ModeDestroyArtifact) return;
            var slot = p.Targets[ModeDestroyArtifact];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Permanent target) return;
            if (target.Zone != ZoneType.Battlefield) return;
            if (!target.HasType(CardType.Artifact)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) / regeneration
            // (CR 701.15) handled via the Destroy-reason gate.
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });

    private static IEffect BuildBottomCreatureEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — put target creature on the bottom of its owner's library", () =>
        {
            if (p.Targets.Count <= ModeBottomCreature) return;
            var slot = p.Targets[ModeBottomCreature];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // CR 109.5 — "its owner's library" (NOT the controller's). The
            // creature leaves the battlefield and is placed on the bottom of
            // its owner's library. The engine keys the battlefield zone by
            // owner (see OracleSpellBinder.MoveToGraveyard), so we remove from
            // the owner's battlefield. Library index 0 is the top, so AddCard
            // (which appends) lands it on the bottom — same ordering contract
            // as ScryAction's ToBottom branch.
            var owner = target.Owner;
            if (owner == null) return;
            owner.Zones.Battlefield.RemoveCard(target);
            owner.Zones.Library.AddCard(target);
            target.SetZone(ZoneType.Library);
        });

    private static IEffect BuildCounterInstantEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        new Effect($"{CardName} — counter target instant spell", () =>
        {
            if (stack == null) return;
            if (p.Targets.Count <= ModeCounterInstant) return;
            var slot = p.Targets[ModeCounterInstant];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ISpell spell) return;

            // CR 608.2b — oracle constraint: target must be an instant spell.
            if (!spell.Card.HasType(CardType.Instant)) return;

            // CR 701.5 — remove from the stack; the helper vetoes per CR 701.5b
            // (uncounterable) and returns false, in which case the spell stays
            // on the stack and resolves normally (don't send it to graveyard).
            if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
            spell.Card.SetZone(ZoneType.Graveyard);
        });
}
