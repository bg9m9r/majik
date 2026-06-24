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
/// Named-card factory for Heritage Reclamation (Modern Horizons 3, {1}{G}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Choose one —
///    • Destroy target artifact.
///    • Destroy target enchantment.
///    • Exile up to one target card from a graveyard. Draw a card."
///
/// CR 700.2d — modal "Choose one —" (pick exactly one of the three printed
/// modes). The first two modes are the Disenchant body split by type
/// (artifact vs. enchantment); the third mode is a graveyard-hate cantrip.
///
/// ## Implementation
///
/// Mirrors <see cref="ClingToDustFactory"/>'s hand-built modal shape: the
/// bound <see cref="SpellDefinition"/> exposes one
/// <see cref="TargetRequest"/> per mode, each with <c>MinTargets=0</c> so the
/// two unchosen modes don't gate the cast. Mode 2's target is itself "up to
/// one" (<c>MinTargets=0, MaxTargets=1</c>) — the player may choose no
/// graveyard card and still draw (CR 115.1b — "up to one target").
///
/// - Modes 0/1 — <b>Destroy</b> the chosen battlefield permanent via the
///   Destroy-reason gate (<see cref="ZoneMoveReason.Destroy"/>, CR 701.7), so
///   Indestructible (CR 702.12) and regeneration (CR 701.15) shields are
///   honoured. Illegal target at resolution (the permanent is no longer a
///   battlefield artifact/enchantment) → no-op (CR 608.2b).
/// - Mode 2 — <b>Exile</b> the chosen graveyard card (CR 701.21 — exile
///   bypasses Indestructible/regeneration; the card moves regardless), then
///   <b>draw a card</b> (CR 121 — top of the caster's library). The draw
///   happens whether or not a graveyard card was chosen/exiled (the printed
///   text does not gate the draw on the exile). An empty library flags the
///   caster for the state-based draw-from-empty loss (CR 704.5c / CR 120.3).
///
/// ## Rules citations
/// - CR 700.2d — "Choose one —".
/// - CR 115.1b — "up to one target".
/// - CR 701.7  — Destroy (honours Indestructible / regeneration).
/// - CR 701.21 — Exile.
/// - CR 608.2b — illegal target at resolution → that part does nothing.
/// - CR 121 / CR 120.3 — draw a card; empty-library draw → loss SBA.
///
/// No new engine mechanic is introduced — Destroy, Exile-from-graveyard, the
/// draw funnel, and the hand-built modal SpellDefinition shape all already
/// ship (Disenchant / Cling to Dust).
/// </summary>
[CardName("Heritage Reclamation")]
public static class HeritageReclamationFactory
{
    public const string CardName = "Heritage Reclamation";
    public const string Slug = "heritage-reclamation";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>Mode 0 — destroy target artifact.</summary>
    public const int ModeDestroyArtifact = 0;

    /// <summary>Mode 1 — destroy target enchantment.</summary>
    public const int ModeDestroyEnchantment = 1;

    /// <summary>Mode 2 — exile up to one target graveyard card, then draw.</summary>
    public const int ModeExileDraw = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Destroy target artifact.",
        "Destroy target enchantment.",
        "Exile up to one target card from a graveyard. Draw a card.",
    };

    /// <summary>
    /// Build a Heritage Reclamation instant owned by <paramref name="owner"/>.
    /// Card shape comes from the embedded JSON (<c>heritage-reclamation.json</c>)
    /// via <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
    /// <see cref="CardDefinitionFactory"/>. The bound
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Heritage
    /// Reclamation. Three modes, each with a single per-mode target request
    /// (<c>MinTargets=0</c> so the unchosen modes don't gate the cast). Mode 2
    /// is "up to one" (<c>MinTargets=0</c>) by its own printed wording.
    /// </summary>
    /// <param name="caster">Spell controller — the draw in mode 2 targets this
    /// player's library/hand.</param>
    /// <param name="resolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen token → live game object). Pass
    /// <c>t =&gt; t</c> for tests that hand objects directly.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        var targetRequests = new[]
        {
            // Mode 0 — destroy target artifact.
            new TargetRequest("target artifact", 0, 1, Array.Empty<object>(), BotIntent.Removal),
            // Mode 1 — destroy target enchantment.
            new TargetRequest("target enchantment", 0, 1, Array.Empty<object>(), BotIntent.Removal),
            // Mode 2 — exile up to one target card from a graveyard (then draw).
            new TargetRequest("target card in a graveyard", 0, 1, Array.Empty<object>(), BotIntent.Removal),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Removal,  // destroy artifact
                BotIntent.Removal,  // destroy enchantment
                BotIntent.Removal,  // graveyard hate + cantrip
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex — same shape
                // as ClingToDustFactory / ArchmagesCharmFactory.
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
                            effectsOut.Add(BuildDestroyEffect(p, resolver, ModeDestroyArtifact, CardType.Artifact));
                            break;
                        case ModeDestroyEnchantment:
                            effectsOut.Add(BuildDestroyEffect(p, resolver, ModeDestroyEnchantment, CardType.Enchantment));
                            break;
                        case ModeExileDraw:
                            effectsOut.Add(BuildExileDrawEffect(caster, p, resolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildDestroyEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        int modeSlot,
        CardType requiredType) =>
        new Effect($"Heritage Reclamation — destroy target {requiredType.ToString().ToLowerInvariant()}", () =>
        {
            if (p.Targets.Count <= modeSlot) return;
            var slot = p.Targets[modeSlot];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ICard card) return;

            // CR 608.2b — illegal-target re-check at resolution: the chosen
            // permanent must still be a battlefield permanent of the mode's
            // required type. (Mode 0 → artifact, mode 1 → enchantment.)
            if (card.Zone != ZoneType.Battlefield) return;
            if (!card.HasType(requiredType)) return;

            // CR 701.7 — Destroy through the indestructible/regeneration gate.
            Fx.MoveToGraveyard(card, ZoneMoveReason.Destroy);
        });

    private static IEffect BuildExileDrawEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Heritage Reclamation — exile up to one target graveyard card; draw a card", () =>
        {
            // CR 115.1b — "up to one target": exile the chosen card if one was
            // picked and is still legal (CR 608.2b — still in a graveyard).
            if (p.Targets.Count > ModeExileDraw)
            {
                var slot = p.Targets[ModeExileDraw];
                if (slot.Count > 0)
                {
                    var resolved = resolver(slot[0]);
                    if (resolved is ICard card && card.Zone == ZoneType.Graveyard)
                    {
                        // CR 701.21 — exile (bypasses Indestructible / regen).
                        Fx.MoveToExile(card);
                    }
                }
            }

            // CR 121 — "Draw a card." Runs regardless of whether a graveyard
            // card was chosen/exiled (the printed text doesn't gate the draw on
            // the exile). Empty library flags the state-based draw-from-empty
            // loss (CR 704.5c / CR 120.3); same simplification as
            // ClingToDustFactory's mode-1 draw.
            var top = caster.Zones.Library.GetCards().FirstOrDefault();
            if (top != null)
            {
                caster.Zones.Library.RemoveCard(top);
                caster.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }
            else
            {
                caster.MarkTriedToDrawFromEmptyLibrary();
            }
        });
}
