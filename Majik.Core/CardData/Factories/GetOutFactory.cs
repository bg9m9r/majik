using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
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
/// Named-card factory for Get Out (Duskmourn: House of Horror, {U}{U}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Counter target creature or enchantment spell.
///     • Return one or two target creatures and/or enchantments you own to
///       your hand."
///
/// CR 700.2d — modal "Choose one —" spell. The bound
/// <see cref="SpellDefinition"/> exposes two <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so the unchosen mode doesn't gate the cast — mirrors
/// <see cref="BantCharmFactory"/> / <see cref="IzzetCharmFactory"/>).
///
/// The card's base shape (name, single Instant card type, {U}{U}) is
/// materialised from the embedded JSON (<c>get-out.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same data-only posture as
/// <see cref="BantCharmFactory"/>. The resolve-time behaviour lives in
/// <see cref="BuildDefinition"/> because a modal <see cref="SpellDefinition"/>
/// (caster reference + target resolver + stack reference) isn't expressible
/// in the JSON schema.
///
/// Mode 0 — "Counter target creature or enchantment spell": pops the targeted
/// spell off the stack via <see cref="OracleSpellBinder.RemoveFromStack"/>
/// (CR 701.5, honouring the CR 701.5b uncounterable veto) and sends the card
/// to the graveyard — gated on the spell being a creature OR enchantment
/// (CR 608.2b), mirroring <see cref="BantCharmFactory"/>'s counter mode's
/// resolution-time type re-check.
///
/// Mode 1 — "Return one or two target creatures and/or enchantments you own
/// to your hand": the target request allows up to two targets (MaxTargets=2).
/// At resolution each targeted permanent is re-checked: still on the
/// battlefield, still a creature or enchantment, and owned by the Get Out
/// caster (CR 608.2b — "you own"). Each surviving target is returned to its
/// owner's hand via
/// <see cref="Fx.BounceToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>
/// (CR 701.20).
/// </summary>
[CardName("Get Out")]
public static class GetOutFactory
{
    public const string CardName = "Get Out";
    public const string Slug = "get-out";

    public const int ModeCounter = 0;
    public const int ModeReturn  = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>CR — "one or two target … " upper bound for the return mode.</summary>
    public const int MaxReturnTargets = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Counter target creature or enchantment spell.",
        "Return one or two target creatures and/or enchantments you own to your hand.",
    };

    /// <summary>Construct Get Out's base shape from the embedded JSON.</summary>
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
    /// Build the modal "Choose one —" <see cref="SpellDefinition"/> for Get
    /// Out. The stack is required for mode 0 (counter); pass it from the
    /// caller's <see cref="Majik.Core.Game.GameContext"/>.
    /// </summary>
    /// <param name="caster">The Get Out controller — used by mode 1 to enforce
    /// the "you own" constraint at resolution (CR 608.2b).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// objects directly.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted for every mode that takes a
        // target. MinTargets=0 so the unchosen mode doesn't gate the cast
        // (mirrors BantCharmFactory / IzzetCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — counter target creature or enchantment spell.
            new TargetRequest(
                Description: "target creature or enchantment spell",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Counter),

            // Mode 1 — return one or two creatures/enchantments YOU OWN to hand.
            new TargetRequest(
                Description: "one or two target creatures and/or enchantments you own",
                MinTargets: 0,
                MaxTargets: MaxReturnTargets,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Bounce,
                // Agent-prompt: creatures/enchantments the CASTER owns
                // (CR 109.5 — "you own"). The caster is the spell's controller.
                CandidateGatherer: _ => caster.Zones.Battlefield.GetCards()
                    .Where(c => c.HasType(CardType.Creature) || c.HasType(CardType.Enchantment))
                    .Where(c => ReferenceEquals(c.Owner, caster))
                    .Cast<object>()
                    .ToList()),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Counter,
                BotIntent.Bounce,
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
                        case ModeReturn:
                            effectsOut.Add(BuildReturnEffect(p, targetResolver, caster));
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
        new Effect($"{CardName} — counter target creature or enchantment spell", () =>
        {
            if (stack == null) return;
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ISpell spell) return;

            // CR 608.2b — oracle constraint: target must be a creature or
            // enchantment spell.
            if (!spell.Card.HasType(CardType.Creature)
                && !spell.Card.HasType(CardType.Enchantment)) return;

            // CR 701.5 — remove from the stack; the helper vetoes per CR 701.5b
            // (uncounterable) and returns false, in which case the spell stays
            // on the stack and resolves normally (don't send it to graveyard).
            if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
            spell.Card.SetZone(ZoneType.Graveyard);
        });

    private static IEffect BuildReturnEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        Player caster) =>
        new Effect($"{CardName} — return one or two creatures/enchantments you own to your hand", () =>
        {
            if (p.Targets.Count <= ModeReturn) return;
            var slot = p.Targets[ModeReturn];
            if (slot.Count == 0) return;

            // CR — "one or two target …"; never act on more than two.
            var count = Math.Min(slot.Count, MaxReturnTargets);
            for (var i = 0; i < count; i++)
            {
                var resolved = resolver(slot[i]);

                // CR 608.2b — resolution-time legality re-check.
                if (resolved is not Permanent target) continue;
                if (target.Zone != ZoneType.Battlefield) continue;
                if (!target.HasType(CardType.Creature)
                    && !target.HasType(CardType.Enchantment)) continue;

                // CR 109.5 — "you own": only permanents owned by the Get Out
                // caster are eligible. (Mirrors the CandidateGatherer filter;
                // re-checked here so a stale / illegal slot can't bounce an
                // opponent's permanent.)
                if (!ReferenceEquals(target.Owner, caster)) continue;

                // CR 701.20 — return to its owner's hand (= your hand, since
                // you own it).
                Fx.BounceToHand(target);
            }
        });
}
