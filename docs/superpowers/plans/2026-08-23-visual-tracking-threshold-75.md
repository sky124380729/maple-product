# Visual Character Tracking Threshold 75% Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lower only the character appearance tracking and recovery score threshold from 82% to 75% and deliver a verified Windows x64 package.

**Architecture:** Keep the existing acquisition and local-tracking state machine intact. Publish one production threshold constant from `VisualStationaryObservationSession`, use it when constructing the character stabilizer, and lock the value and recovery behavior with focused tests.

**Tech Stack:** C# 12, .NET 8, xUnit, PowerShell Windows x64 publish pipeline.

---

### Task 1: Update Authoritative Requirements

**Files:**
- Modify: `docs/PRODUCT_SPEC.md`
- Modify: `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`
- Modify: `docs/PHASE_1_ACCEPTANCE.md`

- [ ] **Step 1: Change only the character tracking/recovery threshold**

Replace character-mode `0.82` references with `0.75`. Retain initial acquisition `0.88`, initial margin `0.06`, tracking margin `0.04`, three-frame recovery, and all name-template values.

- [ ] **Step 2: Verify document formatting**

Run:

```powershell
git diff --check -- docs/PRODUCT_SPEC.md docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md docs/PHASE_1_ACCEPTANCE.md
```

Expected: exit 0; line-ending notices are allowed.

### Task 2: Lock And Implement The Threshold With TDD

**Files:**
- Modify: `tests/Maple.Host.Tests/Stationary/VisualStationaryObservationSessionTests.cs`
- Modify: `tests/Maple.Host.Tests/Stationary/SelfIdentityStabilizerTests.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationaryObservationSession.cs`

- [ ] **Step 1: Write failing tests**

Add a test asserting `VisualStationaryObservationSession.CharacterTrackingScoreThreshold` is `0.75`. Update the character stabilizer test helper to use that constant, assert an established character track accepts exactly `0.75`, and assert `0.74` revokes trust while three subsequent `0.75` frames are required to recover.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj -c Release --filter "FullyQualifiedName~SelfIdentityStabilizerTests|FullyQualifiedName~VisualStationaryObservationSessionTests"
```

Expected: compile failure because `CharacterTrackingScoreThreshold` does not exist.

- [ ] **Step 3: Implement the minimal production change**

Add:

```csharp
public const double CharacterTrackingScoreThreshold = 0.75;
```

Use it only for the character session's `minimumTrackingScore`. Do not change acquisition, margin, frame count, radius, name mode, movement, or randomization.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 2 command. Expected: zero failures.

### Task 3: Verify And Publish

**Files:**
- Modify: `docs/phase-1/evidence/visual-safe-stationary-mode.md`
- Generate: `artifacts/phase-1/win-x64-visual-character-track-75/`

- [ ] **Step 1: Run complete Release verification**

```powershell
dotnet build MapleProduct.sln -c Release --no-restore
dotnet test MapleProduct.sln -c Release --no-build --no-restore
npm --prefix client test -- --run
npm --prefix client run lint
npm --prefix client run build
git diff --check
```

Expected: all tests and builds pass; only documented existing frontend warnings may remain.

- [ ] **Step 2: Publish to a new validated directory**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-windows.ps1 -Configuration Release -OutputRoot artifacts\phase-1\win-x64-visual-character-track-75
```

- [ ] **Step 3: Validate package and record hashes**

Read every ZIP entry; require `Maple.WindowsHost.exe`, `Maple.InputBroker.exe`, `Maple.Host.dll`, and `client/index.html`; compare packaged `Maple.Host.dll` against the fresh Release hash; record SHA-256 values in the evidence file.

- [ ] **Step 4: Deliver the executable path**

Report:

```text
C:\Users\Levi\Desktop\maple-product\maple-product\artifacts\phase-1\win-x64-visual-character-track-75\MapleProduct\Maple.WindowsHost.exe
```
