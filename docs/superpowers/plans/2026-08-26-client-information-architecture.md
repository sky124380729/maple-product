# Client Information Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recompose the Maple Product client into a compact two-tab workbench with prominent character/runtime status, collapsed configuration groups, compact errors, and a read-only runtime log modal without changing automation behavior.

**Architecture:** Keep `StationaryAttackPage` as the state and Bridge orchestration boundary, extract visual workbench components, and preserve existing form names and command payloads. Add one bounded Host query that reads the latest 200 structured session-log rows and publishes them to React without exposing write access or touching an active session.

**Tech Stack:** React 19, TypeScript, Ant Design 6, Vitest/Testing Library, .NET 8 WPF/WebView2, xUnit.

---

### Task 1: Lock the product contract

**Files:**
- Modify: `docs/PRODUCT_SPEC.md`
- Modify: `docs/PHASE_1_ACCEPTANCE.md`
- Create: `docs/superpowers/specs/2026-08-26-client-information-architecture-design.md`

- [ ] Document the two-tab hierarchy, state-panel placement, compact messages, collapsed parameters, log boundary, and behavior-preservation constraints.
- [ ] Commit the specification baseline before implementation.

### Task 2: Specify the React information architecture with failing tests

**Files:**
- Modify: `client/src/pages/StationaryAttackPage.test.tsx`

- [ ] Add a test asserting `角色状态` precedes `运行状态` in the top status grid and that the removed `输入安全边界` text is absent.
- [ ] Add a test asserting map recording and map catalog controls are absent on the default tab and appear after selecting `地图管理`.
- [ ] Add tests asserting `攻击时长分段` and `移动与随机休息` content is hidden by default and preserved after expand/collapse.
- [ ] Add a test asserting a Host error is rendered inside the compact runtime message region.
- [ ] Add a test asserting `运行日志` opens a dialog and sends only `{ command: 'loadSessionLog' }`.
- [ ] Run `npm test -- --run src/pages/StationaryAttackPage.test.tsx` from `client` and confirm the new tests fail for missing UI behavior.

### Task 3: Add bounded read-only session log loading

**Files:**
- Create: `src/Maple.Host/Diagnostics/SessionLogReader.cs`
- Create: `tests/Maple.Host.Tests/Diagnostics/SessionLogReaderTests.cs`
- Modify: `src/Maple.WindowsHost/MainWindow.xaml.cs`
- Modify: `client/src/bridge/bridge.ts`
- Create: `client/src/bridge/sessionLogTypes.ts`

- [ ] Write xUnit tests proving the reader returns the newest 200 valid JSONL rows in chronological order and skips malformed rows.
- [ ] Run `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter SessionLogReaderTests` and confirm failure because the reader does not exist.
- [ ] Implement a reverse bounded reader over the fixed LocalAppData session log and expose `loadSessionLog` as a read-only Bridge command returning `session.log.loaded`.
- [ ] Run the focused .NET tests and confirm they pass.

### Task 4: Build focused visual components

**Files:**
- Create: `client/src/components/CharacterStatusPanel.tsx`
- Create: `client/src/components/RuntimeLogModal.tsx`
- Create: `client/src/components/MapManagementPanel.tsx`
- Modify: `client/src/components/SessionStatusPanel.tsx`
- Modify: `client/src/components/AttackBandsEditor.tsx`
- Modify: `client/src/components/AdvancedParametersCollapse.tsx`

- [ ] Make character recognition and visual safety siblings inside one top-left status surface.
- [ ] Reduce the runtime panel to session/countdown/input details plus compact notices supplied by the page.
- [ ] Wrap attack bands and movement/rest fields in closed-by-default Ant Design Collapse sections without changing Form item names.
- [ ] Render map recording, catalog controls, and navigation status inside the map-management workspace.
- [ ] Render the latest structured logs in an Ant Design Modal table with loading, empty, error, and refresh states.

### Task 5: Recompose the page and styles

**Files:**
- Modify: `client/src/pages/StationaryAttackPage.tsx`
- Modify: `client/src/styles/app.css`

- [ ] Replace the mixed header with a compact product bar, two primary tabs, stable utility actions, and page-specific primary commands.
- [ ] Place character and runtime status in a two-column top grid, then render the compact stationary configuration below.
- [ ] Move all map commands and controls to the map tab and keep tab switching side-effect free.
- [ ] Show save success through Ant Design message feedback while preserving Host failure handling.
- [ ] Implement the restrained workbench tokens, 8px-or-less surfaces, stable control dimensions, responsive two-column collapse, and internal expanded-configuration scrolling.
- [ ] Run the focused React suite until all page tests pass.

### Task 6: Verify and package

**Files:**
- Modify only if verification exposes a regression.

- [ ] Run `npm test -- --run`, `npm run lint`, and `npm run build` from `client`.
- [ ] Run the complete .NET test solution and confirm zero failures.
- [ ] Build the Windows x64 publish artifact using the repository release script or established publish command.
- [ ] Launch the built client, capture the default stationary tab, expanded configuration, map tab, compact error, and log modal at `1180x760`; verify no overlap, clipped text, blank content, or accidental page-level scrollbar in the default state.
- [ ] Review the final diff against every acceptance item, commit on `master`, push, and report the absolute EXE path.
