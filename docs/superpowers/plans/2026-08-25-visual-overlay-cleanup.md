# Visual Overlay Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove all text attached to visual platform-protection rectangles while preserving colored geometry, out-of-image guidance, persistence, recognition, and movement behavior.

**Architecture:** Make the Host overlay contract geometry-only so no renderer can attach captions to visual safety boxes. Keep workflow feedback in the preview toolbar and diagnostics bar, then simplify the WPF renderer to create rectangles only.

**Tech Stack:** .NET 8, C#, WPF, xUnit

---

### Task 1: Lock the geometry-only overlay contract

**Files:**
- Modify: `tests/Maple.Host.Tests/Stationary/VisualPreviewOverlayLayoutTests.cs`
- Modify: `src/Maple.Host/Stationary/VisualPreviewOverlayLayout.cs`

- [ ] **Step 1: Write the failing test**

Replace label assertions with kind and geometry assertions, and add:

```csharp
Assert.Null(typeof(VisualPreviewOverlay).GetProperty("Label"));
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~VisualPreviewOverlayLayoutTests
```

Expected: FAIL because `VisualPreviewOverlay.Label` still exists.

- [ ] **Step 3: Remove label data from the production contract**

Change the record to:

```csharp
public sealed record VisualPreviewOverlay(
    VisualPreviewOverlayKind Kind,
    FrameRect Bounds);
```

Construct every overlay with only `Kind` and `Bounds`, and remove the percentage formatting helper.

- [ ] **Step 4: Run test to verify it passes**

Run the filtered command from Step 2.

Expected: all `VisualPreviewOverlayLayoutTests` pass.

### Task 2: Render rectangles without captions

**Files:**
- Modify: `src/Maple.WindowsHost/Preview/VisualStationarySetupController.cs`
- Modify: `src/Maple.WindowsHost/Preview/PreviewWindowHost.cs`

- [ ] **Step 1: Simplify the rectangle renderer**

Change `AddRectangle` to accept only geometry and visual style:

```csharp
private void AddRectangle(
    FrameRect frameRectangle,
    Brush stroke,
    Color fill,
    double thickness)
```

Delete caption `Border` and `TextBlock` construction, `LabelPosition`, and all label arguments for saved, live, and drag rectangles.

- [ ] **Step 2: Add a compact legend outside the image**

Add a toolbar `TextBlock` with colored `Run` entries for yellow platform, green random core, blue template, cyan trusted identity, and orange candidate. Do not place the legend in `overlay` or `imageLayer`.

- [ ] **Step 3: Preserve live score outside the image**

Keep the existing diagnostics text:

```csharp
$"视觉本人 {(visual.IdentityTrusted ? "可信" : "候选")} {visual.Platform.BestScore:P0}"
```

This retains the match score without attaching it to a rectangle.

- [ ] **Step 4: Build WindowsHost**

Run:

```powershell
dotnet build src/Maple.WindowsHost/Maple.WindowsHost.csproj -c Release -r win-x64
```

Expected: build succeeds with zero errors.

### Task 3: Restore facing after one-sided visual recentering

**Files:**
- Modify: `tests/Maple.Host.Tests/Stationary/VisualStationarySessionControllerTests.cs`
- Modify: `src/Maple.Host/Stationary/VisualStationarySessionController.cs`

- [ ] **Step 1: Write the failing controller test**

Create a trusted sequence that remains right of the center band for two left corrections and then enters the center band. Assert the direction sequence is:

```csharp
["Down:MoveLeft", "Down:MoveLeft", "Down:MoveRight"]
```

and assert `VISUAL_FACING_RESTORED` is published before the controller can start another attack.

- [ ] **Step 2: Run the focused test and verify it fails**

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~Inward_correction_restores_initial_facing_before_the_next_attack
```

Expected: FAIL because the controller currently emits only the first `MoveLeft`.

- [ ] **Step 3: Route opposite-facing correction through the restore state**

After a successful one-sided correction, call `RestoreInitialFacingAsync` when the correction direction differs from `initialFacing`. Inside the restore loop, request the latest `RequiredInwardDirection` while outside the center band; request `initialFacing` only when no inward correction remains. Publish restored only after an initial-facing movement succeeds.

- [ ] **Step 4: Run focused visual controller tests**

```powershell
dotnet test tests/Maple.Host.Tests/Maple.Host.Tests.csproj --filter FullyQualifiedName~VisualStationarySessionControllerTests
```

Expected: all visual controller tests pass.

### Task 4: Regression verification and package

**Files:**
- No production source changes expected

- [ ] **Step 1: Run all .NET tests**

```powershell
dotnet test MapleProduct.sln -c Release
```

Expected: all test projects pass.

- [ ] **Step 2: Run frontend checks**

```powershell
npm test -- --run
npm run lint
npm run build
```

Run from `client`. Expected: tests and build pass; lint has no new errors.

- [ ] **Step 3: Publish Windows package**

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-windows.ps1 -OutputRoot artifacts/phase-1/win-x64-visual-overlay-cleanup
```

Expected: `MapleProduct/Maple.WindowsHost.exe` and the ZIP exist under the output root.

- [ ] **Step 4: Commit implementation**

```powershell
git add src/Maple.Host/Stationary/VisualPreviewOverlayLayout.cs src/Maple.WindowsHost/Preview/VisualStationarySetupController.cs src/Maple.WindowsHost/Preview/PreviewWindowHost.cs tests/Maple.Host.Tests/Stationary/VisualPreviewOverlayLayoutTests.cs docs/superpowers/plans/2026-08-25-visual-overlay-cleanup.md
git commit -m "fix: remove text from visual safety overlays"
```
