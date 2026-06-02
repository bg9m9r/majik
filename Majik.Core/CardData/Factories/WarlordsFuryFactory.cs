using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Warlord's Fury (Khans of Tarkir, {R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Creatures you control gain first strike until end of turn.
///    Draw a card."
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>warlords-fury.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>.
///
/// The resolve-time body lives in <see cref="BuildDefinition"/> because it
/// needs the caster and (for the keyword grant) a per-turn
/// <see cref="ContinuousEffectsService"/> — neither is expressible in the
/// data-only JSON schema. No targets, no modes, no X.
///
/// On resolution (CR 608.2e — left-to-right clause ordering):
///   1. Mass keyword grant — enumerate the caster's battlefield and register
///      a <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "First strike"
///      (CR 702.7) to every <see cref="Creature"/> the caster controls
///      (CR 613.1c Layer 6; expires at cleanup CR 514.2). Creatures-only scope,
///      same as <see cref="CrashThroughFactory"/> (which grants Trample to the
///      same set of creatures + draws a card). When no continuous-effects
///      service is supplied the grant is a no-op (shape-only path).
///   2. Cantrip rider — the caster draws one card via
///      <see cref="Fx.DrawCards(Player, int)"/> (CR 121 / CR 614 replacement
///      bus aware).
/// </summary>
[CardName("Warlord's Fury")]
public static class WarlordsFuryFactory
{
    public const string CardName = "Warlord's Fury";
    public const string Slug = "warlords-fury";
    public const string PrintedManaCost = "{R}";

    /// <summary>Granted keyword — CR 702.7 First strike.</summary>
    public const string GrantedFirstStrike = "First strike";

    /// <summary>Cards drawn by the rider (CR 121).</summary>
    public const int CardsDrawn = 1;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Warlord's Fury is
    /// cast. No modes, no targets, no X. On resolution:
    ///   1. Every creature the caster controls gains First strike until end of
    ///      turn (CR 702.7 / CR 514.2).
    ///   2. The caster draws a card (CR 121).
    /// </summary>
    /// <param name="caster">The player casting Warlord's Fury; their creatures
    /// gain first strike and they draw the card.</param>
    /// <param name="continuousEffects">Optional per-turn continuous-effects
    /// service used to register the layer-6 first-strike grants. When null the
    /// keyword grant is skipped (shape-only path); the draw still happens.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        ContinuousEffectsService? continuousEffects = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                Fx.Inline(
                    "Warlord's Fury: creatures you control gain first strike until end of turn, then draw a card",
                    () =>
                    {
                        // CR 608.2e step 1 — mass keyword grant. CR 613.1c
                        // Layer 6: grant First strike (CR 702.7) until end of
                        // turn to every creature the caster controls. Per-creature
                        // ActiveEffects service is used when a shared
                        // continuous-effects service was not supplied (mirrors
                        // CrashThroughFactory).
                        foreach (var creature in caster.Zones.Battlefield
                            .GetCards()
                            .OfType<Creature>()
                            .ToList())
                        {
                            var svc = continuousEffects ?? creature.ActiveEffects;
                            if (svc == null) continue;
                            svc.Register(
                                new GrantKeywordUntilEndOfTurnEffect(creature, GrantedFirstStrike));
                        }

                        // CR 608.2e step 2 — cantrip rider: draw a card.
                        Fx.DrawCards(caster, CardsDrawn);
                    }),
            });
    }
}
