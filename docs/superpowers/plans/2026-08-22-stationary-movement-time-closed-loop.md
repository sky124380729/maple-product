# Stationary Movement Time Closed Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bound stationary movement by Broker-measured direction-key hold durations instead of requested durations, with a 10ms release guard.

**Architecture:** A Broker-only movement lease scheduler releases left/right keys at their requested monotonic deadline. Broker responses carry measured hold duration and release lateness. The Host plans and commits one movement segment at a time, so the second segment is constrained by the actual first-segment result.

**Tech Stack:** .NET 8, C#, xUnit, named-pipe Broker protocol, Windows `keybd_event` adapter.

---

### Task 1: Add Broker Timing Contract

**Files:**
- Modify: `src/Maple.Core/Broker/BrokerProtocol.cs`
- Modify: `src/Maple.Host/Stationary/StationaryContracts.cs`
- Modify: `src/Maple.Host/Broker/NamedPipeBrokerClient.cs`
- Test: `tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs`

- [ ] **Step 1: Write a failing contract assertion**

Add a controller-test action result with `ActualHoldMs = 46` and `ReleaseLatenessMs = 6`; assert the controller can read both values. Compilation must fail because the result fields do not exist.

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter FullyQualifiedName~StationarySessionControllerTests`

Expected: compile failure for missing timing result fields.

- [ ] **Step 3: Add timing fields**

Extend the records without breaking non-movement callers:

```csharp
public sealed record BrokerResponse(
    int ProtocolVersion,
    long Sequence,
    bool Accepted,
    string Code,
    int? ActualHoldMs = null,
    int? ReleaseLatenessMs = null);

public sealed record InputActionResult(
    bool Success,
    string Code,
    int? ActualHoldMs = null,
    int? ReleaseLatenessMs = null);
```

Map the optional Broker response values in `NamedPipeBrokerClient` and increment `BrokerProtocol.Version` because the wire response changed.

- [ ] **Step 4: Run the focused build/test**

Run the command from Step 2. Expected: compilation succeeds; any behavioral assertion remains RED until Tasks 4-5.

### Task 2: Restore a Broker-Only Movement Deadline Scheduler

**Files:**
- Modify: `src/Maple.InputBroker/BrokerAbstractions.cs`
- Create: `src/Maple.InputBroker/BrokerMovementLeaseScheduler.cs`
- Test: `tests/Maple.InputBroker.Tests/Broker/BrokerMovementLeaseSchedulerTests.cs`

- [ ] **Step 1: Write scheduler tests**

Cover scheduling at a monotonic deadline, replacing an action with a new generation, cancelling one action, and cancelling all actions. Use a manual clock/event so tests do not sleep.

- [ ] **Step 2: Run scheduler tests and verify RED**

Run: `dotnet test tests/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj --no-restore --filter FullyQualifiedName~BrokerMovementLeaseSchedulerTests`

Expected: compile failure because scheduler types do not exist.

- [ ] **Step 3: Implement the scheduler**

Add `IMovementLeaseScheduler` with `Schedule`, `Cancel`, and `CancelAll`. Implement one highest-priority background thread that waits for the earliest monotonic deadline, wakes roughly 1ms early, short-spins to the deadline, removes the matching generation, then invokes the callback outside its lock.

- [ ] **Step 4: Run scheduler tests and verify GREEN**

Run the command from Step 2. Expected: all scheduler tests pass without timing sleeps.

### Task 3: Measure Physical Hold Time in Broker

**Files:**
- Modify: `src/Maple.InputBroker/BrokerInputSession.cs`
- Modify: `src/Maple.InputBroker/NamedPipeBrokerServer.cs`
- Test: `tests/Maple.InputBroker.Tests/Broker/BrokerInputSessionTests.cs`

- [ ] **Step 1: Write failing Broker tests**

Add a manual movement scheduler and fake clock. Assert:

```csharp
// requested 40ms, released at 46ms
Assert.Equal(46, keyUp.ActualHoldMs);
Assert.Equal(6, keyUp.ReleaseLatenessMs);
Assert.Equal("KEY_ALREADY_UP", keyUp.Code);
```

Also assert attack KeyDown never registers with the movement scheduler, explicit movement KeyUp cancels its deadline, stale generations cannot release a new key, and automatic release keeps the target armed.

- [ ] **Step 2: Run Broker session tests and verify RED**

Run: `dotnet test tests/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj --no-restore --filter FullyQualifiedName~BrokerInputSessionTests`

Expected: timing assertions fail or do not compile.

- [ ] **Step 3: Implement measured movement leases**

Inject `IMovementLeaseScheduler`. Record a conservative `PressedAtMonoMs` immediately before physical Down. For directions only, schedule the requested deadline. On explicit or automatic successful Up, record the post-send monotonic time and retain a completion:

```csharp
int actual = checked((int)Math.Max(0, releasedAt - pressedAt));
int lateness = Math.Max(0, actual - requestedLeaseMs);
```

Return the completion on the Host's `KeyUp`; an already auto-released move is accepted as `KEY_ALREADY_UP`. `ReleaseAll/Close` cancel deadlines and clear completions. Wire the production scheduler in `NamedPipeBrokerServer`.

- [ ] **Step 4: Run Broker tests and verify GREEN**

Run all `Maple.InputBroker.Tests`. Expected: all pass.

### Task 4: Plan and Commit Movement One Segment at a Time

**Files:**
- Modify: `src/Maple.Core/Movement/StationaryMovementPlanner.cs`
- Test: `tests/Maple.Core.Tests/Movement/StationaryMovementPlannerTests.cs`

- [ ] **Step 1: Replace the old whole-plan regression with failing tests**

Tests must assert:

```csharp
Assert.Equal(StationaryMovementPlanner.ReleaseSafetyMarginMs, 10);
// At offset -20, a requested left 40ms that actually held 46ms produces -66.
planner.ApplyCompletedSegment(MovementDirection.Left, 46);
Assert.Equal(-66, planner.RelativeOffsetMs);
```

Add a boundary test proving sampling subtracts the 10ms guard, and a multi-cycle test applying actual durations that never exceeds `[-max,+max]` when lateness is at most 10ms.

- [ ] **Step 2: Run planner tests and verify RED**

Run: `dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj --no-restore --filter FullyQualifiedName~StationaryMovementPlannerTests`

Expected: compile failure for missing segment APIs or assertion failure from planned-duration accounting.

- [ ] **Step 3: Implement segment APIs**

Expose `CreateFirstSegment`, `CreateSecondSegment`, `SampleGapMs`, `SampleStabilizeMs`, and `ApplyCompletedSegment`. Use:

```csharp
int maximum = Math.Min(
    config.MoveHoldMaxMs,
    RemainingBudget(direction, RelativeOffsetMs, config.MaxLateralMoveMs)
        - ReleaseSafetyMarginMs);
```

Throw the existing stable budget-exhausted codes when `maximum < MoveHoldMinMs`. Update offset only from `actualHoldMs`.

- [ ] **Step 4: Run planner tests and verify GREEN**

Run the command from Step 2. Expected: all planner tests pass.

### Task 5: Make the Controller Use Broker-Measured Durations

**Files:**
- Modify: `src/Maple.Host/Stationary/StationarySessionController.cs`
- Test: `tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs`

- [ ] **Step 1: Write failing controller tests**

Use an action sink that returns requested movement lease plus configured lateness. Verify the second requested lease is constrained using the first actual duration. Verify missing/zero actual duration stops with `MOVEMENT_TIMING_INVALID`, and lateness above 10ms stops with `MOVEMENT_RELEASE_LATE`; both end with `ReleaseAll` and never enter the next attack cycle.

- [ ] **Step 2: Run controller tests and verify RED**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --no-restore --filter FullyQualifiedName~StationarySessionControllerTests`

Expected: new assertions fail because the controller still creates the full plan and applies requested values.

- [ ] **Step 3: Implement sequential movement execution**

Make `HoldAsync` return the successful Up result. For movement, validate `ActualHoldMs > 0`, `ReleaseLatenessMs >= 0`, and lateness `<= 10`; apply the actual duration immediately. Only then sample/wait the gap and create the second segment from the updated offset. Preserve attack behavior and all existing release paths.

- [ ] **Step 4: Run Host tests and verify GREEN**

Run all `Maple.Host.Tests`. Expected: all pass.

### Task 6: Log Timing Evidence and Verify the Repository

**Files:**
- Modify: `src/Maple.Host/Diagnostics/LoggingActionSink.cs`
- Test: relevant diagnostics tests if present

- [ ] **Step 1: Add a failing log assertion**

Assert movement logs contain `requestedMs`, `actualMs`, and `latenessMs` while attack logs remain valid.

- [ ] **Step 2: Run the diagnostics test and verify RED**

Run the relevant Host diagnostics test filter. Expected: timing fields absent.

- [ ] **Step 3: Add structured timing details**

Include the requested lease on movement Down and actual/lateness values on movement Up without changing React or sending input outside the Broker.

- [ ] **Step 4: Run all verification**

Run:

```powershell
dotnet test --no-restore
npm test -- --run
npm run build
```

Run the npm commands from `client`. Expected: all tests and builds pass with no new warnings.
