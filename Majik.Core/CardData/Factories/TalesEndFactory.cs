using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tale's End (War of the Spark, <c>{1}{U}</c>).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Counter target activated ability, triggered ability, or legendary
///    spell."
///
/// ## Why a named factory
/// Tale's End is the <see cref="ConsignToMemoryFactory"/> shape — a counter
/// instant whose legal targets are a heterogeneous set of stack objects —
/// but with a different predicate: it widens the ability set to <b>both</b>
/// activated and triggered abilities (like
/// <see cref="TishanasTidebinderFactory"/>'s ETB) and narrows the spell set
/// to <b>legendary</b> spells (CR 205.4 — the Legendary supertype) instead
/// of colorless spells. No spell template binds that union, so it gets a
/// named factory.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, blue. Card shape comes from the
///   embedded JSON (<c>tales-end.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same loader path as
///   <see cref="MiscalculationFactory"/>).
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target
///   activated ability, triggered ability, or legendary spell"
///   <see cref="TargetRequest"/>. On resolution the target is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> (CR 701.5):
///   * Activated / triggered ability target → ceases to exist when removed
///     from the stack; abilities have no zone, no graveyard hop
///     (CR 701.5b). Mana abilities never use the stack (CR 605.1) so they
///     can't be a target structurally.
///   * Legendary-spell target → countered, the spell's card moves to its
///     owner's graveyard (CR 701.5a).
/// - Legality gate (CR 608.2b — recheck at resolution): if the chosen
///   target is no longer on the stack, or is a spell that is NOT legendary
///   (CR 205.4 — lacks the Legendary supertype), the effect is a clean
///   no-op for it. Filter is applied defensively at resolve time rather
///   than at choose-time (<see cref="TargetRequest.LegalCandidates"/> left
///   empty) — same posture as <see cref="ConsignToMemoryFactory"/>.
///
/// ## Deferred / notes
/// - "Legendary spell" is read off the spell's card supertypes
///   (<see cref="ICard.HasSupertype"/>) at resolution. A spell granted the
///   Legendary supertype by a continuous effect would be honoured only if
///   that grant is reflected on the card's supertype set; the spell-on-stack
///   characteristic-defining layer for supertypes is out of scope here, same
///   posture as every other supertype-reading factory.
/// </summary>
[CardName("Tale's End")]
public static class TalesEndFactory
{
    public const string CardName = "Tale's End";
    public const string Slug = "tales-end";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>
    /// Build a Tale's End instant owned by <paramref name="owner"/>. Card
    /// shape (Instant {1}{U}, blue) is materialized from the embedded JSON
    /// definition; the resolve-time SpellDefinition is built via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Cards.Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Cards.Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "counter target activated ability, triggered ability, or
    /// legendary spell" <see cref="SpellDefinition"/>. Mirrors
    /// <see cref="ConsignToMemoryFactory.BuildSpellDefinition"/>; the
    /// resolve-time predicate differs only in which stack objects it accepts.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token (as
    /// produced by the caller's <see cref="GameContext"/>) to a live engine
    /// object — typically the identity function when targets are already
    /// engine references.</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// object. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target activated ability, triggered ability, or legendary spell",
                    1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Tale's End — counter target activated ability, triggered ability, or legendary spell",
                        () =>
                        {
                            if (stack == null) return;

                            // CR 608.2b — recheck legality at resolution.
                            // Eligible targets:
                            //   * activated ability on the stack
                            //   * triggered ability on the stack
                            //   * legendary spell on the stack
                            // Anything else (non-legendary spell, off-stack
                            // object) → clean no-op.
                            switch (resolved)
                            {
                                case ITriggeredAbility trig:
                                    if (!stack.GetAll().Contains(trig)) return;
                                    OracleSpellBinder.RemoveFromStack(stack, trig);
                                    // Abilities have no zone — they simply
                                    // cease to exist (CR 701.5b).
                                    return;

                                case IActivatedAbility act:
                                    if (!stack.GetAll().Contains(act)) return;
                                    OracleSpellBinder.RemoveFromStack(stack, act);
                                    return;

                                case ISpell spell:
                                    if (!stack.GetAll().Contains(spell)) return;
                                    // CR 205.4 — only a *legendary* spell is a
                                    // legal target. A non-legendary spell is
                                    // illegal at resolution → no-op.
                                    if (!spell.Card.HasSupertype(CardSupertype.Legendary))
                                    {
                                        return;
                                    }
                                    OracleSpellBinder.RemoveFromStack(stack, spell);
                                    // CR 701.5a — countered spell moves to its
                                    // owner's graveyard.
                                    spell.Card.SetZone(ZoneType.Graveyard);
                                    return;

                                // Any other IStackObject shape is illegal per
                                // the printed oracle predicate.
                                default:
                                    return;
                            }
                        }),
                };
            });
    }
}
