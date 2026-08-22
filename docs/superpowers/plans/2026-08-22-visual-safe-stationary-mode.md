# Visual-Safe Stationary Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in visual-safe stationary attack mode that identifies the operator by a saved name template and prevents lateral movement outside a manually selected fixed platform, without changing the existing continuous mode.

**Architecture:** Keep the existing `StationarySessionController` intact and add a separate visual controller in `Maple.Host`. Pure Host components own profile validation, template matching, temporal identity locking, platform safety state, and random move authorization. `Maple.WindowsHost` owns frame capture, WPF drag selection, local profile persistence, application wiring, and structured WebView messages; React submits only mode/configuration/session intent.

**Tech Stack:** .NET 8, C#, xUnit, WPF, Windows Graphics Capture, React 19, TypeScript, Ant Design, Vitest.

---

### Task 1: Authorize The New Product Mode

**Files:**
- Modify: `docs/PRODUCT_SPEC.md`
- Modify: `docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md`
- Modify: `docs/PHASE_1_ACCEPTANCE.md`

- [ ] **Step 1: Add the third mode to the source-of-truth specification**

Specify `visualSafeContinuous` as an independent opt-in mode. State that the old continuous mode is behaviorally unchanged, platform coordinates are fixed per viewport, identity requires the saved name template, untrusted/outside states freeze movement while attack continues, and React never receives frame pixels.

- [ ] **Step 2: Add implementation contracts and state transitions to the phase design**

Document `VisualStationaryProfile`, `SelfNameTemplateMatcher`, `SelfIdentityStabilizer`, `VisualPlatformSafetyGate`, `VisualStationaryMovementPlanner`, and `VisualStationarySessionController`, including `Acquiring -> TrustedSafe/Guard -> UntrustedFrozen/OutsideFrozen` transitions.

- [ ] **Step 3: Add executable acceptance items**

Add checklist entries for profile validation, ambiguous-name rejection, three-frame locking, per-segment fresh-frame gating, random inward movement, old-mode regression, structured UI state, and Windows package evidence.

- [ ] **Step 4: Check documentation consistency**

Run:

```powershell
Select-String -Path docs/PRODUCT_SPEC.md,docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md,docs/PHASE_1_ACCEPTANCE.md -Pattern 'visualSafeContinuous|视觉增强持续攻击|UntrustedFrozen|OutsideFrozen'
git diff --check -- docs/PRODUCT_SPEC.md docs/PHASE_1_STATIONARY_ATTACK_DESIGN.md docs/PHASE_1_ACCEPTANCE.md
```

Expected: every named contract appears in the intended document and `git diff --check` exits 0.

### Task 2: Add Mode And Visual Profile Contracts

**Files:**
- Modify: `src/Maple.Core/Configuration/StationaryAttackConfig.cs`
- Modify: `src/Maple.Core/Configuration/StationaryConfigValidator.cs`
- Create: `src/Maple.Host/Stationary/VisualStationaryContracts.cs`
- Create: `src/Maple.Host/Stationary/VisualStationaryProfileStore.cs`
- Test: `tests/Maple.Core.Tests/Configuration/StationaryConfigValidatorTests.cs`
- Create: `tests/Maple.Host.Tests/Stationary/VisualStationaryProfileStoreTests.cs`

- [ ] **Step 1: Write failing mode and profile tests**

Add tests proving `AttackTriggerMode.VisualSafeContinuous` is valid, `MonsterInRange` remains disabled, and profiles reject out-of-frame rectangles, low-texture templates, too-narrow platforms, and viewport mismatches. A valid JSON round trip must preserve the BGRA template and rectangles.

```csharp
Assert.True(StationaryConfigValidator.Validate(
    StationaryAttackConfig.Default with { AttackTriggerMode = AttackTriggerMode.VisualSafeContinuous }).IsValid);
Assert.Equal(VisualProfileValidationCode.Valid,
    VisualStationaryProfileValidator.Validate(ValidProfile(), 1366, 768));
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Core.Tests/Maple.Core.Tests.csproj --filter "FullyQualifiedName~StationaryConfigValidatorTests"
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationaryProfileStoreTests"
```

Expected: compile failures because the enum member and visual profile types do not exist.

- [ ] **Step 3: Implement contracts and persistence**

Add `VisualSafeContinuous` to `AttackTriggerMode`. Define immutable `FrameRect`, `VisualStationaryProfile`, `VisualStationaryConfigStatus`, and a validator with explicit minimum sizes and texture variance. Implement atomic JSON metadata plus a binary BGRA template under `%LOCALAPPDATA%\MapleProduct\visual-stationary`; persistence takes an injected root path for tests.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the two commands from Step 2.

Expected: all selected tests pass.

### Task 3: Match And Stabilize The Operator Name

**Files:**
- Create: `src/Maple.Host/Stationary/SelfNameTemplateMatcher.cs`
- Create: `src/Maple.Host/Stationary/SelfIdentityStabilizer.cs`
- Create: `tests/Maple.Host.Tests/Stationary/SelfNameTemplateMatcherTests.cs`
- Create: `tests/Maple.Host.Tests/Stationary/SelfIdentityStabilizerTests.cs`

- [ ] **Step 1: Write failing matcher tests**

Use synthetic BGRA frames with two inserted patterns to prove the matcher returns best score, second-best score, and center X; uniform input must not match. Add a case where two equal copies make `BestMinusSecond` zero.

```csharp
SelfNameMatch result = new SelfNameTemplateMatcher().Match(frame, template, searchRect);
Assert.InRange(result.BestScore, 0.99, 1.0);
Assert.True(result.BestScore - result.SecondBestScore >= 0.08);
```

- [ ] **Step 2: Run matcher tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~SelfNameTemplateMatcherTests|FullyQualifiedName~SelfIdentityStabilizerTests"
```

Expected: compile failures because matcher and stabilizer types do not exist.

- [ ] **Step 3: Implement normalized edge/color matching**

Sample at most 128 high-variance template pixels, calculate normalized color correlation over the bounded platform search area, retain spatially separated best and second peaks, and return no candidate for invalid buffers or near-uniform templates. Do not depend on `Maple.WindowsHost` or ONNX.

- [ ] **Step 4: Implement the temporal lock**

Require score `>= 0.90`, best-second margin `>= 0.06`, three distinct increasing frame sequences, and center jump `<= 12px` before `Trusted`. A missing, ambiguous, repeated, or jumping frame resets the streak and immediately removes movement authorization.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the command from Step 2.

Expected: all matcher and stabilizer tests pass.

### Task 4: Implement Platform Safety And Random Move Authorization

**Files:**
- Create: `src/Maple.Host/Stationary/VisualPlatformSafetyGate.cs`
- Create: `src/Maple.Host/Stationary/VisualStationaryMovementPlanner.cs`
- Create: `tests/Maple.Host.Tests/Stationary/VisualPlatformSafetyGateTests.cs`
- Create: `tests/Maple.Host.Tests/Stationary/VisualStationaryMovementPlannerTests.cs`

- [ ] **Step 1: Write failing safety state tests**

Prove positions classify as `Safe`, `GuardLeft`, `GuardRight`, and `Outside`; the guard is at least 32 logical pixels at 1366 width, grows after a larger observed step, and never shrinks during a session.

```csharp
VisualPlatformState state = gate.ObserveTrusted(sequence: 4, x: 105, platform, frameWidth: 1366);
Assert.Equal(VisualSafetyState.GuardLeft, state.State);
```

- [ ] **Step 2: Write failing random authorization tests**

Prove safe state preserves random direction/duration, left guard permits only right, right guard permits only left, and untrusted/outside returns no movement. Multiple inward samples must not collapse to one fixed duration.

- [ ] **Step 3: Run focused tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualPlatformSafetyGateTests|FullyQualifiedName~VisualStationaryMovementPlannerTests"
```

Expected: compile failures because both components are absent.

- [ ] **Step 4: Implement safety classification and planner**

The gate owns a session guard width of `max(32 * frameWidth / 1366, observedStep + 3 * jitter)`. The planner samples direction and duration through `IRandomSource`, rejects unsafe outward candidates, and returns `VisualMoveDecision.None(reason)` rather than throwing when movement is frozen.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the command from Step 3.

Expected: all selected tests pass.

### Task 5: Build The Visual Observation Pipeline

**Files:**
- Create: `src/Maple.Host/Stationary/VisualStationaryObservationSession.cs`
- Create: `tests/Maple.Host.Tests/Stationary/VisualStationaryObservationSessionTests.cs`
- Modify: `src/Maple.WindowsHost/Preview/PreviewWindowHost.cs`

- [ ] **Step 1: Write failing observation tests**

Drive synthetic frames through the session and prove it publishes `Acquiring`, then `TrustedSafe`; an ambiguous frame immediately publishes `UntrustedFrozen`; viewport mismatch publishes the stable config status without attempting matching. Require an observation sequence newer than the movement release sequence before authorizing another move.

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationaryObservationSessionTests"
```

Expected: compile failure because the observation session is absent.

- [ ] **Step 3: Implement the observation session**

Compose matcher, stabilizer, and safety gate behind `PushFrame(CapturedFrame)`. Store only the latest immutable `VisualStationaryObservation`; expose `WaitForTrustedAfterAsync(sequence, timeout, token)` for the controller without polling React.

- [ ] **Step 4: Feed visual frames from the native preview host**

Add a frame consumer registration API to `PreviewWindowHost`. Dispatch a captured frame to the visual observation session before rendering and unregister it when the visual run stops. Keep preview rendering failures isolated from observation state.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the command from Step 2.

Expected: all selected tests pass.

### Task 6: Add Native Two-Step Platform And Name Selection

**Files:**
- Create: `src/Maple.WindowsHost/Preview/VisualStationarySetupController.cs`
- Modify: `src/Maple.WindowsHost/Preview/PreviewWindowHost.cs`
- Create: `tests/Maple.Host.Tests/Stationary/VisualStationarySetupTests.cs`

- [ ] **Step 1: Write failing coordinate conversion and setup tests**

Extract testable viewport-to-frame conversion into Host-level data functions. Prove `Stretch.Uniform` letterboxing is removed, drag direction is normalized, rectangles clamp to frame bounds, and the second selection crops exact BGRA bytes.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationarySetupTests"
```

Expected: compile failure because setup geometry is absent.

- [ ] **Step 3: Implement the WPF setup interaction**

Add one toolbar button with a shield icon-equivalent glyph and tooltip `配置视觉安全区`. On click, freeze the latest frame, collect a platform drag then a tight name drag, render selection rectangles and computed guard bands, validate, persist, and publish `visualStationary.config.updated`. Escape cancels without overwriting the last valid profile.

- [ ] **Step 4: Run tests and build WindowsHost**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationarySetupTests"
dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj --no-restore
```

Expected: tests pass and WindowsHost builds with 0 errors.

### Task 7: Add The Independent Visual Session Controller

**Files:**
- Create: `src/Maple.Host/Stationary/VisualStationarySessionController.cs`
- Create: `tests/Maple.Host.Tests/Stationary/VisualStationarySessionControllerTests.cs`
- Modify: `src/Maple.WindowsHost/MainWindow.xaml.cs`
- Create: `src/Maple.WindowsHost/MainWindow.VisualStationary.cs`

- [ ] **Step 1: Write failing controller tests**

Prove the controller sends no direction input before a trusted frame, continues attack while untrusted, sends only inward random movement in a guard, waits for a newer trusted frame after KeyUp, freezes outside, and always releases inputs on cancellation/focus/Broker faults. Also run an existing continuous-controller order test unchanged.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationarySessionControllerTests|FullyQualifiedName~Runs_one_complete_cycle_in_strict_key_order"
```

Expected: visual tests fail to compile; the existing continuous test passes.

- [ ] **Step 3: Implement the separate controller**

Reuse `WeightedAttackDurationSampler`, `IStationaryActionSink`, `IStationarySafetyGate`, `IMonotonicScheduler`, and the existing attack phase publishing contract. Do not instantiate or branch inside `StationarySessionController`. While visual movement is frozen, publish the structured visual state and continue the next attack/rest cycle without a direction action.

- [ ] **Step 4: Wire mode-specific startup in MainWindow**

Parse `AttackTriggerMode.VisualSafeContinuous`, require a viewport-compatible saved profile, start/reuse preview capture, attach the observation session, wait for its initial three-frame lock without sending input, then prepare Broker and run the visual controller. Old `Always` startup follows the existing code path unchanged; `MonsterInRange` is still rejected.

- [ ] **Step 5: Run focused and full .NET tests**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter "FullyQualifiedName~VisualStationarySessionControllerTests|FullyQualifiedName~StationarySessionControllerTests"
dotnet test MapleProduct.sln --no-restore
```

Expected: focused tests and the full .NET suite pass.

### Task 8: Expose The Mode And Structured Visual State In React

**Files:**
- Modify: `client/src/bridge/types.ts`
- Modify: `client/src/bridge/bridge.ts`
- Modify: `client/src/bridge/configValidation.ts`
- Modify: `client/src/components/AttackModeField.tsx`
- Create: `client/src/components/VisualSafetyStatus.tsx`
- Modify: `client/src/components/SessionStatusPanel.tsx`
- Modify: `client/src/pages/StationaryAttackPage.tsx`
- Modify: `client/src/pages/StationaryAttackPage.test.tsx`
- Modify: `client/src/styles/app.css`

- [ ] **Step 1: Write failing UI tests**

Prove the third mode is enabled, selecting it exposes `配置视觉安全区`, start sends only the enum and intent, config status renders, visual pixel offset formats negative/positive directions, and no bridge command accepts frame bytes or caller-provided movement.

- [ ] **Step 2: Run UI tests and verify RED**

Run:

```powershell
Set-Location client
npm test -- --run src/pages/StationaryAttackPage.test.tsx
Set-Location ..
```

Expected: assertions fail because the mode and visual status are absent.

- [ ] **Step 3: Implement types, controls, and status**

Add `visualSafeContinuous` to the TypeScript config union, send `openVisualStationarySetup`, subscribe to `visualStationary.config.updated` and `visualStationary.state.updated`, and render compact status rows for lock state, score, and signed pixel offset. Keep the existing millisecond offset visible as a separate diagnostic row.

- [ ] **Step 4: Run frontend tests, lint, and build**

Run:

```powershell
Set-Location client
npm test -- --run
npm run lint
npm run build
Set-Location ..
```

Expected: 0 failed tests, 0 lint errors, and a successful production build.

### Task 9: Verify Isolation, Package, And Produce Evidence

**Files:**
- Modify: `docs/phase-1/evidence/visual-safe-stationary-mode.md`
- Update generated package: `artifacts/phase-1/win-x64/MapleProduct/**`
- Update generated archive: `artifacts/phase-1/win-x64/MapleProduct-phase-1-win-x64.zip`

- [ ] **Step 1: Verify specification coverage**

Review every acceptance item and record the automated command or Windows manual check that proves it. Explicitly record that the old continuous path did not gain a visual dependency.

- [ ] **Step 2: Run fresh complete verification**

Run:

```powershell
dotnet test MapleProduct.sln --no-restore
Set-Location client
npm test -- --run
npm run lint
npm run build
Set-Location ..
dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release --no-restore
```

Expected: all .NET and frontend tests pass, lint has no errors, client build succeeds, and Release WindowsHost build exits 0.

- [ ] **Step 3: Publish the Windows x64 package**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-windows.ps1
Get-FileHash artifacts/phase-1/win-x64/MapleProduct/Maple.WindowsHost.exe -Algorithm SHA256
Get-FileHash artifacts/phase-1/win-x64/MapleProduct-phase-1-win-x64.zip -Algorithm SHA256
```

Expected: publisher exits 0 and both files have non-empty SHA-256 values.

- [ ] **Step 4: Record remaining real-machine acceptance**

The evidence file must distinguish automated proof from checks that still require the user's real game window: two drag selections, other-player interference, temporary name occlusion, guard entry, long random run, and no platform exit.
