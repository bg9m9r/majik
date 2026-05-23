using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Murderous Rider // Swift End (Throne of
/// Eldraine, {1}{B}{B}).
///
/// ## Card text
/// - Murderous Rider — Creature — Zombie Knight {1}{B}{B}, 2/3.
///     "Lifelink
///      When this creature dies, exile it." (LTB exile clause deferred —
///     see Deferred section.)
/// - Swift End (Adventure) — Sorcery — Adventure {1}{B}{B}.
///     "Destroy target creature or planeswalker. You lose 2 life."
///
/// ## Implemented (v1)
/// - 2/3 Zombie Knight creature with mana cost {1}{B}{B}.
/// - <b>Lifelink</b> keyword marker via
///   <see cref="KeywordAbility"/> (CR 702.15) — same wiring as
///   <see cref="LurrusOfTheDreamDenFactory"/> /
///   <see cref="AtraxaGrandUnifierFactory"/>.
/// - <b>Swift End helper</b>: <see cref="BuildAdventureSpell"/> returns a
///   standalone <see cref="SpellDefinition"/> matching the Swift End
///   shape — a single 1..1 "target creature or planeswalker" target
///   request whose resolve effect destroys the chosen permanent
///   (<see cref="OracleSpellBinder.MoveToGraveyard"/>, CR 701.7) and
///   makes the caster lose 2 life (<see cref="Player.LoseLife"/>,
///   CR 119.3). The helper is exposed so callers / tests can drive the
///   "side spell" path even though the engine has no Adventure cast
///   pipeline yet (see Deferred).
///
/// ## Deferred (v1 gaps)
/// - <b>Adventure cast-from-hand-to-exile (CR 715)</b>: matches the gap
///   documented on <see cref="BonecrusherGiantFactory"/>. Adventures
///   require:
///     1. A split-card / dual-faced data model where casting the
///        Adventure face exiles the card if it resolves instead of
///        going to the graveyard (CR 715.2),
///     2. An alternative-cost / cast-from-exile rule that lets the
///        owner cast Murderous Rider from exile until it leaves exile
///        (CR 715.3).
///   Until that pipeline exists, callers wanting the Swift End shape
///   should invoke <see cref="BuildAdventureSpell"/> directly to obtain
///   a standalone destroy-target-creature-or-planeswalker
///   <see cref="SpellDefinition"/>.
/// - <b>"When this creature dies, exile it"</b>: the printed creature
///   half carries a self-exile LTB clause. Not modelled here — the
///   engine's death routing currently goes straight to the owner's
///   graveyard with no replace-with-exile rider on the card itself.
///   Adding this requires the same replacement-effect surface used by
///   the Anger of the Gods exile rider (see
///   <see cref="AngerOfTheGodsFactory"/>); deferred to keep the v1 ship
///   minimal.
/// - <b>Indestructible / regeneration riders</b> on the Swift End
///   destroy path — inherited from
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>; same gap as
///   <see cref="SlaughterPactFactory"/> and the rest of the
///   single-target destroy family.
/// </summary>
public static class MurderousRiderFactory
{
    public const string CardName = "Murderous Rider";
    public const string PrintedManaCost = "{1}{B}{B}";

    public const string AdventureName = "Swift End";
    public const string AdventureManaCost = "{1}{B}{B}";
    public const int AdventureSelfLifeLoss = 2;

    /// <summary>
    /// Construct Murderous Rider with no live event-bus / trigger-manager
    /// wiring. Lifelink keyword marker is attached to the card so
    /// structural / dispatch tests see the ability shape.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 3,
            subtypes: new[] { CardSubtype.Zombie, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.15 — Lifelink. Damage dealt by this creature also causes
        // its controller to gain that much life.
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        return card;
    }

    /// <summary>
    /// Build the standalone Swift End <see cref="SpellDefinition"/>. The
    /// caller resolves the chosen target through
    /// <paramref name="targetResolver"/> (typically a
    /// <c>StackResolver</c>); on resolution the chosen creature or
    /// planeswalker is destroyed (CR 701.7) and the caster loses 2 life
    /// (CR 119.3).
    /// </summary>
    /// <param name="caster">The controller of Swift End — also the
    /// player who takes the 2-life payment on resolve.</param>
    /// <param name="targetResolver">Resolves the raw target token to a
    /// live engine object (typically a <see cref="Permanent"/>).</param>
    public static SpellDefinition BuildAdventureSpell(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Swift End: destroy target creature or planeswalker; you lose 2 life", () =>
                    {
                        // CR 701.7 — destroy → owner's graveyard. CR 608.2b
                        // illegal-target check: must still be a Creature or
                        // Planeswalker permanent at resolution. Indestructible
                        // / regeneration deferred (same gap as SlaughterPact).
                        if (resolved is Permanent permanent
                            && (permanent.HasType(CardType.Creature)
                                || permanent.HasType(CardType.Planeswalker)))
                        {
                            OracleSpellBinder.MoveToGraveyard(permanent);
                        }

                        // CR 119.3 — caster loses 2 life as part of the same
                        // resolution. We pay this even if the destroy half
                        // fizzled (printed wording is two consecutive
                        // sentences, no conditional gate).
                        caster.LoseLife(AdventureSelfLifeLoss);
                    }),
                };
            });
    }
}
