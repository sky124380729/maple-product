# Broker Lease Deadline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Broker enforce every physical key lease independently of Host scheduling and stop the stationary session when a lease deadline is missed.

**Architecture:** Add a single high-priority monotonic deadline worker owned by each Broker input session. Active keys carry a generation and deadline; expiry callbacks serialize through the session lock, release once, and retain an acknowledgement result for the Host's idempotent KeyUp.

**Tech Stack:** .NET 8, C#, xUnit, Windows `keybd_event` Broker, monotonic clocks.

---

### Task 1: Deadline scheduler contract

**Files:**
- Modify: `src/Maple.InputBroker/BrokerAbstractions.cs`
- Create: `src/Maple.InputBroker/BrokerLeaseDeadlineScheduler.cs`
- Test: `tests/Maple.InputBroker.Tests/Broker/BrokerLeaseDeadlineSchedulerTests.cs`

- [x] **Step 1: Write the failing scheduler test**

Create a manual-clock test proving a scheduled callback does not run before its deadline, runs after the clock reaches the deadline, and a cancelled generation never runs.

- [x] **Step 2: Run the scheduler test and verify RED**

Run: `dotnet test tests/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj --filter FullyQualifiedName~BrokerLeaseDeadlineSchedulerTests`

Expected: compilation failure because the scheduler contract and implementation do not exist.

- [x] **Step 3: Implement the scheduler**

Define schedule/cancel/cancel-all operations keyed by `BrokerLogicalAction` and generation. Implement a background thread that waits for the earliest monotonic deadline, wakes when the schedule changes, and invokes callbacks outside its internal lock.

- [x] **Step 4: Run the scheduler tests and verify GREEN**

Run the Task 1 command. Expected: all scheduler tests pass.

### Task 2: Broker session lease ownership

**Files:**
- Modify: `src/Maple.InputBroker/BrokerInputSession.cs`
- Modify: `src/Maple.InputBroker/NamedPipeBrokerServer.cs`
- Test: `tests/Maple.InputBroker.Tests/Broker/BrokerInputSessionTests.cs`

- [x] **Step 1: Write failing session tests**

Add tests proving KeyDown registers `downTime + leaseMs`, expiry releases without watchdog, Host KeyUp acknowledges `KEY_LEASE_EXPIRED`, stale generations do nothing, and explicit KeyUp/ReleaseAll cancel deadlines.

- [x] **Step 2: Write failing late/failure tests**

Add tests proving expiry after the deadline returns `KEY_LEASE_DEADLINE_MISSED`, physical KeyUp failure returns `KEY_LEASE_RELEASE_FAILED`, and both responses are rejected.

- [x] **Step 3: Run tests and verify RED**

Run: `dotnet test tests/Maple.InputBroker.Tests/Maple.InputBroker.Tests.csproj --filter FullyQualifiedName~BrokerInputSessionTests`

Expected: new expiry result assertions fail against the watchdog-only implementation.

- [x] **Step 4: Implement session integration**

Store key, physical-down time, deadline, lease duration, and generation per active action. Serialize explicit and automatic release under the existing session lock, retain the automatic completion until Host KeyUp, and cancel deadlines on every safety cleanup path.

- [x] **Step 5: Run Broker tests and verify GREEN**

Run the Task 2 command. Expected: all Broker session tests pass.

### Task 3: Host fail-closed verification

**Files:**
- Modify: `tests/Maple.Host.Tests/Stationary/StationarySessionControllerTests.cs`

- [x] **Step 1: Add failing controller test**

Make the first movement KeyUp return `KEY_LEASE_DEADLINE_MISSED` and assert the event sequence ends with `ReleaseAll` before MoveGap or MoveSecond.

- [x] **Step 2: Run and verify behavior**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~StationarySessionControllerTests`

Expected: the existing controller already treats rejected KeyUp as a stop; if the test passes immediately, retain it as explicit contract coverage and do not add production Host code.

### Task 4: Full verification and publish

**Files:**
- Modify only if verification exposes a scoped defect.

- [x] **Step 1: Run all .NET tests**

Run: `dotnet test MapleProduct.sln --no-restore`

- [x] **Step 2: Run frontend tests and build**

Run: `npm test -- --run` and `npm run build` in `client`.

- [x] **Step 3: Publish Windows x64**

Run: `powershell -ExecutionPolicy Bypass -File scripts/publish-windows.ps1`.

- [x] **Step 4: Inspect repository state**

Run: `git diff --check` and `git status --short --branch`. Preserve unrelated user changes and report any remaining real-game verification gap.
