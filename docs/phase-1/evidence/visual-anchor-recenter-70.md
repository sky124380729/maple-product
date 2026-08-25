# Visual Anchor, Recovery, And 70% Threshold Evidence

Date: 2026-08-25

## Root cause

- Session `4be98bc5-c7e8-4f98-a454-22bf2ff7b260` had no direction input while the reported visual offset moved from `-32px` to `-118px`.
- The appearance search anchor advanced on every trusted frame. Repeated local candidates could therefore move the anchor by at most `12px` per frame but an unlimited distance over time.
- The previous visual fallback path depended on calibration and did not run the ordinary continuous movement planner. Fallback movement also did not open the appearance tracking window, so a moving character could remain outside the frozen search anchor.

## Implemented behavior

- Character acquisition, tracking, and recovery thresholds are `0.70` and still require three new frames.
- The idle appearance anchor is frozen. It advances only during an explicit direction movement window, including ordinary movement fallback cycles.
- Trusted positions outside the center band but still inside the platform receive one randomly sampled inward correction segment.
- After 15 seconds of continuous visual unavailability, the visual controller continues random attacks and uses the ordinary `StationaryMovementPlanner` for left/right movement without requiring visual calibration.
- The fallback planner preserves the measured time-offset direction. Values beyond the configured time boundary are clamped to the same-sign boundary so they cannot stop the attack session.
- Reacquisition switches back to visual protection only at the next complete movement-cycle boundary.
- Visual and ordinary fallback pairs use `100ms + independently sampled configured gap` before the second direction.
- Appearance scoring gives more weight to normalized structure correlation so a blank local region measuring `0.7031` under the previous weighting falls below the new `0.70` acceptance threshold.

## Verification

- Focused visual regression suite: `71/71` passed.
- Full .NET solution: `438/438` passed.
- Frontend Vitest: `46/46` passed.
- Frontend lint: exit code `0` with four pre-existing React Hook warnings.
- Frontend production build and Windows x64 self-contained publish: exit code `0`.

## Release

- EXE: `artifacts/phase-1/win-x64-visual-anchor-recenter/MapleProduct/Maple.WindowsHost.exe`
- EXE SHA-256: `0A431B25CABF4E71F60DD48F554AFDDF8C3E61B4405CF4A667F652B2771F39FF`
- ZIP: `artifacts/phase-1/win-x64-visual-anchor-recenter/MapleProduct-phase-1-win-x64.zip`
- ZIP SHA-256: `0B5CD7EA851DB41B8C42A39343A0D93651C9E6EB5ACC88C654778D12B6B931DC`
- ZIP entries: `500`; all six required host, core, and broker binary hashes match the published directory.
