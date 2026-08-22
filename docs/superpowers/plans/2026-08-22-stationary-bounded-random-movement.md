# Stationary Bounded Random Movement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace planned-duration stationary offset accounting with Broker-measured, bounded, mean-reverting random movement and display the signed Host offset in the role information panel.

**Architecture:** `StationaryMovementPlanner` owns the signed session offset and samples one segment at a time from an explicitly enumerated legal millisecond set. `StationarySessionController` commits Broker-measured movement results before planning the next segment, publishes the offset in rhythm state, and emits movement telemetry. React only renders the Host value and preserves the final stopped value; stationary input remains `HostKeyUp` and never enters the navigation deadline scheduler.

**Tech Stack:** .NET 8, C#, xUnit, named-pipe Broker protocol, React 18, TypeScript, Ant Design, Vitest, Testing Library.

---

## File Structure

- Modify `src/Maple.Core/Configuration/StationaryAttackConfig.cs`: define the shared `5,000ms` movement hard limit.
- Modify `src/Maple.Core/Configuration/StationaryConfigValidator.cs`: reject impossible safety-margin budgets and over-limit movement holds.
- Modify `src/Maple.Core/Movement/StationaryMovementPlanner.cs`: implement intent selection, per-segment candidate sampling, actual-duration commits, and cycle checks.
- Modify `src/Maple.Core/Session/StationarySessionState.cs`: add authoritative `RelativeOffsetMs`.
- Modify `src/Maple.Host/Stationary/StationaryContracts.cs`: define movement telemetry and its sink.
- Modify `src/Maple.Host/Stationary/StationarySessionController.cs`: execute sequential movement, validate timing, publish offsets, and report telemetry.
- Modify `src/Maple.Host/Diagnostics/SessionDiagnostics.cs`: add structured movement fields to JSONL records.
- Create `src/Maple.Host/Diagnostics/SessionLogMovementTelemetrySink.cs`: map telemetry to session logs.
- Modify `src/Maple.WindowsHost/MainWindow.xaml.cs`: wire the telemetry sink.
- Modify `client/src/bridge/types.ts`: add `relativeOffsetMs` to the bridge contract.
- Modify `client/src/state/sessionReducer.ts`: retain final stopped offset and reset it on a new start.
- Modify `client/src/pages/StationaryAttackPage.tsx`: pass stopped state payloads through the reducer.
- Modify `client/src/components/SessionStatusPanel.tsx` and `RecognitionStatus.tsx`: display signed offset independently of recognition.

### Task 1: Validate the Movement Safety Configuration

**Files:**
- Modify: `src/Maple.Core/Configuration/StationaryAttackConfig.cs`
- Modify: `src/Maple.Core/Configuration/StationaryConfigValidator.cs`
- Modify: `tests/Maple.Core.Tests/Configuration/StationaryConfigValidatorTests.cs`
- Modify: `client/src/bridge/configValidation.ts`
- Modify: `client/src/bridge/configValidation.test.ts`

- [ ] **Step 1: Write failing Core validation tests**

```csharp
[Fact]
public void Rejects_move_hold_above_broker_limit()
{
    var config = StationaryAttackConfig.Default with { MoveHoldMaxMs = 5_001 };
    Assert.Contains(StationaryConfigValidator.Validate(config).Errors,
        error => error.Field == "moveHold" && error.Code == "MOVE_HOLD_LIMIT");
}

[Fact]
public void Rejects_budget_smaller_than_minimum_hold_plus_release_margin()
{
    var config = StationaryAttackConfig.Default with
    {
        MoveHoldMinMs = 30,
        MaxLateralMoveMs = 49
    };
    Assert.Contains(StationaryConfigValidator.Validate(config).Errors,
        error => error.Field == "maxLateralMoveMs" && error.Code == "MOVE_BUDGET_TOO_SMALL");
}
```

- [ ] **Step 2: Run Core tests and verify RED**

Run `dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj --no-restore --filter FullyQualifiedName~StationaryConfigValidatorTests`.

Expected: both new assertions fail because `5_001` and `49` are accepted.

- [ ] **Step 3: Implement the Core rules**

Add `StationaryAttackConfig.MovementDurationLimitMs = 5_000`. In the validator add `MOVE_HOLD_LIMIT`, and replace the budget check with:

```csharp
if (config.MaxLateralMoveMs < config.MoveHoldMinMs + StationaryMovementPlanner.ReleaseSafetyMarginMs)
    Add("maxLateralMoveMs", "MOVE_BUDGET_TOO_SMALL");
```

- [ ] **Step 4: Add matching failing React validation tests**

Test the same `5_001` and `49` inputs against `validateStationaryConfig` and assert the Core-compatible field/code pairs.

- [ ] **Step 5: Run React validation tests and verify RED**

Run `npm test -- --run src/bridge/configValidation.test.ts` from `client`. Expected: both new cases fail because the TypeScript validator accepts them.

- [ ] **Step 6: Implement the TypeScript rules**

Add `movementDurationLimitMs = 5_000` and `releaseSafetyMarginMs = 20`, then return `MOVE_HOLD_LIMIT` and `MOVE_BUDGET_TOO_SMALL` through the existing `ConfigValidationError` shape.

- [ ] **Step 7: Verify validation GREEN**

Rerun both focused Core and React commands. Expected: all validation tests pass.

- [ ] **Step 8: Commit the validation change**

Run `git add src/Maple.Core/Configuration/StationaryAttackConfig.cs src/Maple.Core/Configuration/StationaryConfigValidator.cs tests/Maple.Core.Tests/Configuration/StationaryConfigValidatorTests.cs client/src/bridge/configValidation.ts client/src/bridge/configValidation.test.ts` and `git commit -m "fix: validate stationary movement safety budget"`.

### Task 2: Build the Per-Segment Bounded Random Planner

**Files:**
- Modify: `src/Maple.Core/Movement/StationaryMovementPlanner.cs`
- Modify: `tests/Maple.Core.Tests/Movement/StationaryMovementPlannerTests.cs`

- [ ] **Step 1: Replace whole-plan tests with failing segment API tests**

Define the desired API through tests:

```csharp
MovementCycle cycle = planner.BeginCycle(config);
MovementSegment first = planner.CreateFirstSegment(config, cycle);
planner.ApplyCompletedSegment(first.Direction, actualHoldMs: 46, config.MaxLateralMoveMs);
MovementSegment second = planner.CreateSecondSegment(config, cycle);
```

Add separate assertions for `ReleaseSafetyMarginMs == 20`, actual `46ms` producing offset `-46`, the second budget using `-46`, exact 40/70 percent intent boundaries, the 75 percent return roll, strict return progress, next-cycle reserve, candidate variability, `MOVEMENT_RETURN_UNSATISFIED`, and stable first/second budget exhaustion codes. Add the `10_000`-cycle deterministic regression now, before implementation; it applies actual holds between requested and requested plus `20ms`, checks every executed segment against the boundary, and requires at least 20 distinct `(first, second, finalOffset)` tuples.

- [ ] **Step 2: Run planner tests and verify RED**

Run `dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj --no-restore --filter FullyQualifiedName~StationaryMovementPlannerTests`.

Expected: compilation fails for missing `MovementCycle`, `MovementIntent`, and segment methods.

- [ ] **Step 3: Implement intent selection and legal candidate enumeration**

Replace `MovementPlan` with:

```csharp
public enum MovementIntent { Unbiased, ReturnTowardCenter }
public sealed record MovementCycle(int StartOffsetMs, MovementIntent Intent);
public sealed record MovementSegment(MovementDirection Direction, int HoldMs);
```

Expose:

```csharp
public const int ReleaseSafetyMarginMs = 20;
public MovementCycle BeginCycle(StationaryAttackConfig config);
public MovementSegment CreateFirstSegment(StationaryAttackConfig config, MovementCycle cycle);
public MovementSegment CreateSecondSegment(StationaryAttackConfig config, MovementCycle cycle);
public void ApplyCompletedSegment(MovementDirection direction, int actualHoldMs, int maximumOffsetMs);
public void CompleteCycle(StationaryAttackConfig config, MovementCycle cycle);
public int SampleGapMs(StationaryAttackConfig config);
public int SampleStabilizeMs(StationaryAttackConfig config);
```

Enumerate every hold from `MoveHoldMinMs` through the smaller of `MoveHoldMaxMs` and `remainingBudget - 20`. Filter first holds by existence of a legal planned second hold. Filter second holds by intent and next-cycle reserve. Select with `random.NextInclusive(0, candidates.Count - 1)` so every surviving millisecond remains random.

- [ ] **Step 4: Verify planner GREEN**

Rerun the focused planner command. Expected: all segment, intent, boundary, variability, and long-cycle tests pass.

- [ ] **Step 5: Commit the planner change**

Run `git add src/Maple.Core/Movement/StationaryMovementPlanner.cs tests/Maple.Core.Tests/Movement/StationaryMovementPlannerTests.cs` and `git commit -m "fix: bound stationary random movement"`.

### Task 3: Commit Broker-Measured Durations in the Controller

**Files:**
- Modify: `src/Maple.Core/Session/StationarySessionState.cs`
- Modify: `src/Maple.Host/Stationary/StationaryContracts.cs`
- Modify: `src/Maple.Host/Stationary/StationarySessionController.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs`

- [ ] **Step 1: Write failing controller timing tests**

Queue movement Up results such as:

```csharp
InputActionResult.Ok("KEY_UP_SENT", actualHoldMs: 46, releaseLatenessMs: 6)
```

Assert the following `MoveGap` state contains `RelativeOffsetMs == -46` and the second lease was selected afterward. Add cases for `ActualHoldMs` null, `0`, `5_001`, and negative `ReleaseLatenessMs`; each must stop with `MOVEMENT_TIMING_INVALID`, issue no second movement, and end with `ReleaseAll`. Add an actual boundary crossing that stops with `MOVEMENT_OFFSET_EXCEEDED`.

- [ ] **Step 2: Run controller tests and verify RED**

Run `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter FullyQualifiedName~StationarySessionControllerTests`.

Expected: offset assertions fail because `HoldAsync` discards the Up result and state lacks `RelativeOffsetMs`.

- [ ] **Step 3: Implement sequential execution and state publication**

Append `int RelativeOffsetMs` to `StationaryRhythmState`. Make `HoldAsync` return the Up result. Validate movement timing with:

```csharp
if (result.ActualHoldMs is not >= 1 or > StationaryAttackConfig.MovementDurationLimitMs ||
    result.ReleaseLatenessMs is null or < 0)
    throw new SessionStopException("MOVEMENT_TIMING_INVALID");
```

Execute `BeginCycle -> first -> actual commit -> gap -> second -> actual commit -> CompleteCycle -> stabilize`. At `BeginCycle`, reject a hot-reloaded maximum smaller than the current absolute offset with `MOVEMENT_OFFSET_EXCEEDED`. Publish `movementPlanner.RelativeOffsetMs` in every state including `Stopped`. Keep attack behavior and stationary `HostKeyUp` unchanged.

- [ ] **Step 4: Verify controller GREEN**

Rerun the focused Host command. Expected: strict key order remains, timing failures stop safely, and offsets update after each release.

- [ ] **Step 5: Commit the controller change**

Run `git add src/Maple.Core/Session/StationarySessionState.cs src/Maple.Host/Stationary/StationaryContracts.cs src/Maple.Host/Stationary/StationarySessionController.cs tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs` and `git commit -m "fix: account for measured stationary movement"`.

### Task 4: Add Structured Movement Telemetry

**Files:**
- Modify: `src/Maple.Host/Stationary/StationaryContracts.cs`
- Modify: `src/Maple.Host/Stationary/StationarySessionController.cs`
- Modify: `src/Maple.Host/Diagnostics/SessionDiagnostics.cs`
- Create: `src/Maple.Host/Diagnostics/SessionLogMovementTelemetrySink.cs`
- Modify: `src/Maple.WindowsHost/MainWindow.xaml.cs`
- Modify: `tests/Maple.Host.Tests/Diagnostics/SessionDiagnosticsTests.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs`

- [ ] **Step 1: Write failing telemetry tests**

Define a recording sink and expect:

```csharp
new StationaryMovementTelemetry(
    sessionId, cycleId, MovementDirection.Left, MovementIntent.Unbiased,
    RequestedHoldMs: 40, ActualHoldMs: 46, ReleaseLatenessMs: 6,
    OffsetBeforeMs: 0, OffsetAfterMs: -46, MaxLateralMoveMs: 80)
```

Serialize the record through the JSONL sink and assert each named value is a top-level property.

- [ ] **Step 2: Run focused tests and verify RED**

Run `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter "FullyQualifiedName~SessionDiagnosticsTests|FullyQualifiedName~StationarySessionControllerTests"`.

Expected: compilation fails for missing telemetry types and sink.

- [ ] **Step 3: Implement telemetry and JSONL mapping**

Add `StationaryMovementTelemetry` plus:

```csharp
public interface IStationaryMovementTelemetrySink
{
    Task WriteAsync(StationaryMovementTelemetry telemetry, CancellationToken cancellationToken);
}
```

Extend `SessionLogEntry` with nullable typed movement fields. Implement `SessionLogMovementTelemetrySink` with phase `Movement` and event `segmentCompleted`. Inject it into the controller and wire `new SessionLogMovementTelemetrySink(sessionLog)` in `MainWindow`.

- [ ] **Step 4: Verify telemetry GREEN**

Rerun the focused tests. Expected: one record per successful segment with requested, actual, lateness, direction, intent, and before/after offsets.

- [ ] **Step 5: Commit the telemetry change**

Run `git add src/Maple.Host/Stationary/StationaryContracts.cs src/Maple.Host/Stationary/StationarySessionController.cs src/Maple.Host/Diagnostics/SessionDiagnostics.cs src/Maple.Host/Diagnostics/SessionLogMovementTelemetrySink.cs src/Maple.WindowsHost/MainWindow.xaml.cs tests/Maple.Host.Tests/Diagnostics/SessionDiagnosticsTests.cs tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs` and `git commit -m "feat: log stationary movement telemetry"`.

### Task 5: Display and Retain the Signed Offset

**Files:**
- Modify: `client/src/bridge/types.ts`
- Modify: `client/src/state/sessionReducer.ts`
- Modify: `client/src/pages/StationaryAttackPage.tsx`
- Modify: `client/src/components/SessionStatusPanel.tsx`
- Modify: `client/src/components/RecognitionStatus.tsx`
- Modify: `client/src/pages/StationaryAttackPage.test.tsx`

- [ ] **Step 1: Write failing React behavior tests**

Send rhythm messages with offsets `-23`, `17`, and `0`; assert the role information shows exactly `-23 ms（左）`, `+17 ms（右）`, and `0 ms（中心）` without a recognition snapshot. Send `stationary.stopped` with a final state and assert final offset remains while countdown is zero. Start a new session and assert the old offset disappears before the first new Host state.

- [ ] **Step 2: Run page tests and verify RED**

Run `npm test -- --run src/pages/StationaryAttackPage.test.tsx` from `client`.

Expected: offset text is absent and stopped messages discard state.

- [ ] **Step 3: Implement bridge, reducer, and display**

Add `relativeOffsetMs: number` to the TypeScript state. Change stopped action to:

```typescript
| { type: 'stopped'; reason: string; payload?: StationaryRhythmState }
```

Forward stopped `data.state`, preserve a zero-duration stopped rhythm, and clear it on `starting`. Pass the offset to `RecognitionStatus` and format it with:

```typescript
export function formatRelativeOffset(value: number | null) {
  if (value == null) return '-'
  if (value < 0) return `${value} ms（左）`
  if (value > 0) return `+${value} ms（右）`
  return '0 ms（中心）'
}
```

Render label `计算偏移` independently of recognition health.

- [ ] **Step 4: Verify React GREEN and build**

Run `npm test -- --run src/pages/StationaryAttackPage.test.tsx` and `npm run build`. Expected: tests and build pass.

- [ ] **Step 5: Commit the offset display**

Run `git add client/src/bridge/types.ts client/src/state/sessionReducer.ts client/src/pages/StationaryAttackPage.tsx client/src/components/SessionStatusPanel.tsx client/src/components/RecognitionStatus.tsx client/src/pages/StationaryAttackPage.test.tsx` and `git commit -m "feat: display stationary movement offset"`.

### Task 6: Verify Broker Isolation and the Full Repository

**Files:**
- Verify: `tests/Maple.Host.Tests/Broker/NamedPipeBrokerClientTests.cs`
- Verify: `tests/Maple.InputBroker.Tests/Broker/BrokerInputSessionTests.cs`

- [ ] **Step 1: Confirm stationary release-mode regression coverage**

Tests must prove stationary left/right uses `BrokerMovementReleaseMode.HostKeyUp`, does not register with `IMovementLeaseScheduler`, and explicit `KeyUp` returns `ActualHoldMs` plus `ReleaseLatenessMs`. Navigation must continue using `BrokerDeadline`.

- [ ] **Step 2: Run Broker-focused tests**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter FullyQualifiedName~NamedPipeBrokerClientTests
dotnet test tests/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj --no-restore --filter FullyQualifiedName~BrokerInputSessionTests
```

Expected: all focused Broker tests pass without changing release ownership.

- [ ] **Step 3: Run all automated verification**

Run:

```powershell
dotnet test --no-restore
dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release -r win-x64 --no-restore
dotnet build src/Maple.InputBroker/Maple.InputBroker.csproj -c Release -r win-x64 --no-restore
```

From `client`, run `npm test -- --run` and `npm run build`. Expected: zero failed tests and zero build errors.

- [ ] **Step 4: Perform frontend visual verification**

Start the existing client server on an unused port. Capture desktop and narrow-window screenshots after injecting negative, positive, zero, and stopped states. Verify `计算偏移` stays inside role information, does not overlap HP/MP/EXP, and the longest signed value fits.

- [ ] **Step 5: Record the Windows-only residual verification**

Document that real-game long-run evidence remains required: safe flat platform, no knockback, default movement settings, logs with requested/actual/lateness/offset, and no continuous drift. Automated tests and cross-builds cannot replace this Windows game-client check.
