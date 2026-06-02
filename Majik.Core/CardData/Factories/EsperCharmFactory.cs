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
/// Named-card factory for Esper Charm (Shards of Alara, {W}{U}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Destroy target enchantment.
///     • Draw two cards.
///     • Target player discards two cards."
///
/// CR 700.2d — modal "Choose one —" spell. Modes 0 and 2 take a target
/// (enchantment / player respectively); mode 1 takes none. The bound
/// <see cref="SpellDefinition"/> exposes three <see cref="TargetRequest"/>s
/// (one slot per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so unchosen modes don't gate the cast — mirrors
/// <see cref="BantCharmFactory"/> / <see cref="IzzetCharmFactory"/>).
///
/// The card's base shape (name, single Instant card type, {W}{U}{B}) is
/// materialised from the embedded JSON (<c>esper-charm.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same data-only posture as
/// <see cref="BantCharmFactory"/>. The resolve-time behaviour lives in
/// <see cref="BuildDefinition"/> because a modal <see cref="SpellDefinition"/>
/// (target resolver + per-player discard agent) isn't expressible in the JSON
/// schema.
///
/// Mode 0 — "Destroy target enchantment": re-checks the resolved target is
/// still a <see cref="Permanent"/> on the battlefield with type
/// <see cref="CardType.Enchantment"/> (CR 608.2b illegal-target gate), then
/// destroys via
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
/// (CR 702.12) / regeneration (CR 701.15) shields are honoured. Identical
/// destroy shape to <see cref="BantCharmFactory"/>'s destroy-artifact mode,
/// retargeted to enchantments.
///
/// Mode 1 — "Draw two cards": two simple top-of-library draws for the caster
/// (CR 121.1). Drawing from an empty library flags the player for the
/// CR 704.5c loss SBA via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
/// — same draw body as <see cref="IzzetCharmFactory"/>'s loot mode (draw half).
///
/// Mode 2 — "Target player discards two cards": the targeted player discards
/// two cards of THEIR own choice (CR 701.7a — the discarding player chooses),
/// agent-driven via <see cref="IPlayerAgent.ChooseFromHandAsync"/> with a
/// deterministic first-card fallback; fewer than two in hand discards as many
/// as possible (CR 701.7c). Same body as <see cref="MindRotFactory"/>.
/// </summary>
[CardName("Esper Charm")]
public static class EsperCharmFactory
{
    public const string CardName = "Esper Charm";
    public const string Slug = "esper-charm";

    public const int ModeDestroyEnchantment = 0;
    public const int ModeDrawTwo            = 1;
    public const int ModeTargetDiscardTwo   = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Destroy target enchantment.",
        "Draw two cards.",
        "Target player discards two cards.",
    };

    /// <summary>Construct Esper Charm's base shape from the embedded JSON.</summary>
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
    /// Build the modal "Choose one —" <see cref="SpellDefinition"/> for Esper
    /// Charm.
    /// </summary>
    /// <param name="caster">Spell's controller — draws for mode 1.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// objects directly.</param>
    /// <param name="discardAgent">Optional agent for the TARGET player's
    /// discard picks in mode 2. Non-null → each discard calls
    /// <see cref="IPlayerAgent.ChooseFromHandAsync"/>
    /// (<see cref="BotIntent.Discard"/>); null → deterministic first-card
    /// fallback (CR 701.7a — player chooses; test fixtures still produce
    /// deterministic output). Mirrors <see cref="MindRotFactory"/>.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IPlayerAgent? discardAgent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted for every mode that takes a
        // target. MinTargets=0 so unchosen modes don't gate the cast (mirrors
        // BantCharmFactory / IzzetCharmFactory). Mode 1 (draw) takes no target.
        var targetRequests = new[]
        {
            // Mode 0 — destroy target enchantment.
            new TargetRequest(
                Description: "target enchantment",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Removal,
                // Agent-prompt: walk every battlefield, yield enchantments (CR 303).
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Enchantment))
                    .Cast<object>()
                    .ToList()),

            // Mode 1 — draw two cards (no target).
            new TargetRequest(
                Description: "no target",
                MinTargets: 0,
                MaxTargets: 0,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Draw),

            // Mode 2 — target player discards two cards.
            new TargetRequest(
                Description: "target player",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Discard,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .Cast<object>()
                    .ToList()),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Removal,
                BotIntent.Draw,
                BotIntent.Discard,
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
                        case ModeDestroyEnchantment:
                            effectsOut.Add(BuildDestroyEnchantmentEffect(p, targetResolver));
                            break;
                        case ModeDrawTwo:
                            effectsOut.Add(BuildDrawTwoEffect(caster));
                            break;
                        case ModeTargetDiscardTwo:
                            effectsOut.Add(BuildTargetDiscardEffect(p, targetResolver, discardAgent));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildDestroyEnchantmentEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target enchantment", () =>
        {
            if (p.Targets.Count <= ModeDestroyEnchantment) return;
            var slot = p.Targets[ModeDestroyEnchantment];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Permanent target) return;
            if (target.Zone != ZoneType.Battlefield) return;
            if (!target.HasType(CardType.Enchantment)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) / regeneration
            // (CR 701.15) handled via the Destroy-reason gate.
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });

    private static IEffect BuildDrawTwoEffect(Player caster) =>
        new Effect($"{CardName} — draw two cards", () =>
        {
            // CR 121.1 — "Draw two cards." Two simple top-of-library draws.
            // Empty library mid-draw flags the player for the loss SBA
            // (CR 704.5c) — same draw body as IzzetCharmFactory's loot mode.
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
        });

    private static IEffect BuildTargetDiscardEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        IPlayerAgent? discardAgent) =>
        new Effect($"{CardName} — target player discards two cards", () =>
        {
            if (p.Targets.Count <= ModeTargetDiscardTwo) return;
            var slot = p.Targets[ModeTargetDiscardTwo];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — illegal-target check.
            if (resolved is not Player victim) return;

            // Discard up to 2 cards. Each pick is made by the TARGET player's
            // agent (CR 701.7a — "that player discards … of their choice").
            // Null/no-agent → deterministic first-card pick (matches Mind Rot
            // / Liliana of the Veil +1 v1 fallback). CR 701.7c — discards as
            // many as possible when the hand holds fewer than two.
            for (var i = 0; i < 2; i++)
            {
                var hand = victim.Zones.Hand.GetCards().ToList();
                if (hand.Count == 0) break;

                ICard? pick;
                if (discardAgent != null)
                {
                    pick = discardAgent
                        .ChooseFromHandAsync(victim, hand, BotIntent.Discard)
                        .GetAwaiter().GetResult();
                    if (pick == null || pick.Zone != ZoneType.Hand)
                        pick = hand[0];
                }
                else
                {
                    pick = hand[0];
                }

                victim.Zones.Hand.RemoveCard(pick);
                victim.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            }
        });
}
