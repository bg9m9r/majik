using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spell Stutter (Modern Horizons 3, {1}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Counter target spell unless its controller pays {2} plus an additional
///    {1} for each Faerie you control."
///
/// ## Implemented (v1)
///
/// - Instant card shape ({1}{U}, Blue), mana value 2. The base shape is
///   materialised from the embedded JSON definition (<c>spell-stutter.json</c>)
///   via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
///
/// - <b>"Counter target spell unless its controller pays {2} plus an
///   additional {1} for each Faerie you control."</b> — wired in
///   <see cref="BuildSpellDefinition"/>. Declares a single 1..1 "target spell"
///   TargetRequest; on resolution the unless-cost is computed as
///   <c>{2} + {1} × (Faeries the SPELL'S CONTROLLER controls)</c>. "You" =
///   the ability's controller, i.e. the player who cast Spell Stutter
///   (CR 109.5). The engine attempts to spend that generic total from the
///   target spell's controller's mana pool (CR 118.4); if they have it the
///   payment auto-succeeds and the counter no-ops. Otherwise the target spell
///   is removed from the stack and its card moves to the graveyard
///   (CR 701.5, CR 608.2b).
///
///   The Faerie count is sampled at RESOLUTION time, not cast time
///   (CR 608.2 — the spell's instructions are followed when it resolves), and
///   counts <see cref="CardSubtype.Faerie"/> permanents on the caster's
///   battlefield. Pattern mirrors <see cref="MetallicRebukeFactory.BuildSpellDefinition"/>
///   (counter-unless-pay-generic-N) with the {2} base plus the per-Faerie
///   read borrowed from <see cref="SpellstutterSpriteFactory"/>.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Suitable for shape /
///   dispatcher tests.
/// - <see cref="BuildSpellDefinition"/> — build the counter-unless-pay
///   SpellDefinition for use at cast time.
/// </summary>
[CardName("Spell Stutter")]
public static class SpellStutterFactory
{
    public const string CardName = "Spell Stutter";
    public const string Slug = "spell-stutter";

    /// <summary>The fixed base of the unless-cost, before the per-Faerie
    /// additional {1} (CR — printed "{2} plus an additional {1} for each
    /// Faerie you control").</summary>
    public const int BaseUnlessPay = 2;

    /// <summary>Create a Spell Stutter card owned by <paramref name="owner"/>.
    /// Card shape only — call <see cref="BuildSpellDefinition"/> separately to
    /// produce the resolve-time counter effect.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(definition, owner);
        return card;
    }

    /// <summary>
    /// Build the "counter target spell unless its controller pays {2} plus an
    /// additional {1} for each Faerie you control" SpellDefinition. Mirrors
    /// <see cref="MetallicRebukeFactory.BuildSpellDefinition"/> with the
    /// unless-pay amount computed at resolution as {2} + {1} per Faerie the
    /// caster controls (CR 109.5 — "you" = the spell's controller).
    /// </summary>
    /// <param name="caster">The player who cast Spell Stutter — "you" for the
    /// "each Faerie you control" count (CR 109.5). May be null in shape-only
    /// tests, in which case the count contributes 0.</param>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="Majik.Core.Game.GameContext"/> (chosen handle → live stack
    /// object).</param>
    /// <param name="stack">Live stack required to remove the countered spell.
    /// May be null in shape-only tests — the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player? caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Spell Stutter — counter target spell unless its controller pays {2} plus {1} per Faerie you control",
                        () =>
                        {
                            if (stack == null || resolved is not ISpell spell) return;

                            // CR 109.5 — "you" = the spell's controller (the
                            // caster). CR 608.2 — sample the Faerie count when
                            // Spell Stutter RESOLVES, not when it was cast.
                            var unlessAmount = BaseUnlessPay
                                + CountFaeriesControlled(caster);

                            // CR 118.4 — if the target's controller can pay the
                            // generic total they may do so to save their spell.
                            // v1 auto-pays when able (mirrors MetallicRebuke /
                            // Daze's unless-pay pattern).
                            if (spell.Controller is not null
                                && spell.Controller.PayMana(
                                    ManaCost.Zero.AddGenericCost(unlessAmount)))
                            {
                                return; // paid — counter no-ops, spell survives
                            }

                            // Controller couldn't / wouldn't pay — counter the
                            // spell (CR 701.5).
                            OracleSpellBinder.RemoveFromStack(stack, spell);
                            spell.Card.SetZone(ZoneType.Graveyard);
                        }),
                };
            });
    }

    /// <summary>
    /// Count creatures with the <see cref="CardSubtype.Faerie"/> subtype on
    /// the caster's battlefield (CR 109.5 — "Faeries you control"). A null
    /// caster contributes 0 (shape-only path).
    /// </summary>
    private static int CountFaeriesControlled(Player? caster) =>
        caster == null
            ? 0
            : caster.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .Count(c => c.HasSubtype(CardSubtype.Faerie));
}
