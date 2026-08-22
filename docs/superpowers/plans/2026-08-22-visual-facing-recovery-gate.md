# Visual Facing Recovery Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent visual-safe stationary attack from starting another attack after only the first direction executed, while adding conservative `0.90` acquisition / `0.86` local tracking confidence hysteresis.

**Architecture:** Keep identity hysteresis inside `SelfIdentityStabilizer` and keep facing recovery inside `VisualStationarySessionController`. A movement attempt returns whether a direction key was actually sent; after the first opposite-direction movement succeeds, the controller condition-waits behind the existing visual safety gate until the initial-facing direction succeeds, so no attack can run with dirty facing.

**Tech Stack:** C# 12, .NET 8, xUnit, WPF Windows x64 packaging, existing broker + `keybd_event` input path.

---

### Task 1: Update Authoritative Product Requirements

**Files:**
- Modify: `docs/PRODUCT_SPEC.md`
- Modify: `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`
- Modify: `docs/PHASE_1_ACCEPTANCE.md`

- [ ] **Step 1: Add the facing invariant to the product spec**

Specify that a successful first opposite-direction Down/Up creates `FacingRestorePending`, and that no later attack Down, rest, or next-cycle sampling is allowed until the initial-facing Down/Up succeeds through the visual safety gate.

- [ ] **Step 2: Add confidence hysteresis to the design**

Document acquisition/reacquisition after a full reset at `0.90`, local tracking within horizontal and vertical `12px` at `0.86`, unchanged `0.06` initial peak margin, and unchanged three-new-frame stabilization after transient loss.

- [ ] **Step 3: Add acceptance cases**

Add explicit checks for `Attack -> opposite -> initialFacing -> Attack`, indefinite attack pause while recovery remains unsafe, `0.89` initial rejection, `0.86` local acceptance, and `0.86` far-candidate rejection.

- [ ] **Step 4: Check documentation consistency**

Run: `git diff --check -- docs/PRODUCT_SPEC.md docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md docs/PHASE_1_ACCEPTANCE.md`

Expected: exit 0 with no whitespace errors.

### Task 2: Add Identity Confidence Hysteresis with TDD

**Files:**
- Modify: `tests/Maple.Host.Tests/Stationary/SelfIdentityStabilizerTests.cs`
- Modify: `src/Maple.Host/Stationary/SelfIdentityStabilizer.cs`

- [ ] **Step 1: Write failing acquisition and tracking tests**

Add tests equivalent to:

```csharp
[Fact]
public void Initial_acquisition_rejects_a_candidate_below_point_nine()
{
    var stabilizer = new SelfIdentityStabilizer();
    Assert.All(new[]
    {
        stabilizer.Update(Match(1, 100, best: 0.89)),
        stabilizer.Update(Match(2, 100, best: 0.89)),
        stabilizer.Update(Match(3, 100, best: 0.89))
    }, result => Assert.NotEqual(SelfIdentityStatus.Trusted, result.Status));
}

[Fact]
public void Established_local_track_accepts_point_eight_six_but_rejects_the_same_score_outside_twelve_pixels()
{
    var stabilizer = TrustedAt(100);
    Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(4, 106, best: 0.86)).Status);
    Assert.Equal(SelfIdentityStatus.Untrusted, stabilizer.Update(Match(5, 125, best: 0.86)).Status);
}
```

- [ ] **Step 2: Run the new tests and verify RED**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~SelfIdentityStabilizerTests" --no-restore`

Expected: the `0.86` local tracking test fails because current code uses `0.90` for both acquisition and tracking.

- [ ] **Step 3: Implement separate thresholds**

Change the constructor and candidate filters to:

```csharp
public sealed class SelfIdentityStabilizer(
    double minimumAcquisitionScore = 0.90,
    double minimumTrackingScore = 0.86,
    double minimumPeakMargin = 0.06,
    int requiredFrames = 3,
    double maximumJumpPx = 12)
```

Use `minimumAcquisitionScore` only when no previous accepted position exists. For an established track, evaluate each best/second candidate independently with `minimumTrackingScore` plus the existing two-axis `maximumJumpPx` check. Update failure codes using the same state-specific threshold.

- [ ] **Step 4: Run stabilizer tests and verify GREEN**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~SelfIdentityStabilizerTests" --no-restore`

Expected: all stabilizer tests pass.

### Task 3: Add the Facing Recovery Gate with TDD

**Files:**
- Modify: `tests/Maple.Host.Tests/Stationary/VisualStationarySessionControllerTests.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationarySessionController.cs`

- [ ] **Step 1: Extend the test observation fake deterministically**

Add authorization-read and wait-call controls so a test can cancel only the second direction authorization and return a timeout only for that retry, without changing production interfaces:

```csharp
public HashSet<int> CancelAuthorizationReads { get; } = [];
public HashSet<int> TimeoutWaitCalls { get; } = [];
public Action<int>? WaitStarted { get; set; }
```

The fake must call `cancellationToken.ThrowIfCancellationRequested()` after `WaitStarted` and must not consume a queued observation for a configured timeout.

- [ ] **Step 2: Write the failing recovery-order test**

Create a two-cycle test where the first left movement succeeds, the initial right attempt times out before KeyDown, recovery later succeeds, and cancellation begins on the second attack. Assert exact directional order:

```csharp
Assert.Equal(
    ["Down:Attack", "Up:Attack", "Down:MoveLeft", "Up:MoveLeft",
     "Down:MoveRight", "Up:MoveRight", "Down:Attack", "Up:Attack", "ReleaseAll"],
    actions.Events);
```

- [ ] **Step 3: Write the failing persistent-untrusted test**

After first movement, cancel every later movement authorization and cancel the session from the third trusted-wait callback. Assert exactly one attack Down and no recovery direction Down; the current implementation fails by starting cycle two.

- [ ] **Step 4: Run controller tests and verify RED**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationarySessionControllerTests" --no-restore`

Expected: both new tests fail because `TryMoveAsync` currently returns `Task` and the run loop enters the next attack after a missed second segment.

- [ ] **Step 5: Return movement execution status**

Change `TryMoveAsync` to `Task<bool>`. Return `false` for null/stale/blocked/pre-KeyDown timeout paths and `true` only after direction Down and Up produce a valid `MovementHoldResult`. Existing stabilization and feedback behavior remains unchanged.

- [ ] **Step 6: Add condition-based initial-facing recovery**

After `firstExecuted == true`, require the initial-facing movement to succeed. If the normal second attempt returns false, call a helper with this shape:

```csharp
private async Task RestoreInitialFacingAsync(
    Guid sessionId,
    long cycleId,
    MovementDirection initialFacing,
    int sampledAttackDurationMs,
    StationaryAttackConfig config,
    CancellationToken cancellationToken)
```

The helper publishes `VISUAL_FACING_RESTORE_PENDING`, checks the normal window/Broker safety gate, waits for a sequence-new trusted observation in `750ms` chunks, and retries `TryMoveAsync` with `MoveSecond`. It has no total timeout and returns only after the initial-facing Down/Up succeeds. It never bypasses `Untrusted`, `Outside`, or outward guard decisions.

- [ ] **Step 7: Run controller tests and verify GREEN**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationarySessionControllerTests" --no-restore`

Expected: all controller tests pass, including exact ordering and cancellation while recovery is pending.

### Task 4: Regression Verification and Windows Package

**Files:**
- Verify: `MapleProduct.sln`
- Verify: `client/`
- Create: `artifacts/phase-1/win-x64-visual-facing-recovery/`

- [ ] **Step 1: Run all visual Host tests**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationary|FullyQualifiedName~SelfIdentity" --no-restore`

Expected: zero failures.

- [ ] **Step 2: Run full Release tests**

Run: `dotnet test MapleProduct.sln -c Release --no-restore`

Expected: zero failures across Core, Host, and InputBroker tests.

- [ ] **Step 3: Run frontend tests and lint**

Run: `npm --prefix client test -- --run`

Expected: all tests pass.

Run: `npm --prefix client run lint`

Expected: zero errors; pre-existing warnings may remain documented.

- [ ] **Step 4: Publish a new Windows x64 directory**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-windows.ps1 `
  -OutputRoot artifacts\phase-1\win-x64-visual-facing-recovery
```

Expected: `Maple.WindowsHost.exe` and `MapleProduct-phase-1-win-x64.zip` are created under the new output root.

- [ ] **Step 5: Hash and report the package**

Run `Get-FileHash -Algorithm SHA256` for the EXE, `Maple.Host.dll`, and ZIP. Report the absolute latest EXE path and remind the user to exit old app instances before testing.
