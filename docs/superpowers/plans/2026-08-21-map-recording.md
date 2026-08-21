# 单地图自动录制建图 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在实时预览中提供不发送输入的地图录制器，自动从用户走图过程生成可加载的 `.mapzip` 观察包。

**Architecture:** `MapFrameGeometryDetector` 负责单帧平台/梯子候选，`MapRecorder` 负责限额、跨帧合并和样本记录，`MapRecordingExporter` 负责原子写出地图包。`PreviewWindowHost` 只负责 UI 生命周期和帧转发，不承载检测算法；导出包继续由 `MapPackageLoader` 校验。

**Tech Stack:** .NET 8, WPF, `CapturedFrame`, `System.IO.Compression`, `System.Text.Json`, xUnit。

---

### Task 1: Geometry detector contracts

**Files:**
- Create: `src/Maple.Host/Navigation/MapRecordingModels.cs`
- Create: `src/Maple.Host/Navigation/MapFrameGeometryDetector.cs`
- Test: `tests/Maple.Host.Tests/Navigation/MapFrameGeometryDetectorTests.cs`

- [x] Write failing tests for normalized horizontal platform runs, vertical ladder runs, and rejection of unstable single-frame noise.
- [x] Run the focused test and confirm the missing detector types fail before implementation.
- [x] Implement immutable candidates and deterministic BGRA scanning with minimum run lengths and normalized coordinates.
- [x] Run the focused tests; all 3 detector tests pass.
- [x] Commit `509c022 feat: record map geometry into map packages`.

### Task 2: Recorder, limits, and package export

**Files:**
- Create: `src/Maple.Host/Navigation/MapRecorder.cs`
- Create: `tests/Maple.Host.Tests/Navigation/MapRecorderTests.cs`
- Modify: `src/Maple.Host/Navigation/MapPackageLoader.cs`

- [x] Write failing tests for start/stop, cross-frame deduplication, ladder/platform association, and sample limits.
- [x] Implement `MapRecorder` with a 5 FPS sample gate, 6000 sample limit, 10 minute deadline, and immutable result snapshots.
- [x] Implement atomic `.mapzip` export with generated `manifest.json`, `map.json`, and `recording/observations.jsonl`.
- [x] Run focused recorder tests; all 3 recorder tests pass and the generated package reloads successfully.
- [x] Commit `509c022 feat: record map geometry into map packages`.

### Task 3: Preview recording controls

**Files:**
- Modify: `src/Maple.WindowsHost/Preview/PreviewWindowHost.cs`
- Create: `src/Maple.WindowsHost/Preview/MapRecordingStatus.cs`
- Test: `tests/Maple.Host.Tests/Navigation/MapRecordingStatusTests.cs`

- [x] Add native preview controls for start/stop recording and a status line showing samples, candidates, and output path.
- [x] Forward captured frames to `MapRecorder` without changing recognition or input flows.
- [x] On preview close, stop recording and finalize the current package without starting Broker or sending keys.
- [x] Run WindowsHost Release build with 0 warnings and 0 errors.
- [ ] Commit preview controls together with phase-2 evidence.

### Task 4: End-to-end verification

**Files:**
- Modify: `docs/PHASE_2_AUTO_NAVIGATION_SCOPE.md`
- Create: `docs/phase-2/evidence/map-recording.md`

- [ ] Document that recording is observation-only and automatic navigation remains disabled.
- [ ] Run Core/Host/InputBroker tests, React tests/build, WindowsHost Release build, and `git diff --check`.
- [ ] Run a live capture smoke test against the current client without sending input and record the generated package path.
- [ ] Commit `docs: record map recording acceptance evidence`.
