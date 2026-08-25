# Stationary Return Progress Tolerance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow a return-toward-center cycle to continue when measured release jitter leaves its absolute offset unchanged, while still stopping when the offset worsens.

**Architecture:** Keep the behavior inside `StationaryMovementPlanner.CompleteCycle`, which already owns return-intent completion validation. Change only the equality boundary; hard offset enforcement, next-cycle budget checks, and random segment selection remain unchanged.

**Tech Stack:** C# 12, .NET 8, xUnit, PowerShell release script

---

### Task 1: Lock the Product Contract

**Files:**
- Modify: `docs/PRODUCT_SPEC.md`
- Modify: `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`
- Modify: `docs/PHASE_1_ACCEPTANCE.md`

- [ ] **Step 1: Specify the equality boundary**

Document this truth table: smaller continues, equal continues, larger stops with `MOVEMENT_RETURN_UNSATISFIED`.

- [ ] **Step 2: Check the documents for contradictions**

Run:

```powershell
Select-String -Path docs/PRODUCT_SPEC.md,docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md,docs/PHASE_1_ACCEPTANCE.md -Pattern 'MOVEMENT_RETURN_UNSATISFIED|相等'
```

Expected: all three documents describe equality as non-worsening.

### Task 2: Drive the Boundary Change with Tests

**Files:**
- Modify: `tests/Maple.Core.Tests/Movement/StationaryMovementPlannerTests.cs`
- Modify: `src/Maple.Core/Movement/StationaryMovementPlanner.cs:85`

- [ ] **Step 1: Write the failing equality test**

```csharp
[Fact]
public void Allows_a_completed_return_cycle_that_keeps_absolute_offset_stable()
{
    var planner = new StationaryMovementPlanner(new SequenceRandomSource(1, 10, 0));
    StationaryAttackConfig config = CompactConfig();
    planner.StartSession(MovementDirection.Right, relativeOffsetMs: 60);
    MovementCycle cycle = planner.BeginCycle(config);
    MovementSegment first = planner.CreateFirstSegment(config, cycle);
    planner.ApplyCompletedSegment(first.Direction, actualHoldMs: 50, config.MaxLateralMoveMs);
    MovementSegment second = planner.CreateSecondSegment(config, cycle);
    planner.ApplyCompletedSegment(second.Direction, actualHoldMs: 50, config.MaxLateralMoveMs);

    planner.CompleteCycle(config, cycle);

    Assert.Equal(60, planner.RelativeOffsetMs);
}
```

- [ ] **Step 2: Run the equality test and verify RED**

Run:

```powershell
dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj --filter "FullyQualifiedName~Allows_a_completed_return_cycle_that_keeps_absolute_offset_stable" --no-restore
```

Expected: FAIL with `MOVEMENT_RETURN_UNSATISFIED`.

- [ ] **Step 3: Preserve the worsening test**

Add a second case using `actualHoldMs: 51` for the second segment and assert that `CompleteCycle` throws `MOVEMENT_RETURN_UNSATISFIED`.

- [ ] **Step 4: Implement the minimal comparison change**

```csharp
if (cycle.Intent == MovementIntent.ReturnTowardCenter &&
    Math.Abs((long)RelativeOffsetMs) > Math.Abs((long)cycle.StartOffsetMs))
    throw new InvalidOperationException("MOVEMENT_RETURN_UNSATISFIED");
```

- [ ] **Step 5: Run focused and full planner tests**

Run:

```powershell
dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj --no-restore
```

Expected: all Maple.Core tests pass.

### Task 3: Verify and Publish

**Files:**
- Replace generated output: `artifacts/phase-1/win-x64/`

- [ ] **Step 1: Run all backend and frontend tests**

```powershell
dotnet test MapleProduct.sln --no-restore
npm --prefix client run test -- --run
npm --prefix client run lint
```

Expected: zero test failures and zero lint errors.

- [ ] **Step 2: Build the Windows package**

```powershell
& .\scripts\publish-windows.ps1
```

Expected: fresh `Maple.WindowsHost.exe` and `MapleProduct-phase-1-win-x64.zip`.

- [ ] **Step 3: Validate package identity and ZIP readability**

Confirm required Host, Broker, and client entries exist; read every ZIP entry; verify the packaged assemblies match the fresh Release output; calculate SHA-256.
