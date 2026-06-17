using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dai Li Indoctrination (Avatar: The Last Airbender,
/// {1}{B}).
///
/// Sorcery — Lesson. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Target opponent reveals their hand. You choose a nonland permanent
///       card from it. That player discards that card.
///     • Earthbend 2. (Target land you control becomes a 0/0 creature with
///       haste that's still a land. Put two +1/+1 counters on it. When it dies
///       or is exiled, return it to the battlefield tapped.)"
///
/// ## Why it gets its own factory
/// A CR 700.2d "Choose one —" modal sorcery whose two modes reuse primitives
/// that already ship:
///   - Mode 0 is the reveal-hand → caster-picks → discard pattern of
///     <see cref="DespiseFactory"/>, re-pointed at a <i>nonland permanent</i>
///     filter (CR 701.16 reveal + agent pick via
///     <see cref="IPlayerAgent.ChooseFromHandAsync"/>).
///   - Mode 1 is <b>Earthbend 2</b> (CR 701.59) routed through the shared
///     <see cref="EarthbendAction.Apply(Permanent, Player, int, ContinuousEffectsService?)"/>
///     keyword action (same one driving <see cref="BadgermoleCubFactory"/> /
///     <see cref="NahiriTheHarbingerFactory"/>).
/// No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{B}, black. Card shape comes from the
///   embedded JSON (<c>dai-li-indoctrination.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>. The printed "Lesson" subtype is
///   omitted from the JSON — <see cref="CardSubtype"/> has no Lesson member
///   and Lesson carries no gameplay rules (it only gates Learn, which is not
///   modelled), so its absence is behaviourally lossless.
/// - <b>Mode 0 — targeted discard</b> (CR 700.2d, CR 701.16): single
///   "target opponent" request; on resolution the opponent reveals their hand
///   (<see cref="RevealHelper.RevealHand"/>), the caster picks a NONLAND
///   PERMANENT card (creature / artifact / enchantment / planeswalker / battle —
///   anything that is a permanent type and not a land) via the agent (intent
///   <see cref="BotIntent.HandHate"/>, deterministic first-legal fallback),
///   and that opponent discards the chosen card (Hand → Graveyard).
/// - <b>Mode 1 — Earthbend 2</b> (CR 701.59): "target land you control"
///   request; on resolution the chosen land is animated into a 0/0 Elemental
///   creature with haste that's still a land, gets two +1/+1 counters
///   (→ a 2/2), and a delayed "return tapped on death/exile" trigger is
///   attached — all via <see cref="EarthbendAction.Apply(Permanent, Player, int, ContinuousEffectsService?)"/>.
///   The live <see cref="ContinuousEffectsService"/> drives the animate layer;
///   when none is wired (shape-only test path) the counters + return trigger
///   still apply.
///
/// ## Rules citations
/// - CR 700.2d — "Choose one —" modal spell (pick exactly one mode).
/// - CR 701.16 — reveal a hand / "that player discards that card".
/// - CR 701.59 — Earthbend N (animate land, N +1/+1 counters, return-tapped).
/// - CR 608.2b — a target illegal at resolution is skipped (each mode guards).
///
/// ## Deferred (v1 gaps)
/// - <b>Discard pick prompt fidelity</b>: same posture as
///   <see cref="DespiseFactory"/> — the agent picks, with a first-legal
///   fallback when no agent is supplied.
/// </summary>
[CardName("Dai Li Indoctrination")]
public static class DaiLiIndoctrinationFactory
{
    public const string CardName = "Dai Li Indoctrination";
    public const string Slug = "dai-li-indoctrination";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    public const int ModeDiscard   = 0;
    public const int ModeEarthbend = 1;

    /// <summary>CR 701.59 — Earthbend <b>2</b>.</summary>
    public const int EarthbendAmount = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Target opponent reveals their hand. You choose a nonland permanent card from it. That player discards that card.",
        "Earthbend 2.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the modal <see cref="SpellDefinition"/> for Dai Li Indoctrination.
    /// </summary>
    /// <param name="caster">The player casting the spell (chooses the discard,
    /// and earthbends one of their own lands).</param>
    /// <param name="resolver">Target resolver from the caller's GameContext —
    /// maps a chosen target token to its live game object.</param>
    /// <param name="agent">Agent that picks the nonland permanent to discard
    /// (mode 0). Null → deterministic first-legal pick.</param>
    /// <param name="eventBus">Optional bus for the reveal event (mode 0). Null
    /// in shape-only fixtures (the reveal is a no-op then).</param>
    /// <param name="continuousEffects">Optional per-turn continuous-effects
    /// service for the Earthbend animate layer (mode 1). When null the animate
    /// effect is skipped (counters + return trigger still apply).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        IPlayerAgent? agent,
        IEventBus? eventBus,
        ContinuousEffectsService? continuousEffects = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        // CR 601.2c — one target request per mode that takes a target.
        // MinTargets=0 so the unchosen mode never gates the cast.
        var targetRequests = new[]
        {
            // Mode 0 — "Target opponent".
            new TargetRequest("target opponent", 0, 1, Array.Empty<object>(), BotIntent.HandHate),
            // Mode 1 — "target land you control".
            new TargetRequest("target land you control", 0, 1, Array.Empty<object>(), BotIntent.Ramp),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.HandHate,
                BotIntent.Ramp,
            },
            EffectFactory: p =>
            {
                // Honour either the multi-pick list (first entry wins for a
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
                        case ModeDiscard:
                            effectsOut.Add(BuildDiscardEffect(p, resolver, agent, eventBus));
                            break;
                        case ModeEarthbend:
                            effectsOut.Add(BuildEarthbendEffect(caster, p, resolver, continuousEffects));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: reveal hand → caster picks a nonland permanent → discard it
    // -----------------------------------------------------------------------

    private static IEffect BuildDiscardEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        IPlayerAgent? agent,
        IEventBus? eventBus) =>
        new Effect(
            $"{CardName} — reveal hand → caster picks a nonland permanent → discard",
            () =>
            {
                if (p.Targets.Count <= ModeDiscard) return;
                var slot = p.Targets[ModeDiscard];
                if (slot.Count == 0) return;

                // CR 608.2b — the chosen target must still resolve to a player.
                if (resolver(slot[0]) is not Player victim) return;

                // CR 701.16 — reveal the hand.
                RevealHelper.RevealHand(eventBus, victim, CardName);

                // CR 700.2 — "You choose a nonland permanent card from it."
                // Permanent types (CR 110.4): artifact, battle, creature,
                // enchantment, land, planeswalker. "Nonland permanent" = any
                // of those except land.
                var legal = victim.Zones.Hand.GetCards()
                    .Where(IsNonlandPermanentCard)
                    .ToList();

                if (legal.Count == 0) return;

                // Agent pick (intent HandHate) with deterministic first-legal
                // fallback — same posture as DespiseFactory.
                ICard pick = legal[0];
                if (agent != null)
                {
                    var chosen = agent
                        .ChooseFromHandAsync(victim, legal, BotIntent.HandHate)
                        .GetAwaiter().GetResult();
                    if (chosen != null
                        && chosen.Zone == ZoneType.Hand
                        && IsNonlandPermanentCard(chosen)
                        && ReferenceEquals(chosen.Owner, victim))
                    {
                        pick = chosen;
                    }
                }

                // CR 701.16 — "That player discards that card."
                victim.Zones.Hand.RemoveCard(pick);
                victim.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            });

    /// <summary>
    /// CR 110.4 / 300.1 — a "nonland permanent card" is a card whose types
    /// include at least one permanent type (artifact, creature, enchantment,
    /// planeswalker) and is NOT a land. (Battle is a permanent type per
    /// CR 110.4 but the engine's <see cref="CardType"/> enum doesn't model it,
    /// so it isn't enumerated here — no Modern-legal Battle exists anyway.)
    /// </summary>
    private static bool IsNonlandPermanentCard(ICard card) =>
        !card.HasType(CardType.Land)
        && (card.HasType(CardType.Creature)
            || card.HasType(CardType.Artifact)
            || card.HasType(CardType.Enchantment)
            || card.HasType(CardType.Planeswalker));

    // -----------------------------------------------------------------------
    // Mode 1: Earthbend 2 (CR 701.59)
    // -----------------------------------------------------------------------

    private static IEffect BuildEarthbendEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver,
        ContinuousEffectsService? continuousEffects) =>
        new Effect(
            $"{CardName} — Earthbend {EarthbendAmount} (animate target land you control)",
            () =>
            {
                if (p.Targets.Count <= ModeEarthbend) return;
                var slot = p.Targets[ModeEarthbend];
                if (slot.Count == 0) return;

                // CR 608.2b — target must still be a land on the battlefield.
                if (resolver(slot[0]) is not Land land) return;
                if (land.Zone != ZoneType.Battlefield) return;

                // CR 701.59 — Earthbend 2. The live CES drives the animate
                // continuous effect; EarthbendAction falls back to the land's
                // ActiveEffects when continuousEffects is null.
                EarthbendAction.Apply(land, caster, EarthbendAmount, continuousEffects);
            });
}
