using System;
using System.Collections.Generic;
using System.Linq;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.Targeting;

public static partial class TargetCandidateService
{
    /// <summary>
    /// Enumerate the complete legal candidate pool for the category that
    /// <paramref name="description"/> maps to, against the live state in
    /// <paramref name="ctx"/>. Returns an empty pool for
    /// <see cref="TargetCategory.None"/> (the card's own gatherer wins).
    /// Untargetable objects (hexproof / shroud / protection the caster can't
    /// pierce) are filtered out via <see cref="TargetLegality"/> so the UI
    /// never offers an illegal target; players are always targetable EXCEPT
    /// where their own static abilities (player-shroud / opponent-hexproof)
    /// say otherwise, also enforced via <see cref="TargetLegality"/>.
    /// </summary>
    public static IReadOnlyList<object> GatherCandidates(
        string? description, GameContext ctx, Player caster)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(caster);

        var cat = Classify(description);
        if (cat == TargetCategory.None) return Array.Empty<object>();

        IEnumerable<Permanent> AllPermanents() =>
            ctx.AllPlayers
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .OfType<Permanent>();
        IEnumerable<Creature> Creatures() =>
            AllPermanents().Where(p => p.IsEffectivelyCreature()).OfType<Creature>();
        IEnumerable<Planeswalker> Walkers() =>
            AllPermanents().Where(p => p.IsEffectivePlaneswalker()).OfType<Planeswalker>();
        IEnumerable<Player> Players() => ctx.AllPlayers;
        IEnumerable<Majik.Core.Spells.ISpell> Spells() =>
            ctx.Stack.GetAll().OfType<Majik.Core.Spells.ISpell>();

        IEnumerable<object> raw = cat switch
        {
            TargetCategory.AnyTarget =>
                Creatures().Cast<object>().Concat(Walkers()).Concat(Players()),
            TargetCategory.Creature => Creatures(),
            TargetCategory.Planeswalker => Walkers(),
            TargetCategory.Player => Players(),
            TargetCategory.Opponent => Players().Where(p => !ReferenceEquals(p, caster)),
            TargetCategory.CreatureOrPlayer => Creatures().Cast<object>().Concat(Players()),
            TargetCategory.CreatureOrPlaneswalker => Creatures().Cast<object>().Concat(Walkers()),
            TargetCategory.PlayerOrPlaneswalker => Walkers().Cast<object>().Concat(Players()),
            TargetCategory.Permanent => AllPermanents(),
            TargetCategory.NonlandPermanent => AllPermanents().Where(p => !p.HasType(CardType.Land)),
            TargetCategory.Artifact => AllPermanents().Where(p => p.HasType(CardType.Artifact)),
            TargetCategory.Enchantment => AllPermanents().Where(p => p.HasType(CardType.Enchantment)),
            TargetCategory.Land => AllPermanents().Where(p => p.HasType(CardType.Land)),
            TargetCategory.Spell => Spells(),
            TargetCategory.NoncreatureSpell => Spells().Where(s => !SpellIsCreature(s)),
            TargetCategory.CreatureSpell => Spells().Where(SpellIsCreature),
            TargetCategory.GraveyardCard =>
                ctx.AllPlayers.SelectMany(p => p.Zones.Graveyard.GetCards()).Cast<object>(),
            _ => Array.Empty<object>(),
        };

        // CR 115.5 / 702 — exclude objects the caster cannot legally target
        // right now (hexproof / shroud / protection on permanents and players).
        // Stack spells + graveyard cards carry no battlefield untargetability
        // keywords, so they pass straight through.
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var result = new List<object>();
        foreach (var o in raw)
        {
            if (!seen.Add(o)) continue;
            if (IsTargetableNow(o, caster)) result.Add(o);
        }
        return result;
    }

    private static bool SpellIsCreature(Majik.Core.Spells.ISpell s) =>
        s.Card?.HasType(CardType.Creature) == true;

    // CR 115.5 / 702 — reuse the engine's keyword-gated legality check. A
    // permissive spec accepting every type/player lets TargetLegality.IsLegal
    // apply ONLY its untargetability gate (hexproof / shroud / protection),
    // not a type filter (the category already selected the type). Permanents
    // off the battlefield and non-card / non-player objects (stack spells)
    // are always targetable here.
    private static readonly TargetSpec PermissiveSpec =
        new TargetSpec(string.Empty)
            .AnyTarget()
            .Artifacts()
            .Enchantments()
            .Lands();

    private static bool IsTargetableNow(object o, Player caster)
    {
        if (o is Permanent || o is Player)
        {
            return TargetLegality.IsLegal(PermissiveSpec, o, caster);
        }
        return true;
    }
}
