# Targeting Overhaul + Player-as-Target Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the human click a player's HUD (and any legal permanent) to choose it as a target, by giving every targeting prompt a complete legal-candidate pool (incl. players) computed centrally and plumbing players onto the wire + the on-board click-to-select UI.

**Architecture:** A central `TargetCandidateService` maps a TargetRequest `Description` → `TargetCategory` → legal pool (creatures/players/planeswalkers/permanents/stack-spells/graveyard-cards), wired into `TargetCollection.CollectAsync` as the fallback when a card ships no candidates. A category-derived legality predicate is stamped at cast time so the existing CR 608.2b recheck stays honest. Players are shipped via a new `PlayerCandidateDto` on `PromptDto`; the portal player HUD becomes a clickable target reusing the PR #140 `SelectionService` flow.

**Tech Stack:** C# / .NET 10 (engine, xUnit + FluentAssertions); Angular 21 + NgRx Signals (portal, `ng test --no-watch` / jsdom). Cross-repo, contract change, api-first deploy.

**Spec:** `majik.core/docs/superpowers/specs/2026-06-16-targeting-overhaul-player-targets-design.md`

---

## Two phases

- **Phase A — Engine + wire (majik.core, PR to `bg9m9r/majik`).** Tasks 1–7. Ship + auto-merge. Then deploy majik-api (manual).
- **Phase B — Portal (majik.portal, PR to `bg9m9r/majik.portal`).** Tasks 8–12. Depends on Phase A's contract. Ship + auto-merge; portal auto-deploys.

The implementer runs Phase A to green + merged + api deployed BEFORE Phase B regenerates the portal client.

## Verify-against-real-code anchors (read before coding)

- `TargetRequest` record: `Majik.Core/Players/Agents/TargetRequest.cs:51`. `ResolveCandidates`/`WithCandidates` lines 85-108.
- `TargetCollection.CollectAsync`: `Majik.Core/Targeting/TargetCollection.cs` (find the `ResolveCandidates(ctx)` call ~line 68 and the `caster`/casting-player variable in scope).
- `TargetLegality` (hexproof/shroud/protection): `Majik.Core/Targeting/TargetLegality.cs` — find the method that tests whether `target` is legal for `controller` (reuse it to filter the pool).
- `StackResolver` recheck: `Majik.Core/Services/StackResolver.cs:85-101` (`Spell.TargetLegalityPredicate`).
- `Spell.TargetLegalityPredicate`: `Majik.Core/Spells/Spell.cs` (confirm the property + setter).
- `RemoteAgent.ChooseTargetsAsync`: `Majik.Core.Api/RemoteAgent.cs:784-824`; `PromptPayload`: line ~1228; `CandidateMatchesId`: 653-658 (NO change).
- `PromptDto` + `PlayerDto`: `Majik.Core.Api/Dtos/Dtos.cs:136-186` / `23-33`.
- `BuildPrompt`: `Majik.Core.Api/GameFacade.cs:1490-1504`.
- Drift gate: `Majik.Server.Tests/OpenApiContractDriftTests.cs`; snapshot `Majik.Server.Tests/Snapshots/openapi.v1.json`.
- Portal: `src/app/core/match/selection.service.ts`, `src/app/ui/player-hud.component.ts`, `src/app/routes/match/components/board.component.ts`, `src/app/core/match/match.types.ts`, `src/app/routes/match/match.ts` (`boardInstanceIds` ~line 1039, `translateDecision` ~line 805), affordance CSS in `src/app/ui/card-view.component.ts`.

Build/test commands: `dotnet build Majik.sln`; `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj`; `dotnet test Majik.Core.Api.Tests/...`; `dotnet test Majik.Server.Tests/...` (drift). Portal: `npx ng test --no-watch [--include='**/<f>.spec.ts']`; `npm run build`.

---

# PHASE A — ENGINE + WIRE (majik.core)

Branch off `main`: `feat/targeting-overhaul-player-targets`. DCO: `git commit -s`. End messages with the Co-Authored-By line.

### Task 1: TargetCategory + Classify

**Files:**
- Create: `Majik.Core/Targeting/TargetCandidateService.cs`
- Test: `Majik.Core.Tests/Targeting/TargetCandidateServiceClassifyTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Majik.Core.Targeting;
using Xunit;

namespace Majik.Core.Tests.Targeting;

public class TargetCandidateServiceClassifyTests
{
    [Theory]
    [InlineData("any target", TargetCategory.AnyTarget)]
    [InlineData("target creature", TargetCategory.Creature)]
    [InlineData("target player", TargetCategory.Player)]
    [InlineData("target opponent", TargetCategory.Opponent)]
    [InlineData("target creature or player", TargetCategory.CreatureOrPlayer)]
    [InlineData("target player or planeswalker", TargetCategory.PlayerOrPlaneswalker)]
    [InlineData("target creature or planeswalker", TargetCategory.CreatureOrPlaneswalker)]
    [InlineData("target planeswalker", TargetCategory.Planeswalker)]
    [InlineData("target nonland permanent", TargetCategory.NonlandPermanent)]
    [InlineData("target permanent", TargetCategory.Permanent)]
    [InlineData("target spell", TargetCategory.Spell)]
    [InlineData("target noncreature spell", TargetCategory.NoncreatureSpell)]
    [InlineData("target creature spell", TargetCategory.CreatureSpell)]
    [InlineData("target card in a graveyard", TargetCategory.GraveyardCard)]
    [InlineData("target artifact", TargetCategory.Artifact)]
    [InlineData("target enchantment", TargetCategory.Enchantment)]
    [InlineData("target land", TargetCategory.Land)]
    [InlineData("target creature with power 1 or less", TargetCategory.Creature)] // coarse → Creature
    [InlineData("no target", TargetCategory.None)]
    [InlineData("", TargetCategory.None)]
    public void Classify_maps_description_to_category(string desc, TargetCategory expected)
    {
        TargetCandidateService.Classify(desc).Should().Be(expected);
    }
}
```

> `Classify` must be public (or internal + `[assembly:InternalsVisibleTo("Majik.Core.Tests")]` if that already exists — check the csproj; the explore noted `Classify` as internal, so confirm InternalsVisibleTo is present, else make `Classify`/`TargetCategory` public).

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter FullyQualifiedName~TargetCandidateServiceClassifyTests`
Expected: FAIL — type missing.

- [ ] **Step 3: Implement `TargetCategory` + `Classify`**

```csharp
using Majik.Core.Game;

namespace Majik.Core.Targeting;

public enum TargetCategory
{
    None,
    AnyTarget,
    Creature,
    Player,
    Opponent,
    CreatureOrPlayer,
    CreatureOrPlaneswalker,
    PlayerOrPlaneswalker,
    Planeswalker,
    Permanent,
    NonlandPermanent,
    Artifact,
    Enchantment,
    Land,
    Spell,
    NoncreatureSpell,
    CreatureSpell,
    GraveyardCard,
}

public static partial class TargetCandidateService
{
    // Most-specific-first classification of a free-text target description.
    public static TargetCategory Classify(string? description)
    {
        var d = (description ?? string.Empty).ToLowerInvariant().Trim();
        if (d.Length == 0 || d.Contains("no target")) return TargetCategory.None;

        if (d.Contains("graveyard")) return TargetCategory.GraveyardCard;
        if (d.Contains("noncreature spell")) return TargetCategory.NoncreatureSpell;
        if (d.Contains("creature spell")) return TargetCategory.CreatureSpell;
        if (d.Contains("spell")) return TargetCategory.Spell;

        if (d.Contains("any target")) return TargetCategory.AnyTarget;

        var hasCreature = d.Contains("creature");
        var hasPlayer = d.Contains("player");
        var hasPw = d.Contains("planeswalker");
        if (hasCreature && hasPlayer) return TargetCategory.CreatureOrPlayer;
        if (hasCreature && hasPw) return TargetCategory.CreatureOrPlaneswalker;
        if (hasPlayer && hasPw) return TargetCategory.PlayerOrPlaneswalker;
        if (hasCreature) return TargetCategory.Creature;
        if (hasPw) return TargetCategory.Planeswalker;
        if (d.Contains("opponent")) return TargetCategory.Opponent;
        if (hasPlayer) return TargetCategory.Player;

        if (d.Contains("nonland permanent")) return TargetCategory.NonlandPermanent;
        if (d.Contains("permanent")) return TargetCategory.Permanent;
        if (d.Contains("artifact")) return TargetCategory.Artifact;
        if (d.Contains("enchantment")) return TargetCategory.Enchantment;
        if (d.Contains("land")) return TargetCategory.Land;
        return TargetCategory.None;
    }
}
```

> NOTE ordering: "spell" is matched before "creature"/"player" so "target creature spell" → CreatureSpell, not Creature. "graveyard" first so graveyard-card targets never fall to permanent categories. Adjust if a real description in the codebase breaks a case — add an `[InlineData]` row first, then fix `Classify`.

- [ ] **Step 4: Run, verify pass**

Run: same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Majik.Core/Targeting/TargetCandidateService.cs Majik.Core.Tests/Targeting/TargetCandidateServiceClassifyTests.cs
git commit -s -m "feat(targeting): TargetCategory + description classifier"
```

---

### Task 2: GatherCandidates (pool enumeration)

**Files:**
- Modify: `Majik.Core/Targeting/TargetCandidateService.cs`
- Test: `Majik.Core.Tests/Targeting/TargetCandidateServiceGatherTests.cs`

- [ ] **Step 1: Write the failing test.** Build a minimal game with `TestDataBuilder` (see `Majik.Core.Tests/Helpers/TestDataBuilder.cs`): two players, a creature + a planeswalker on a battlefield, a spell on the stack.

```csharp
using System.Linq;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Xunit;

namespace Majik.Core.Tests.Targeting;

public class TargetCandidateServiceGatherTests
{
    [Fact]
    public void AnyTarget_includes_both_players_creatures_and_planeswalkers()
    {
        var (ctx, caster) = TargetingTestWorld.Build(); // helper: see Step 3 note
        var pool = TargetCandidateService.GatherCandidates("any target", ctx, caster);
        pool.OfType<Player>().Should().HaveCount(2);
        pool.OfType<Creature>().Should().NotBeEmpty();
        pool.OfType<Planeswalker>().Should().NotBeEmpty();
    }

    [Fact]
    public void TargetCreature_returns_only_creatures()
    {
        var (ctx, caster) = TargetingTestWorld.Build();
        var pool = TargetCandidateService.GatherCandidates("target creature", ctx, caster);
        pool.Should().OnlyContain(o => o is Creature);
        pool.Should().NotBeEmpty();
    }

    [Fact]
    public void TargetPlayer_returns_only_players()
    {
        var (ctx, caster) = TargetingTestWorld.Build();
        var pool = TargetCandidateService.GatherCandidates("target player", ctx, caster);
        pool.Should().OnlyContain(o => o is Player);
        pool.Should().HaveCount(2);
    }

    [Fact]
    public void None_category_returns_empty()
    {
        var (ctx, caster) = TargetingTestWorld.Build();
        TargetCandidateService.GatherCandidates("no target", ctx, caster).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run, verify fail** (`GatherCandidates` + helper missing).

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter FullyQualifiedName~TargetCandidateServiceGatherTests`
Expected: FAIL.

- [ ] **Step 3: Implement `GatherCandidates` + a small test world helper.**

Add to `TargetCandidateService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Majik.Core.Cards;
using Majik.Core.Players;

public static partial class TargetCandidateService
{
    public static IReadOnlyList<object> GatherCandidates(string? description, GameContext ctx, Player caster)
    {
        var cat = Classify(description);
        if (cat == TargetCategory.None) return Array.Empty<object>();

        IEnumerable<Permanent> AllPermanents() =>
            ctx.AllPlayers.SelectMany(p => p.Zones.Battlefield.GetCards()).OfType<Permanent>();
        IEnumerable<Creature> Creatures() => AllPermanents().OfType<Creature>();
        IEnumerable<Planeswalker> Walkers() => AllPermanents().OfType<Planeswalker>();
        IEnumerable<Player> Players() => ctx.AllPlayers;

        IEnumerable<object> raw = cat switch
        {
            TargetCategory.AnyTarget => Creatures().Cast<object>().Concat(Walkers()).Concat(Players()),
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
            TargetCategory.Spell => ctx.Stack.GetAll().OfType<Majik.Core.Spells.ISpell>(),
            TargetCategory.NoncreatureSpell => ctx.Stack.GetAll().OfType<Majik.Core.Spells.ISpell>()
                .Where(s => !SpellIsCreature(s)),
            TargetCategory.CreatureSpell => ctx.Stack.GetAll().OfType<Majik.Core.Spells.ISpell>()
                .Where(SpellIsCreature),
            TargetCategory.GraveyardCard => ctx.AllPlayers.SelectMany(p => p.Zones.Graveyard.GetCards()).Cast<object>(),
            _ => Array.Empty<object>(),
        };

        // Exclude untargetable objects (hexproof/shroud/protection) so the UI
        // never offers an illegal target. Players are always targetable.
        return raw.Where(o => o is Player || IsTargetableNow(o, caster)).Distinct().ToList();
    }

    private static bool SpellIsCreature(Majik.Core.Spells.ISpell s) =>
        s.Card?.HasType(CardType.Creature) == true;

    private static bool IsTargetableNow(object o, Player caster)
    {
        // Reuse the existing keyword-gated legality check. Verify the real
        // TargetLegality API name/signature and adapt this call.
        if (o is Permanent perm) return TargetLegality.CanBeTargetedBy(perm, caster);
        return true;
    }
}
```

> VERIFY the real `TargetLegality` method name/signature (the explore named the class but not the exact method). If it takes `(target, byController)` adapt `IsTargetableNow`. If protection/shroud needs the source spell rather than the caster, pass what it needs. The contract: exclude objects that cannot legally be targeted by this caster right now.

Add the test helper `Majik.Core.Tests/Targeting/TargetingTestWorld.cs` building `(GameContext ctx, Player caster)` with 2 players, ≥1 creature + ≥1 planeswalker on a battlefield, using `TestDataBuilder`. (Mirror an existing test that constructs a `GameContext`; reuse builders, don't hand-roll.)

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Majik.Core/Targeting/TargetCandidateService.cs Majik.Core.Tests/Targeting/
git commit -s -m "feat(targeting): central candidate pool enumeration by category"
```

---

### Task 3: BuildLegalityPredicate

**Files:**
- Modify: `Majik.Core/Targeting/TargetCandidateService.cs`
- Test: `Majik.Core.Tests/Targeting/TargetCandidateServicePredicateTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Xunit;

namespace Majik.Core.Tests.Targeting;

public class TargetCandidateServicePredicateTests
{
    [Fact]
    public void Creature_predicate_accepts_creature_rejects_land()
    {
        var pred = TargetCandidateService.BuildLegalityPredicate("target creature");
        pred.Should().NotBeNull();
        var (ctx, _) = TargetingTestWorld.Build();
        var creature = ctx.AllPlayers.SelectMany(p => p.Zones.Battlefield.GetCards()).OfType<Creature>().First();
        pred!(creature).Should().BeTrue();
        pred(ctx.AllPlayers.First()).Should().BeFalse(); // a Player is not a creature
    }

    [Fact]
    public void AnyTarget_predicate_accepts_player()
    {
        var pred = TargetCandidateService.BuildLegalityPredicate("any target");
        pred!(/* a Player */ default(Player)!).Should().BeTrue(); // replace with a real player from the world
    }

    [Fact]
    public void None_returns_null_predicate()
    {
        TargetCandidateService.BuildLegalityPredicate("no target").Should().BeNull();
    }
}
```

> Fix the `AnyTarget_predicate_accepts_player` body to use a real `Player` from `TargetingTestWorld.Build()` rather than `default`.

- [ ] **Step 2: Run, verify fail.**

Run: `dotnet test ... --filter FullyQualifiedName~TargetCandidateServicePredicateTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

```csharp
public static partial class TargetCandidateService
{
    public static Func<object, bool>? BuildLegalityPredicate(string? description)
    {
        var cat = Classify(description);
        return cat switch
        {
            TargetCategory.None => null,
            TargetCategory.AnyTarget => o => o is Creature || o is Planeswalker || o is Player,
            TargetCategory.Creature => o => o is Creature,
            TargetCategory.Planeswalker => o => o is Planeswalker,
            TargetCategory.Player => o => o is Player,
            TargetCategory.Opponent => o => o is Player,
            TargetCategory.CreatureOrPlayer => o => o is Creature || o is Player,
            TargetCategory.CreatureOrPlaneswalker => o => o is Creature || o is Planeswalker,
            TargetCategory.PlayerOrPlaneswalker => o => o is Player || o is Planeswalker,
            TargetCategory.Permanent => o => o is Permanent,
            TargetCategory.NonlandPermanent => o => o is Permanent p && !p.HasType(CardType.Land),
            TargetCategory.Artifact => o => o is Permanent p && p.HasType(CardType.Artifact),
            TargetCategory.Enchantment => o => o is Permanent p && p.HasType(CardType.Enchantment),
            TargetCategory.Land => o => o is Permanent p && p.HasType(CardType.Land),
            TargetCategory.Spell => o => o is Majik.Core.Spells.ISpell,
            TargetCategory.NoncreatureSpell => o => o is Majik.Core.Spells.ISpell s && !SpellIsCreature(s),
            TargetCategory.CreatureSpell => o => o is Majik.Core.Spells.ISpell s && SpellIsCreature(s),
            TargetCategory.GraveyardCard => o => o is ICard,
            _ => null,
        };
    }
}
```

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Majik.Core/Targeting/TargetCandidateService.cs Majik.Core.Tests/Targeting/TargetCandidateServicePredicateTests.cs
git commit -s -m "feat(targeting): category-derived resolution legality predicate"
```

---

### Task 4: Wire into TargetCollection.CollectAsync

**Files:**
- Modify: `Majik.Core/Targeting/TargetCollection.cs`
- Test: `Majik.Core.Tests/Targeting/TargetCollectionCentralPoolTests.cs`

- [ ] **Step 1: Read `CollectAsync`.** Identify the line `var live = req.ResolveCandidates(ctx);` (~68), the casting `Player` in scope, and the `spell` (if any) whose `TargetLegalityPredicate` can be stamped.

- [ ] **Step 2: Write the failing test** — an "any target" request with an empty pool now yields a pool containing the opponent player when collected.

```csharp
[Fact]
public async Task EmptyPool_AnyTarget_collects_central_pool_including_player()
{
    var (ctx, caster) = TargetingTestWorld.Build();
    var req = new TargetRequest("any target", 1, 1, System.Array.Empty<object>());
    // Use an auto-pick agent that records the offered candidate pool.
    var (agent, offered) = RecordingTargetAgent.Create();
    var collection = /* construct TargetCollection per its real ctor */;
    await collection.CollectAsync(/* req, ctx, caster, agent ... per real signature */);
    offered().OfType<Player>().Should().NotBeEmpty();
}
```

> ADAPT to the real `TargetCollection` API (ctor + `CollectAsync` signature). If a recording agent doesn't exist, assert instead at the seam: call a small extracted method `ResolveLivePool(req, ctx, caster)` that returns the pool, and test that directly. Prefer extracting `ResolveLivePool` so the central-pool logic is unit-testable without the full agent flow.

- [ ] **Step 3: Implement.** Extract and use:

```csharp
// In TargetCollection (or a static helper): resolve the live pool, falling
// back to the central service when the card ships no candidates.
internal static IReadOnlyList<object> ResolveLivePool(TargetRequest req, GameContext ctx, Player caster)
{
    var live = req.ResolveCandidates(ctx);
    if (live.Count == 0)
    {
        var central = TargetCandidateService.GatherCandidates(req.Description, ctx, caster);
        if (central.Count > 0) live = central;
    }
    return live;
}
```

Replace the inline `req.ResolveCandidates(ctx)` at the prompt seam with `ResolveLivePool(req, ctx, caster)`. Then stamp the recheck predicate at cast time when applicable:

```csharp
// After choosing targets for a spell, if the spell has no predicate, stamp a
// category-derived one so StackResolver's CR 608.2b recheck stays honest.
if (spell != null && spell.TargetLegalityPredicate == null)
{
    var pred = TargetCandidateService.BuildLegalityPredicate(req.Description);
    if (pred != null) spell.TargetLegalityPredicate = pred;
}
```

> VERIFY where `spell` is reachable in `CollectAsync` (targets may be collected for abilities too, which have no `Spell`). Only stamp when a `Spell` is in scope. If `CollectAsync` doesn't see the spell, stamp in the caller that does (e.g. `SpellCastFlow`) — find where `ChosenTargets` is assigned to the spell and add the stamp there. Keep the stamp in ONE place.

- [ ] **Step 4: Run, verify pass.** Run the new test + the full targeting test folder:

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter FullyQualifiedName~Targeting`
Expected: PASS, no regressions.

- [ ] **Step 5: Commit**

```bash
git add Majik.Core/Targeting/TargetCollection.cs Majik.Core.Tests/Targeting/TargetCollectionCentralPoolTests.cs
git commit -s -m "feat(targeting): central pool fallback + recheck-predicate stamp at cast"
```

---

### Task 5: PlayerCandidateDto + PromptDto/PromptPayload + ChooseTargetsAsync + BuildPrompt

**Files:**
- Modify: `Majik.Core.Api/Dtos/Dtos.cs`
- Modify: `Majik.Core.Api/RemoteAgent.cs`
- Modify: `Majik.Core.Api/GameFacade.cs`
- Test: `Majik.Core.Api.Tests/PlayerTargetCandidateWireTests.cs`

- [ ] **Step 1: Write the failing test** — a targets prompt whose pool includes a player ships `PlayerCandidates` and still validates the player id inbound. Mirror `Majik.Core.Api.Tests/SacrificeAnotherCreatureLivePlayTests.cs` harness.

```csharp
[Fact]
public async Task TargetsPrompt_with_player_in_pool_ships_PlayerCandidates_and_accepts_player_id()
{
    // Arrange a live RemoteAgent + a TargetRequest whose ResolveCandidates yields
    // [a creature, the opponent player]. Drive ChooseTargetsAsync; capture the
    // PromptDto via GameFacade.BuildPrompt (or PendingPayload).
    // Assert: payload.PlayerCandidates has the opponent (id/name/life).
    // Then Submit a ChooseTargetsCommand with the player's id and assert the
    // awaited result contains that Player.
}
```

> Use the same in-process `RemoteAgent` test pattern the choiceView wire test used (`RemoteAgentTests` / the sacrifice live-play test). Build the candidate list to include a `Player`.

- [ ] **Step 2: Run, verify fail.**

Run: `dotnet test Majik.Core.Api.Tests/Majik.Core.Api.Tests.csproj --filter FullyQualifiedName~PlayerTargetCandidateWireTests`
Expected: FAIL — `PlayerCandidates` missing.

- [ ] **Step 3: Implement.**

`Dtos.cs` — add the DTO and the field (trailing optional, mirror the `ChoiceView` precedent from PR #2959):

```csharp
public sealed record PlayerCandidateDto(System.Guid Id, string Name, int Life);
```

Add to `PromptDto` as a new trailing optional parameter:

```csharp
    IReadOnlyList<PlayerCandidateDto>? PlayerCandidates = null);
```

`RemoteAgent.cs` `PromptPayload` record — add trailing optional:

```csharp
    IReadOnlyList<PlayerCandidateDto>? PlayerCandidates = null);
```

`RemoteAgent.cs` `ChooseTargetsAsync` (~line 803-811) — populate players alongside cards:

```csharp
if (candidates.Count > 0)
{
    var cardSnapshots = candidates.OfType<ICard>().Select(StateSnapshotter.SnapshotCard).ToList();
    var playerSnapshots = candidates.OfType<Player>()
        .Select(p => new PlayerCandidateDto(p.Id, p.Name, p.LifeTotal)).ToList();
    _pendingPayload = new PromptPayload(
        Candidates: cardSnapshots.Count > 0 ? cardSnapshots : null,
        Label: request.Description,
        PlayerCandidates: playerSnapshots.Count > 0 ? playerSnapshots : null);
}
```

> Use named args (the codebase requires named args when copying fields — see the fetchland lesson). Keep `_pendingTargetCandidates = candidates;` (full pool, unchanged) so inbound validation still sees the player.

`GameFacade.cs` `BuildPrompt` (~line 1493-1504) — copy through:

```csharp
            PlayerCandidates: payload?.PlayerCandidates);
```

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Majik.Core.Api/Dtos/Dtos.cs Majik.Core.Api/RemoteAgent.cs Majik.Core.Api/GameFacade.cs Majik.Core.Api.Tests/PlayerTargetCandidateWireTests.cs
git commit -s -m "feat(wire): ship PlayerCandidates on targets prompts"
```

---

### Task 6: StackResolver recheck regression coverage

**Files:**
- Test: `Majik.Core.Tests/Services/StackResolverCategoryRecheckTests.cs`

- [ ] **Step 1: Write the test** — a spell with NO per-card predicate, category "target creature", whose only chosen target is now a non-creature (e.g. became a Player or a land), is countered by the CR 608.2b recheck after the Task 4 stamp.

```csharp
[Fact]
public void Spell_with_category_predicate_is_countered_when_target_illegal()
{
    // Build a spell whose TargetLegalityPredicate was stamped from "target creature".
    // Set its ChosenTargets to a non-creature. Run StackResolver. Assert the spell
    // went to the graveyard (countered) — same assertion style as
    // StackResolverTargetRecheckTests.
}
```

> Model on `Majik.Core.Tests/Services/StackResolverTargetRecheckTests.cs` (referenced by the explore). Reuse its setup.

- [ ] **Step 2: Run, verify pass** (Task 4 already stamps the predicate; this is regression coverage). If it fails because the stamp isn't applied in this construction path, fix the stamp location from Task 4.

Run: `dotnet test Majik.Core.Tests/Majik.Core.Tests.csproj --filter FullyQualifiedName~StackResolverCategoryRecheckTests`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Majik.Core.Tests/Services/StackResolverCategoryRecheckTests.cs
git commit -s -m "test(targeting): category predicate recheck counters illegal target"
```

---

### Task 7: Regenerate OpenAPI snapshot + full engine suite + PR

**Files:**
- Modify: `Majik.Server.Tests/Snapshots/openapi.v1.json`

- [ ] **Step 1: Run the drift gate (expect fail).**

Run: `dotnet test Majik.Server.Tests/Majik.Server.Tests.csproj --filter FullyQualifiedName~OpenApiContractDrift`
Expected: FAIL — `PlayerCandidates`/`PlayerCandidateDto` not in snapshot.

- [ ] **Step 2: Regenerate the snapshot** per `majik.core/CLAUDE.md` "OpenAPI contract" (emit `/openapi/v1.json` from the in-process host, normalize, overwrite `Majik.Server.Tests/Snapshots/openapi.v1.json`). Confirm it now contains `playerCandidates` + `PlayerCandidateDto`.

- [ ] **Step 3: Run the FULL engine suite.**

Run: `dotnet build Majik.sln && dotnet test Majik.sln`
Expected: all green (engine, api, server drift, bot). Investigate any bot-targeting regression from fuller pools.

- [ ] **Step 4: Commit + PR + auto-merge**

```bash
git add Majik.Server.Tests/Snapshots/openapi.v1.json docs/superpowers/
git commit -s -m "chore(contract): regenerate openapi snapshot for PlayerCandidates"
git push -u origin HEAD
gh pr create --repo bg9m9r/majik --title "feat(targeting): central candidate pools + player-as-target wire" \
  --body "Implements docs/superpowers/specs/2026-06-16-targeting-overhaul-player-targets-design.md (engine + wire half).

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
gh pr merge --repo bg9m9r/majik --squash --auto
```

- [ ] **Step 5: STOP — hand back for api deploy.** Phase B (portal) requires the deployed api contract. Report the PR; the operator deploys majik-api (manual; auto-deploy off) before Phase B regenerates the portal client.

---

# PHASE B — PORTAL (majik.portal)

Run only AFTER Phase A is merged AND majik-api is deployed (live `/openapi/v1.json` carries `playerCandidates`). Branch off `main`: `feat/player-target-hud`. DCO `-s`. Co-Authored-By line.

### Task 8: Refresh OpenAPI client + PromptEnvelope.playerCandidates

**Files:**
- Modify: `majik.portal/openapi.json`, generated client (gitignored — regen)
- Modify: `src/app/core/match/match.types.ts`

- [ ] **Step 1: Regenerate the client from the live (deployed) api.** Run the portal's `npm run openapi` (fetches live `/openapi/v1.json`). Confirm `openapi.json` now has `playerCandidates`/`PlayerCandidateDto`. (If deploying locally instead, run the core server and point the fetch at it — same as the choiceView portal PR.)

- [ ] **Step 2: Add to `PromptEnvelope`** (`match.types.ts`):

```ts
  playerCandidates?: { id: string; name: string; life: number }[];
```

- [ ] **Step 3: Build to typecheck.**

Run: `npm run build`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add majik.portal/openapi.json src/app/core/match/match.types.ts
git commit -s -m "feat(match): playerCandidates on PromptEnvelope + openapi regen"
```

---

### Task 9: SelectionService includes player ids

**Files:**
- Modify: `src/app/core/match/selection.service.ts`
- Test: `src/app/core/match/selection.service.spec.ts`

- [ ] **Step 1: Write the failing test**

```ts
it('includes playerCandidates ids in the targets candidate set', () => {
  svc.setBoardInstanceIds(new Set(['c1', 'pA', 'pB'])); // cards + players are board-locatable
  svc.setPrompt({
    gameId: 'g', playerId: 'me', expectedKinds: ['ChooseTargetsCommand'],
    candidates: [{ instanceId: 'c1' } as any],
    playerCandidates: [{ id: 'pA', name: 'A', life: 20 }, { id: 'pB', name: 'B', life: 20 }],
    label: 'Bolt: any target',
  } as any);
  const m = svc.mode();
  expect(m?.kind).toBe('targets');
  expect(m!.candidateIds.has('pA')).toBe(true);
  expect(m!.candidateIds.has('c1')).toBe(true);
});
```

- [ ] **Step 2: Run, verify fail.**

Run: `npx ng test --no-watch --include='**/selection.service.spec.ts'`
Expected: FAIL — `pA` not in candidateIds.

- [ ] **Step 3: Implement.** In the `mode()` computed, for `kind === 'targets'`, union player ids into the candidate id set and the locatable check:

```ts
if (kind === 'targets' || kind === 'choice') {
  const cardIds = (p.candidates ?? []).map(c => c.instanceId);
  const playerIds = kind === 'targets' ? (p.playerCandidates ?? []).map(pc => pc.id) : [];
  const ids = [...cardIds, ...playerIds];
  if (ids.length === 0) return null;
  const board = this._boardIds();
  if (!ids.every(id => board.has(id))) return null;
  const { min, max } = this.bounds(kind, p);
  return { kind, min, max, candidateIds: new Set(ids),
    sourceLabel: p.label ?? p.description ?? '', choiceKind: p.choiceView?.kind,
    cancellable: kind === 'targets' };
}
```

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS (and existing specs stay green).

- [ ] **Step 5: Commit**

```bash
git add src/app/core/match/selection.service.ts src/app/core/match/selection.service.spec.ts
git commit -s -m "feat(match): SelectionService merges player target candidates"
```

---

### Task 10: boardInstanceIds includes player ids

**Files:**
- Modify: `src/app/routes/match/match.ts`
- Test: `src/app/routes/match/match.page.spec.ts` (or the spec that covers `boardInstanceIds`)

- [ ] **Step 1: Write the failing test** for `boardInstanceIds`:

```ts
it('boardInstanceIds includes player ids', () => {
  const state = { players: [
    { id: 'pA', battlefield: { cards: [{ instanceId: 'c1' }] }, hand: { cards: [] } },
    { id: 'pB', battlefield: { cards: [] }, hand: { cards: [] } },
  ] } as any;
  const ids = boardInstanceIds(state);
  expect(ids.has('pA')).toBe(true);
  expect(ids.has('pB')).toBe(true);
  expect(ids.has('c1')).toBe(true);
});
```

- [ ] **Step 2: Run, verify fail.**

Run: `npx ng test --no-watch --include='**/match.page.spec.ts'`
Expected: FAIL — player ids absent.

- [ ] **Step 3: Implement** in `boardInstanceIds` (`match.ts:~1039`):

```ts
export function boardInstanceIds(state: GameState | null): Set<string> {
  const ids = new Set<string>();
  for (const p of state?.players ?? []) {
    ids.add(p.id); // players are always board-locatable (HUD on screen)
    for (const c of p.battlefield?.cards ?? []) ids.add(c.instanceId);
    for (const c of p.hand?.cards ?? []) ids.add(c.instanceId);
  }
  return ids;
}
```

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/routes/match/match.ts src/app/routes/match/match.page.spec.ts
git commit -s -m "feat(match): treat player ids as board-locatable"
```

---

### Task 11: Clickable player HUD + affordance

**Files:**
- Modify: `src/app/ui/player-hud.component.ts`
- Modify: `src/app/routes/match/components/board.component.ts`
- Test: `src/app/ui/player-hud.component.spec.ts` (create), extend `board.component.spec.ts`

- [ ] **Step 1: Write the failing tests.**

player-hud:

```ts
it('marks the HUD targetable/dimmed/selected', () => {
  const f = TestBed.createComponent(PlayerHudComponent);
  f.componentRef.setInput('player', { id: 'pA', name: 'A', life: 20, hand: { cards: [] }, library: { cards: [] }, graveyard: { cards: [] }, exile: { cards: [] }, battlefield: { cards: [] }, mana: {} } as any);
  f.componentRef.setInput('side', 'opponent');
  f.componentRef.setInput('targetable', true);
  f.detectChanges();
  const hud = (f.nativeElement as HTMLElement).querySelector('.player-hud')!;
  expect(hud.getAttribute('data-targetable')).toBe('true');
  expect(hud.getAttribute('data-player-id')).toBe('pA');
});
```

board (HUD click auto-submits a single player target):

```ts
it('auto-submits a single player target when the HUD is clicked', () => {
  const f = TestBed.createComponent(BoardComponent);
  const cmp = f.componentInstance; const emitted: any[] = [];
  cmp.boardDecision.subscribe((d: any) => emitted.push(d));
  f.componentRef.setInput('state', { players: [
    { id: 'me', name: 'Me', battlefield: { cards: [] }, hand: { cards: [] } },
    { id: 'foe', name: 'Foe', battlefield: { cards: [] }, hand: { cards: [] } },
  ] } as any);
  f.componentRef.setInput('selfPlayerIds', ['me']);
  f.detectChanges();
  svc.setBoardInstanceIds(new Set(['foe']));
  svc.setPrompt({ gameId: 'g', playerId: 'me', expectedKinds: ['ChooseTargetsCommand'], candidates: [], playerCandidates: [{ id: 'foe', name: 'Foe', life: 20 }], label: 'Bolt' } as any);
  f.detectChanges();
  cmp.onPlayerHudClick({ id: 'foe' } as any);
  expect(emitted).toEqual([{ kind: 'targets', targetInstanceIds: ['foe'] }]);
});
```

- [ ] **Step 2: Run, verify fail.**

Run: `npx ng test --no-watch --include='**/player-hud.component.spec.ts'` then `--include='**/board.component.spec.ts'`
Expected: FAIL — inputs / `onPlayerHudClick` missing.

- [ ] **Step 3: Implement.**

`player-hud.component.ts`: add inputs + attrs + co-located CSS (mirror `card-view`):

```ts
readonly targetable = input(false);
readonly dimmed = input(false);
readonly selectedForTarget = input(false);
```

On the `.player-hud` root add:

```html
[attr.data-player-id]="player()?.id"
[attr.data-targetable]="targetable() ? 'true' : null"
[attr.data-dimmed]="dimmed() ? 'true' : null"
[attr.data-selected]="selectedForTarget() ? 'true' : null"
```

Add to `styles: [ ... ]` the same three rules used by `card-view` but scoped to `.player-hud[data-…]` (outline/box-shadow/cursor for targetable; opacity+pointer-events:none for dimmed; stronger outline for selected).

`board.component.ts`: add the click handler + bind the HUDs:

```ts
onPlayerHudClick(player: { id: string }): void {
  const m = this.selection.mode();
  if (!m || m.kind !== 'targets') return;
  if (!m.candidateIds.has(player.id)) return;
  this.selection.toggle(player.id);
  this.maybeAutoSubmit(m);
}
```

Where the two `<app-player-hud>`s render, add:

```html
<app-player-hud
  ...existing [player]/[active]/side/label...
  [targetable]="isTargetable(opponent()?.id ?? '')"
  [dimmed]="isDimmed(opponent()?.id ?? '')"
  [selectedForTarget]="isSelectedForTarget(opponent()?.id ?? '')"
  (click)="onPlayerHudClick(opponent()!)" />
```

(and the same for `self()`). `isTargetable`/`isDimmed`/`isSelectedForTarget` already consult `selection.mode().candidateIds` / `selection.selected()`, so player ids flow through unchanged.

- [ ] **Step 4: Run, verify pass.** Both specs. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/ui/player-hud.component.ts src/app/ui/player-hud.component.spec.ts src/app/routes/match/components/board.component.ts src/app/routes/match/components/board.component.spec.ts
git commit -s -m "feat(board): clickable player HUD as a target with affordance"
```

---

### Task 12: Full portal suite + build + PR

- [ ] **Step 1: Full suite.**

Run: `npx ng test --no-watch`
Expected: all green.

- [ ] **Step 2: Build.**

Run: `npm run build`
Expected: success (pre-existing bundle-budget warning OK).

- [ ] **Step 3: Manual smoke (if dev server available):** cast a damage spell with "any target" — the opponent HUD + legal creatures highlight, others dim; click the HUD → auto-submits; spell resolves to the player.

- [ ] **Step 4: PR + auto-merge**

```bash
git add docs/superpowers/ 2>/dev/null; git push -u origin HEAD
gh pr create --repo bg9m9r/majik.portal --title "feat(match): click a player's HUD to target them" \
  --body "Portal half of docs/superpowers/specs/2026-06-16-targeting-overhaul-player-targets-design.md. Requires the engine PR's api deploy first.

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
gh pr merge --repo bg9m9r/majik.portal --squash --auto
```

---

## Self-review

- **Spec coverage:** central service (T1-T3), CollectAsync seam + recheck stamp (T4), recheck regression (T6), wire `PlayerCandidates` (T5), drift snapshot (T7), portal envelope+client (T8), SelectionService player ids (T9), boardInstanceIds (T10), clickable HUD (T11), suites (T7/T12). Scope boundary (stack/graveyard → modal, players+permanents clickable) honored — no stack/graveyard click task. All spec sections covered.
- **Placeholder scan:** the only deliberately-open items are flagged "VERIFY/ADAPT" against real APIs (TargetLegality method name; TargetCollection ctor/signature; where the spell is reachable to stamp the predicate). These are real-code reconciliations, not vague requirements; each has a concrete fallback.
- **Type consistency:** `PlayerCandidateDto(Id,Name,Life)` ↔ wire `playerCandidates {id,name,life}` ↔ envelope field. Decision shape unchanged (`targetInstanceIds`), player id rides in it. `candidateIds`/`mode()` reused from PR #140. `GatherCandidates`/`BuildLegalityPredicate`/`Classify` signatures consistent across T1-T4.
- **Sequencing:** Phase A merged + api deployed before Phase B regen (T7 step 5 hard stop). Mirrors the prior cross-repo flow.
- **Bot safety:** T7 step 3 runs the full solution incl. bot suites; fuller pools must not break `TargetPolicy`.
