# Stack + Graveyard Click-to-Target Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the human click a spell on the stack (counterspell targets) and a card in a graveyard to choose it as a target, reusing the `SelectionService` + affordance flow from PRs #140/#141/#3005.

**Architecture:** Stack spells (`ISpell`) get a new wire candidate (`StackCandidateDto` on `PromptDto`) + one `CandidateMatchesId` arm; graveyard cards (`ICard`) already ride the wire and need no engine change. The portal merges stack ids + graveyard card ids into `SelectionService.candidateIds` and the board-locatable id set, makes the already-rendered stack chip clickable (auto-expanded under a stack-target prompt), and opens the existing graveyard zone-modal in "target mode" with the standard affordance.

**Tech Stack:** C#/.NET 10 (xUnit + FluentAssertions); Angular 21 + NgRx Signals (`ng test --no-watch`/jsdom). Cross-repo, contract change, api-first deploy.

**Spec:** `majik.core/docs/superpowers/specs/2026-06-16-stack-graveyard-target-surface-design.md`

---

## Two phases

- **Phase A — Engine + wire (majik.core, PR to `bg9m9r/majik`).** Tasks 1–3. Auto-merge, then deploy majik-api (manual).
- **Phase B — Portal (majik.portal, PR to `bg9m9r/majik.portal`).** Tasks 4–9. Needs Phase A's contract live.

Run Phase A → merged → api deployed BEFORE Phase B regenerates the portal client.

## Verify-against-real-code anchors

- `CandidateMatchesId`: `Majik.Core.Api/RemoteAgent.cs:653-658`. `ChooseTargetsAsync`: ~784-834 (post-#3005, has the player snapshot block to mirror). `PromptPayload` record: ~1228.
- `Spell.Id` / `ISpell`: `Majik.Core/Spells/Spell.cs:25`; `ISpell.Controller`, `ISpell.Card`. Confirm `Controller` is non-null and exposes `.Id`, and `Card.Name`.
- `PromptDto` + `PlayerCandidateDto`: `Majik.Core.Api/Dtos/Dtos.cs` (PlayerCandidateDto is the shape to mirror). `BuildPrompt`: `GameFacade.cs:~1490`.
- `StackObjectDto`/`SnapshotStackObject`: `StateSnapshotter.cs:275-299` (`Id == spell.Id` confirms the id the portal sends).
- Drift gate + regen escape hatch (the engine agent used `MAJIK_REGEN_OPENAPI=1`): `Majik.Server.Tests/OpenApiContractDriftTests.cs`; snapshot `Majik.Server.Tests/Snapshots/openapi.v1.json`.
- Portal: `selection.service.ts` (`mode()` 56-94), `match.ts` (`boardInstanceIds` ~1039, prompt→envelope map), `board.component.ts` (stack render ~538-606, `reversedStack()` ~1532, `onBoardCardClick`/`isTargetable`/`isDimmed`/`isSelectedForTarget`/`maybeAutoSubmit` ~792-886, zone-modal integration ~1041-1055), `zone-modal.component.ts`, `card-view.component.ts` affordance CSS, `match.types.ts` (`StackItem` 140-155, `PromptEnvelope`).

Build/test: `dotnet build Majik.sln`; `dotnet test Majik.sln`. Portal: `npx ng test --no-watch [--include='**/<f>.spec.ts']`; `npm run build`.

---

# PHASE A — ENGINE + WIRE (majik.core)

Branch off `main`: `feat/stack-target-candidates`. DCO `-s`. Co-Authored-By line.

### Task 1: StackCandidateDto + CandidateMatchesId arm + ChooseTargetsAsync + BuildPrompt

**Files:**
- Modify: `Majik.Core.Api/Dtos/Dtos.cs`, `Majik.Core.Api/RemoteAgent.cs`, `Majik.Core.Api/GameFacade.cs`
- Test: `Majik.Core.Api.Tests/StackTargetCandidateWireTests.cs`

- [ ] **Step 1: Write the failing test** — mirror the player-target wire test (`PlayerTargetCandidateWireTests` from #3005). Build a `RemoteAgent` whose `ChooseTargetsAsync` is given a candidate pool containing a stack `ISpell`; assert the payload's `StackCandidates` carries `(spell.Id, spell.Card.Name, spell.Controller.Id)` and that submitting a `ChooseTargetsCommand` with `spell.Id` resolves to the spell.

```csharp
[Fact]
public async Task TargetsPrompt_with_stack_spell_ships_StackCandidates_and_accepts_spell_id()
{
    // Arrange: a live RemoteAgent + a TargetRequest whose resolved pool = [ an ISpell ].
    // (Reuse the harness from PlayerTargetCandidateWireTests; put an ISpell in the pool.)
    // Act: drive ChooseTargetsAsync; read PendingPayload / BuildPrompt.
    // Assert: payload.StackCandidates single entry with Id==spell.Id, CardName, ControllerId.
    // Then Submit ChooseTargetsCommand { TargetInstanceIds = [spell.Id] };
    //   awaited result contains the ISpell.
}
```

> Find how the #3005 player test constructs the agent + pool + drives Submit. Mirror it. To get an `ISpell` in a test, see how existing stack/counterspell tests build a spell on the stack (search `Majik.Core.Tests` for `ISpell`/`Stack.Push`).

- [ ] **Step 2: Run, verify fail.**

Run: `dotnet test Majik.Core.Api.Tests/Majik.Core.Api.Tests.csproj --filter FullyQualifiedName~StackTargetCandidateWireTests`
Expected: FAIL — `StackCandidates` missing.

- [ ] **Step 3: Implement.**

`Dtos.cs` — add the DTO (mirror `PlayerCandidateDto`) and the `PromptDto` field:

```csharp
public sealed record StackCandidateDto(System.Guid Id, string CardName, System.Guid ControllerId);
```

Add to `PromptDto` as a new trailing optional parameter (after `PlayerCandidates`):

```csharp
    IReadOnlyList<StackCandidateDto>? StackCandidates = null);
```

`RemoteAgent.cs` `PromptPayload` record — add trailing optional:

```csharp
    IReadOnlyList<StackCandidateDto>? StackCandidates = null);
```

`RemoteAgent.cs` `ChooseTargetsAsync` — in the same `if (candidates.Count > 0)` block that builds card + player snapshots, add stack snapshots and pass them (NAMED args):

```csharp
var stackSnapshots = candidates.OfType<Majik.Core.Spells.ISpell>()
    .Select(s => new StackCandidateDto(s.Id, s.Card.Name, s.Controller.Id)).ToList();
_pendingPayload = new PromptPayload(
    Candidates: cardSnapshots.Count > 0 ? cardSnapshots : null,
    Label: request.Description,
    PlayerCandidates: playerSnapshots.Count > 0 ? playerSnapshots : null,
    StackCandidates: stackSnapshots.Count > 0 ? stackSnapshots : null);
```

> Confirm `ISpell.Controller` is non-null in this path; if it can be null, fall back to `s.Controller?.Id ?? System.Guid.Empty` and note it. Keep `_pendingTargetCandidates = candidates;` unchanged (full pool incl. spells).

`RemoteAgent.cs` `CandidateMatchesId` — add the spell arm:

```csharp
private static bool CandidateMatchesId(object candidate, Guid id) => candidate switch
{
    ICard card => card.InstanceId == id,
    Player player => player.Id == id,
    Majik.Core.Spells.ISpell spell => spell.Id == id,
    _ => false,
};
```

`GameFacade.cs` `BuildPrompt` — copy through:

```csharp
            StackCandidates: payload?.StackCandidates);
```

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Majik.Core.Api/Dtos/Dtos.cs Majik.Core.Api/RemoteAgent.cs Majik.Core.Api/GameFacade.cs Majik.Core.Api.Tests/StackTargetCandidateWireTests.cs
git commit -s -m "feat(wire): ship StackCandidates on targets prompts + match spell ids inbound"
```

---

### Task 2: Graveyard regression guard

**Files:**
- Test: `Majik.Core.Api.Tests/GraveyardTargetCandidateWireTests.cs`

- [ ] **Step 1: Write the test** — a targets pool containing a graveyard `ICard` still ships it in `Candidates` and accepts its `InstanceId` inbound (proves no regression; graveyard needs no new field).

```csharp
[Fact]
public async Task TargetsPrompt_with_graveyard_card_rides_Candidates_unchanged()
{
    // pool = [ a card whose Zone is a graveyard ]. Assert payload.Candidates has it;
    // Submit ChooseTargetsCommand { [card.InstanceId] } resolves to the card.
}
```

- [ ] **Step 2: Run, verify pass** (should already pass — regression coverage).

Run: `dotnet test Majik.Core.Api.Tests/Majik.Core.Api.Tests.csproj --filter FullyQualifiedName~GraveyardTargetCandidateWireTests`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Majik.Core.Api.Tests/GraveyardTargetCandidateWireTests.cs
git commit -s -m "test(wire): graveyard card target rides Candidates unchanged"
```

---

### Task 3: Regenerate drift snapshot + full suite + PR

**Files:**
- Modify: `Majik.Server.Tests/Snapshots/openapi.v1.json`

- [ ] **Step 1: Run the drift gate (expect fail).**

Run: `dotnet test Majik.Server.Tests/Majik.Server.Tests.csproj --filter FullyQualifiedName~OpenApiContractDrift`
Expected: FAIL — `StackCandidates`/`StackCandidateDto` not in snapshot.

- [ ] **Step 2: Regenerate** per CLAUDE.md (the #3005 agent used `MAJIK_REGEN_OPENAPI=1`; confirm the env-var hook in `OpenApiContractDriftTests.cs` and use it, else emit `/openapi/v1.json` from the in-process host and overwrite the snapshot). Confirm it now contains `stackCandidates` + `StackCandidateDto`.

- [ ] **Step 3: Full engine suite.**

Run: `dotnet build Majik.sln && dotnet test Majik.sln`
Expected: all green (engine, api, server drift, bot). The bot is unaffected (it doesn't opt into synthesized candidates — `WantsSynthesizedTargetCandidates` is false — and this change only adds a wire field), but run the bot suite to be sure.

- [ ] **Step 4: Commit + PR + auto-merge**

```bash
git add Majik.Server.Tests/Snapshots/openapi.v1.json docs/superpowers/
git commit -s -m "chore(contract): regenerate openapi snapshot for StackCandidates"
git push -u origin HEAD
gh pr create --repo bg9m9r/majik --title "feat(targeting): stack-spell target candidates on the wire" \
  --body "Engine + wire half of docs/superpowers/specs/2026-06-16-stack-graveyard-target-surface-design.md. Graveyard needs no engine change.

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
gh pr merge --repo bg9m9r/majik --squash --auto
```

- [ ] **Step 5: STOP — hand back for api deploy.** Phase B needs the deployed contract. Report the PR; operator deploys majik-api (manual) before Phase B.

---

# PHASE B — PORTAL (majik.portal)

Run only AFTER Phase A merged AND majik-api deployed (live `/openapi/v1.json` carries `stackCandidates`). Branch off `main`: `feat/stack-graveyard-target-surface`. DCO `-s`. Co-Authored-By line.

### Task 4: OpenAPI client + PromptEnvelope.stackCandidates

**Files:**
- Modify: `majik.portal/openapi.json` (+ gitignored client regen), `src/app/core/match/match.types.ts`

- [ ] **Step 1: Regenerate the client** from the deployed api: `npm run openapi`. Confirm `openapi.json` has `stackCandidates`/`StackCandidateDto`. (`ng-openapi-gen.json` already has `ignoreUnusedModels: false` from #141, so the ref-only DTO emits.)

- [ ] **Step 2: Add to `PromptEnvelope`** (`match.types.ts`):

```ts
  stackCandidates?: { id: string; cardName: string; controllerId: string }[];
```

- [ ] **Step 3: Thread it through** the `prompt$ → PromptEnvelope` map in `match.ts` (alongside `playerCandidates`).

- [ ] **Step 4: Build to typecheck.** Run: `npm run build`. Expected: success.

- [ ] **Step 5: Commit**

```bash
git add majik.portal/openapi.json src/app/core/match/match.types.ts src/app/routes/match/match.ts
git commit -s -m "feat(match): stackCandidates on PromptEnvelope + openapi regen"
```

---

### Task 5: SelectionService merges stack + graveyard ids

**Files:**
- Modify: `src/app/core/match/selection.service.ts`
- Test: `src/app/core/match/selection.service.spec.ts`

- [ ] **Step 1: Write the failing test**

```ts
it('includes stackCandidates ids in the targets candidate set', () => {
  svc.setBoardInstanceIds(new Set(['s1']));
  svc.setPrompt({ gameId:'g', playerId:'me', expectedKinds:['ChooseTargetsCommand'],
    candidates: [], stackCandidates: [{ id:'s1', cardName:'Bolt', controllerId:'foe' }], label:'Counterspell' } as any);
  const m = svc.mode();
  expect(m?.kind).toBe('targets');
  expect(m!.candidateIds.has('s1')).toBe(true);
});

it('activates a targets mode for a graveyard card candidate when locatable', () => {
  svc.setBoardInstanceIds(new Set(['gy1'])); // graveyard id now locatable (Task 6)
  svc.setPrompt({ gameId:'g', playerId:'me', expectedKinds:['ChooseTargetsCommand'],
    candidates: [{ instanceId:'gy1' } as any], label:'Raise Dead' } as any);
  expect(svc.mode()?.candidateIds.has('gy1')).toBe(true);
});
```

- [ ] **Step 2: Run, verify fail.**

Run: `npx ng test --no-watch --include='**/selection.service.spec.ts'`
Expected: FAIL — `s1` not in candidateIds.

- [ ] **Step 3: Implement.** In `mode()` for `kind === 'targets'`, union stack ids (graveyard card ids already arrive via `p.candidates`):

```ts
if (kind === 'targets' || kind === 'choice') {
  const cardIds = (p.candidates ?? []).map(c => c.instanceId);
  const playerIds = kind === 'targets' ? (p.playerCandidates ?? []).map(pc => pc.id) : [];
  const stackIds = kind === 'targets' ? (p.stackCandidates ?? []).map(s => s.id) : [];
  const ids = [...cardIds, ...playerIds, ...stackIds];
  if (ids.length === 0) return null;
  const board = this._boardIds();
  if (!ids.every(id => board.has(id))) return null;
  const { min, max } = this.bounds(kind, p);
  return { kind, min, max, candidateIds: new Set(ids),
    sourceLabel: p.label ?? p.description ?? '', choiceKind: p.choiceView?.kind,
    cancellable: kind === 'targets' };
}
```

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS (+ existing specs green).

- [ ] **Step 5: Commit**

```bash
git add src/app/core/match/selection.service.ts src/app/core/match/selection.service.spec.ts
git commit -s -m "feat(match): SelectionService merges stack + graveyard target candidates"
```

---

### Task 6: boardInstanceIds includes graveyard + stack ids

**Files:**
- Modify: `src/app/routes/match/match.ts`
- Test: `src/app/routes/match/match.page.spec.ts`

- [ ] **Step 1: Write the failing test** for `boardInstanceIds`:

```ts
it('boardInstanceIds includes graveyard card ids and stack ids', () => {
  const state = { players: [
    { id:'me', battlefield:{cards:[]}, hand:{cards:[]}, graveyard:{cards:[{instanceId:'gy1'}]} },
  ], stack: [{ id:'s1' }] } as any;
  const ids = boardInstanceIds(state);
  expect(ids.has('gy1')).toBe(true);
  expect(ids.has('s1')).toBe(true);
});
```

- [ ] **Step 2: Run, verify fail.**

Run: `npx ng test --no-watch --include='**/match.page.spec.ts'`
Expected: FAIL.

- [ ] **Step 3: Implement** (`match.ts` `boardInstanceIds`):

```ts
export function boardInstanceIds(state: GameState | null): Set<string> {
  const ids = new Set<string>();
  for (const p of state?.players ?? []) {
    ids.add(p.id);
    for (const c of p.battlefield?.cards ?? []) ids.add(c.instanceId);
    for (const c of p.hand?.cards ?? []) ids.add(c.instanceId);
    for (const c of p.graveyard?.cards ?? []) ids.add(c.instanceId);
  }
  for (const s of state?.stack ?? []) ids.add(s.id);
  return ids;
}
```

> Confirm `GameState` exposes `stack` (it does — `StackItem[]`). Confirm the player has `graveyard?.cards`.

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/routes/match/match.ts src/app/routes/match/match.page.spec.ts
git commit -s -m "feat(match): graveyard + stack ids are board-locatable"
```

---

### Task 7: Clickable stack chip + auto-expand

**Files:**
- Modify: `src/app/routes/match/components/board.component.ts`
- Test: `src/app/routes/match/components/board.component.spec.ts`

- [ ] **Step 1: Write the failing tests**

```ts
it('auto-submits a stack-spell target when a stack item is clicked', () => {
  const f = TestBed.createComponent(BoardComponent);
  const cmp = f.componentInstance; const emitted: any[] = [];
  cmp.boardDecision.subscribe((d: any) => emitted.push(d));
  f.componentRef.setInput('state', { players: [], stack: [{ id:'s1', kind:'Spell', description:'Bolt' }] } as any);
  f.componentRef.setInput('selfPlayerIds', ['me']);
  f.detectChanges();
  svc.setBoardInstanceIds(new Set(['s1']));
  svc.setPrompt({ gameId:'g', playerId:'me', expectedKinds:['ChooseTargetsCommand'], candidates: [], stackCandidates:[{id:'s1', cardName:'Bolt', controllerId:'foe'}], label:'Counter' } as any);
  f.detectChanges();
  cmp.onStackItemClick({ id:'s1' } as any);
  expect(emitted).toEqual([{ kind:'targets', targetInstanceIds:['s1'] }]);
});

it('ignores a click on a non-candidate stack item', () => {
  // ...same setup, candidateIds has 's1'; click {id:'s2'} → no emit
});
```

- [ ] **Step 2: Run, verify fail.**

Run: `npx ng test --no-watch --include='**/board.component.spec.ts'`
Expected: FAIL — `onStackItemClick` missing.

- [ ] **Step 3: Implement.**

Add `onStackItemClick` + affordance helpers (the existing `isTargetable`/`isDimmed`/`isSelectedForTarget` already key off `candidateIds`/`selected`, so they work for stack ids unchanged):

```ts
onStackItemClick(item: { id: string }): void {
  const m = this.selection.mode();
  if (!m || m.kind !== 'targets') return;
  if (!m.candidateIds.has(item.id)) return;
  this.selection.toggle(item.id);
  this.maybeAutoSubmit(m);
}

// True when the active targets prompt carries stack candidates (auto-expand the chip).
readonly stackTargetActive = computed(() => {
  const m = this.selection.mode();
  return !!m && m.kind === 'targets' && (this.state()?.stack ?? []).some(s => m.candidateIds.has(s.id));
});
```

In the stack chip template (~538-606), bind each stack-item element:

```html
<div class="stack-item"
  [attr.data-stack-id]="item.id"
  [attr.data-targetable]="isTargetable(item.id) ? 'true' : null"
  [attr.data-dimmed]="isDimmed(item.id) ? 'true' : null"
  [attr.data-selected]="isSelectedForTarget(item.id) ? 'true' : null"
  (click)="onStackItemClick(item)">
  ...existing item content...
</div>
```

Co-locate the affordance CSS for `.stack-item[data-targetable='true']` etc. in the board component `styles[]` (jsdom can't load board.scss), mirroring the card-view rules. Drive the chip's expanded state from `stackTargetActive()` OR the user toggle (e.g. `[class.expanded]="expanded() || stackTargetActive()"` — wire to whatever signal currently controls expand/collapse; find it near `reversedStack()`).

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/routes/match/components/board.component.ts src/app/routes/match/components/board.component.spec.ts
git commit -s -m "feat(board): clickable stack-spell targets with auto-expand + affordance"
```

---

### Task 8: Graveyard zone-modal target mode

**Files:**
- Modify: `src/app/routes/match/components/zone-modal.component.ts`, `src/app/routes/match/components/board.component.ts`
- Test: extend `board.component.spec.ts` (+ `zone-modal.component.spec.ts` if present)

- [ ] **Step 1: Write the failing test** (board auto-opens the graveyard modal in target mode + a candidate card click auto-submits)

```ts
it('auto-opens the graveyard modal in target mode and submits a clicked graveyard card', () => {
  const f = TestBed.createComponent(BoardComponent);
  const cmp = f.componentInstance; const emitted: any[] = [];
  cmp.boardDecision.subscribe((d: any) => emitted.push(d));
  f.componentRef.setInput('state', { players: [
    { id:'me', name:'Me', battlefield:{cards:[]}, hand:{cards:[]}, graveyard:{cards:[{instanceId:'gy1', name:'Bear'}]} },
  ], stack: [] } as any);
  f.componentRef.setInput('selfPlayerIds', ['me']);
  f.detectChanges();
  svc.setBoardInstanceIds(new Set(['gy1']));
  svc.setPrompt({ gameId:'g', playerId:'me', expectedKinds:['ChooseTargetsCommand'], candidates:[{instanceId:'gy1'} as any], label:'Raise Dead' } as any);
  f.detectChanges();
  expect(cmp.graveyardTargetActive()).toBe(true); // modal should be open in target mode
  cmp.onGraveyardCardClick({ instanceId:'gy1' } as any);
  expect(emitted).toEqual([{ kind:'targets', targetInstanceIds:['gy1'] }]);
});
```

- [ ] **Step 2: Run, verify fail.**

Run: `npx ng test --no-watch --include='**/board.component.spec.ts'`
Expected: FAIL — `graveyardTargetActive`/`onGraveyardCardClick` missing.

- [ ] **Step 3: Implement.**

Board:

```ts
// Graveyard card ids that are current targets candidates.
private graveyardCandidateIds(): Set<string> {
  const m = this.selection.mode();
  if (!m || m.kind !== 'targets') return new Set();
  const ids = new Set<string>();
  for (const p of this.state()?.players ?? [])
    for (const c of p.graveyard?.cards ?? [])
      if (m.candidateIds.has(c.instanceId)) ids.add(c.instanceId);
  return ids;
}
readonly graveyardTargetActive = computed(() => this.graveyardCandidateIds().size > 0);

onGraveyardCardClick(card: { instanceId: string }): void {
  const m = this.selection.mode();
  if (!m || m.kind !== 'targets') return;
  if (!m.candidateIds.has(card.instanceId)) return;
  this.selection.toggle(card.instanceId);
  this.maybeAutoSubmit(m);
}
```

Auto-open: when `graveyardTargetActive()` is true, set the existing zone-modal open-state to the graveyard(s) containing candidates (reuse the board's zone-modal open signal at ~1041-1055). If candidates span both players, render both graveyard sections in the modal (pass the candidate-containing graveyards). Exiting target mode (prompt cleared / cancel) closes it.

`zone-modal.component.ts`: give the card cells the affordance + click. Add optional inputs the board feeds (e.g. `targetMode`, plus the `isTargetable`/`isDimmed`/`isSelectedForTarget` predicates as bound callbacks OR have the modal emit a `cardClicked` output the board handles). Prefer an OUTPUT to keep the modal dumb:

```html
<app-card-view [snapshot]="c" zone="other"
  [targetable]="targetMode() && targetable()(c.instanceId)"
  [dimmed]="targetMode() && dimmed()(c.instanceId)"
  [selectedForTarget]="targetMode() && selected()(c.instanceId)"
  (click)="targetMode() && cardClicked.emit(c)" />
```

Where the board renders `<app-zone-modal>`, pass `[targetMode]="graveyardTargetActive()"`,
the three predicate callbacks (`isTargetable`/`isDimmed`/`isSelectedForTarget` bound to the
component), and `(cardClicked)="onGraveyardCardClick($event)"`.

> ADAPT to the real `zone-modal` input/output surface (it currently takes `cards`/`kind`/zone
> + a close output). Keep it presentation-only; the board owns selection logic. If passing
> function inputs is awkward in this codebase's style, instead pass three boolean Sets
> (targetableIds/dimmedIds/selectedIds) computed by the board.

- [ ] **Step 4: Run, verify pass.** Same filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/app/routes/match/components/zone-modal.component.ts src/app/routes/match/components/board.component.ts src/app/routes/match/components/board.component.spec.ts
git commit -s -m "feat(board): graveyard zone-modal target mode (click a graveyard card as a target)"
```

---

### Task 9: Full portal suite + build + PR

- [ ] **Step 1:** `npx ng test --no-watch` → all green.
- [ ] **Step 2:** `npm run build` → success (pre-existing bundle-budget warning OK).
- [ ] **Step 3: Manual smoke (if dev server):** cast a counterspell while the opponent has a spell on the stack — the stack chip auto-expands, the spell glows, click → submits. Cast a graveyard-recursion spell — the graveyard modal opens in target mode, legal cards glow, click → submits.
- [ ] **Step 4: PR + auto-merge**

```bash
git add docs/superpowers/ 2>/dev/null; git push -u origin HEAD
gh pr create --repo bg9m9r/majik.portal --title "feat(match): click stack spells + graveyard cards as targets" \
  --body "Portal half of docs/superpowers/specs/2026-06-16-stack-graveyard-target-surface-design.md. Requires the engine PR's api deploy first.

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
gh pr merge --repo bg9m9r/majik.portal --squash --auto
```

---

## Self-review

- **Spec coverage:** stack wire (T1), graveyard regression (T2), drift (T3), envelope+client (T4), SelectionService stack+graveyard ids (T5), boardInstanceIds (T6), stack click+auto-expand (T7), graveyard modal target mode (T8), suites (T3/T9). Scope boundary (no exile, no stack-ability targets) honored. All spec sections covered.
- **Placeholder scan:** open items are flagged "VERIFY/ADAPT" against real APIs (ISpell.Controller nullability; the zone-modal input/output surface; the chip's expand signal). Each has a concrete fallback. No vague requirements.
- **Type consistency:** `StackCandidateDto(Id,CardName,ControllerId)` ↔ wire `stackCandidates {id,cardName,controllerId}` ↔ envelope field. Decision shape unchanged (`targetInstanceIds`); stack id (= `Spell.Id` = `StackItem.id` = `StackObjectDto.Id`) rides it. `candidateIds`/`mode()`/`maybeAutoSubmit`/affordance reused from #140/#141. `CandidateMatchesId` spell arm matches `spell.Id`.
- **Sequencing:** Phase A merged + api deployed before Phase B regen (T3 step 5 hard stop). Mirrors prior cross-repo flows.
- **No-duplication:** selection state stays in `SelectionService`; the zone-modal is presentation-only (board owns the toggle/auto-submit).
