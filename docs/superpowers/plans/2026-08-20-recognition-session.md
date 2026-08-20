# Shared Recognition Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with checkpoints.

**Goal:** Add an opt-in, shared Windows capture/recognition session whose immutable snapshots are visible in realtime preview and consumable by stationary attack and future navigation, with HP/MP/EXP as the first safety data.

**Architecture:** Keep capture in the existing `IFrameCaptureSource` boundary and add a Host-owned `RecognitionSession` with reference-counted leases for preview, stationary, and future navigation. Providers produce immutable dynamic/HUD/map observations; the WPF preview renders the latest frame plus recognition overlays and diagnostics, while controllers consume snapshots without sending input directly.

**Tech Stack:** .NET 8, Maple.Host contracts/services, WPF/Windows.Graphics.Capture preview, xUnit, React/WebView2 bridge for configuration and status.

---

### Task 1: Add recognition domain contracts and immutable snapshots

**Files:**
- Create: `src/Maple.Host/Recognition/RecognitionContracts.cs`
- Create: `tests/Maple.Host.Tests/Recognition/RecognitionContractsTests.cs`
- Modify: `src/Maple.Host/Maple.Host.csproj` only if a project reference is required (none expected)

- [ ] **Step 1: Write failing tests for snapshot safety and stale health**

```csharp
[Fact]
public void SnapshotCopiesCollectionsAndMarksOldFramesStale()
{
    var monsters = new List<RecognitionTarget> { new(10, 20, 30, 40, "monster", 0.9) };
    var snapshot = RecognitionSnapshot.Create("s1", 1, 1000, 1100, HudObservation.Empty, monsters, [], [], null);
    monsters.Clear();

    Assert.Single(snapshot.Monsters);
    Assert.Equal(RecognitionHealth.Running, snapshot.Health);
    Assert.Equal(100, snapshot.FrameAgeMs);
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~RecognitionContractsTests --no-restore`

Expected: FAIL because `RecognitionSnapshot`, `HudObservation`, and `RecognitionTarget` do not exist.

- [ ] **Step 3: Implement the contracts**

Define `RecognitionHealth` (`Disabled`, `Starting`, `Running`, `Stale`, `Faulted`, `TargetLost`), `RecognitionTarget`, `SelfObservation`, `HudObservation`, `MapObservation`, `RecognitionSnapshot`, and a `RecognitionSnapshotStore` that publishes immutable copies. Include `SessionId`, `WindowIdentity`, source sequence/timestamps, frame age, confidence values, and a stable fault code. Collections must be copied into read-only arrays in the constructor/factory.

- [ ] **Step 4: Run the focused test and verify it passes**

Run the same command; expected: PASS.

- [ ] **Step 5: Commit the domain contracts**

```powershell
git add src/Maple.Host/Recognition tests/Maple.Host.Tests/Recognition
git commit -m "feat: add recognition snapshot contracts"
```

### Task 2: Implement the shared lease-based recognition session

**Files:**
- Create: `src/Maple.Host/Recognition/RecognitionSession.cs`
- Create: `src/Maple.Host/Recognition/IRecognitionProvider.cs`
- Create: `src/Maple.Host/Recognition/DiagnosticRecognitionProvider.cs`
- Create: `tests/Maple.Host.Tests/Recognition/RecognitionSessionTests.cs`

- [ ] **Step 1: Write failing lifecycle tests**

Cover these exact cases with a fake `IFrameCaptureSource` and provider: two leases call `StartAsync` once; releasing one lease keeps capture alive; releasing the last lease calls `StopAsync` once; duplicate release is harmless; a frame is processed once and published with monotonically increasing sequence; disposing waits for an in-flight provider call; provider exceptions publish `Faulted` without escaping the capture callback.

- [ ] **Step 2: Run the focused tests and verify they fail**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~RecognitionSessionTests --no-restore`

Expected: FAIL because the session and provider interfaces are missing.

- [ ] **Step 3: Implement capture, backpressure, and leases**

`RecognitionSession.AcquireAsync(RecognitionLeaseKind kind, WindowIdentity target, CancellationToken)` must reuse one capture source per target. Keep only the newest frame with an interlocked exchange; serialize provider execution with a single worker; cancel and await that worker during final release. A lease implements `IAsyncDisposable` and releases exactly once. Ignore preview-only frames when recognition is disabled, but keep the existing preview capture path available.

- [ ] **Step 4: Add the diagnostic provider**

`DiagnosticRecognitionProvider` returns a valid snapshot with `Running` health, empty target arrays, and HUD fields marked unavailable. It must preserve frame dimensions/timestamps so the preview can prove the pipeline is live before model adapters are added. Keep provider interfaces replaceable for OpenCV/YOLO/OCR implementations.

- [ ] **Step 5: Run the focused tests and verify they pass**

Run the same command; expected: PASS with no leaked fake capture sessions.

- [ ] **Step 6: Commit the session implementation**

```powershell
git add src/Maple.Host/Recognition tests/Maple.Host.Tests/Recognition
git commit -m "feat: add shared recognition session leases"
```

### Task 3: Add configuration and Host bridge state for the recognition switch

**Files:**
- Modify: `src/Maple.Core/Configuration/StationaryAttackConfig.cs`
- Modify: `src/Maple.Host/Configuration/JsonConfigStore.cs`
- Modify: `src/Maple.WindowsHost/MainWindow.xaml.cs`
- Modify: `client/src/bridge/types.ts`
- Modify: `client/src/pages/StationaryAttackPage.tsx`
- Create: `client/src/components/RecognitionToggle.tsx`
- Test: `client/src/pages/StationaryAttackPage.test.tsx`

- [ ] **Step 1: Write the failing UI/config tests**

Assert that `recognition.enabled` defaults to `false`, JSON round-trips the property in camelCase, the toggle emits `config.save` with the new value, and starting/stopping does not implicitly flip the setting.

- [ ] **Step 2: Run the focused React tests and verify they fail**

Run: `npm --prefix client test -- --run src/pages/StationaryAttackPage.test.tsx`

Expected: FAIL because the recognition field and control are absent.

- [ ] **Step 3: Implement the opt-in configuration and bridge events**

Add a nested `RecognitionConfig { Enabled }` with a safe disabled default. Extend the existing config serialization and bridge message types. Add `recognition.status` and `recognition.snapshot` messages, carrying only metadata/HUD/target summaries, never raw frame pixels.

- [ ] **Step 4: Run the focused React tests and verify they pass**

Run the same command; expected: PASS.

- [ ] **Step 5: Commit configuration and bridge changes**

```powershell
git add src/Maple.Core/Configuration src/Maple.Host/Configuration src/Maple.WindowsHost/MainWindow.xaml.cs client/src
git commit -m "feat: add opt-in recognition configuration"
```

### Task 4: Integrate preview capture with recognition and render overlays

**Files:**
- Modify: `src/Maple.WindowsHost/Preview/PreviewWindowHost.cs`
- Modify: `src/Maple.WindowsHost/MainWindow.xaml.cs`
- Create: `src/Maple.WindowsHost/Preview/RecognitionOverlayRenderer.cs`
- Create: `tests/Maple.Host.Tests/Preview/RecognitionOverlayRendererTests.cs`

- [ ] **Step 1: Write failing overlay tests**

Given a frame-sized canvas and a snapshot containing self, monster, drop, other-player and HUD observations, assert that the renderer produces one overlay geometry per target type, excludes targets marked below confidence, and emits text containing HP/MP/EXP and frame age. Assert stale snapshots render a visible stale diagnostic and no actionable target geometry.

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~RecognitionOverlayRendererTests --no-restore`

Expected: FAIL because the renderer is missing.

- [ ] **Step 3: Implement overlay rendering without changing capture ownership**

Keep `PreviewWindowHost` responsible for WPF window lifetime and frame bitmap display. Subscribe the recognition session to the same `CapturedFrame` stream, retain the latest immutable snapshot, and draw translucent rectangles/labels in a WPF `Canvas` above the image. Show a compact HUD panel with HP/MP/EXP, provider health, frame age, and fault code. Do not send snapshots through React as images.

- [ ] **Step 4: Add preview lease acquisition/release**

When preview opens and recognition is enabled, acquire a preview lease for the resolved target identity; when preview closes or recognition is disabled, dispose that lease after in-flight rendering completes. Preview with recognition disabled keeps the existing raw-frame diagnostics behavior.

- [ ] **Step 5: Run preview and Host tests**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~Preview --no-restore`

Expected: all existing preview tests plus overlay tests pass.

- [ ] **Step 6: Commit preview recognition**

```powershell
git add src/Maple.WindowsHost/Preview src/Maple.WindowsHost/MainWindow.xaml.cs tests/Maple.Host.Tests/Preview
git commit -m "feat: show recognition results in realtime preview"
```

### Task 5: Keep recognition alive during stationary attack and expose snapshots to medication consumers

**Files:**
- Modify: `src/Maple.WindowsHost/MainWindow.xaml.cs`
- Modify: `src/Maple.Host/Windows/StationarySessionApplicationService.cs`
- Modify: `src/Maple.Host/Stationary/StationarySessionController.cs`
- Create: `src/Maple.Host/Recognition/RecognitionSnapshotReader.cs`
- Create: `tests/Maple.Host.Tests/Recognition/RecognitionSnapshotReaderTests.cs`

- [ ] **Step 1: Write failing integration tests**

Assert that starting a stationary session with recognition enabled acquires a run lease even when the preview is closed, stopping the session releases it after controller/heartbeat cleanup, and a stale HUD snapshot returns `Unavailable` rather than a medication decision. Assert that recognition-disabled sessions never create a capture source.

- [ ] **Step 2: Run focused tests and verify they fail**

Run: `dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~RecognitionSnapshotReaderTests --no-restore`

Expected: FAIL because the reader and session wiring are absent.

- [ ] **Step 3: Wire the run lease into the existing lifecycle**

Create the recognition session once in `MainWindow`/host composition. During start, after target identity resolution and before the controller begins input, acquire a run lease when `config.Recognition.Enabled` is true. Pass a snapshot reader into the application service/controller. During stop and window close, release the lease only after input release and heartbeat shutdown are awaited; make repeated stop/cleanup idempotent.

- [ ] **Step 4: Add the medication-facing reader boundary**

`RecognitionSnapshotReader.TryReadVitals()` returns a value object containing HP/MP current/max/percent, EXP percent, confidence and freshness. It returns `Unavailable` for missing, stale, target-lost, or low-confidence fields. It has no Broker or input dependency; a later medication policy will decide whether to press a configured key.

- [ ] **Step 5: Run Host, Core and broker tests**

Run: `dotnet test MapleProduct.sln --no-restore`

Expected: all existing tests plus new recognition tests pass.

- [ ] **Step 6: Commit stationary integration**

```powershell
git add src/Maple.WindowsHost/MainWindow.xaml.cs src/Maple.Host/Windows src/Maple.Host/Stationary src/Maple.Host/Recognition tests/Maple.Host.Tests/Recognition
git commit -m "feat: keep recognition active during stationary sessions"
```

### Task 6: Add Windows verification and update phase documentation

**Files:**
- Modify: `docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md`
- Modify: `docs/PHASE_1_ACCEPTANCE.md`
- Create: `docs/phase-2/evidence/windows-recognition-preview.md`
- Modify: `tests/Maple.Host.Tests/Preview/PreviewSessionTests.cs` if target-loss coverage needs a shared fake

- [ ] **Step 1: Document recognition preview acceptance cases**

Record exact steps for a 1366x768 client: identify one target window, enable recognition, open preview before starting attack, verify overlay/status updates, start stationary attack with preview closed, verify HP/MP/EXP snapshots remain fresh, reopen preview, disable recognition, and verify capture resources stop.

- [ ] **Step 2: Add automated target-loss/fault evidence hooks**

Ensure the evidence log captures stable codes for `TARGET_LOST`, `RECOGNITION_STALE`, `RECOGNITION_FAULT`, and `RECOGNITION_DISABLED`, without marking real-client checks complete until manually observed.

- [ ] **Step 3: Run final verification**

Run:

```powershell
dotnet test MapleProduct.sln --no-restore
dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release --no-restore
npm --prefix client test -- --run
npm --prefix client run build
git diff --check
```

Expected: all tests/builds pass, client build succeeds, and `git diff --check` is clean.

- [ ] **Step 4: Commit documentation and verification evidence**

```powershell
git add docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md docs/PHASE_1_ACCEPTANCE.md docs/phase-2/evidence
git commit -m "docs: define recognition preview acceptance"
```
