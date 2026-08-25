# Visual Tracking Hysteresis Design

## Problem

Session `7dd9938d-b019-4d7e-9178-3ab04ee64681` remained inside the configured platform boundary. At cycle 184 the last trusted offset was about `-27px`, while the configured safe half-width was `84px`. The appearance score nevertheless crossed from `0.702` to `0.692`. Acquisition and established tracking both used a hard `0.70` threshold, so one small score change revoked trust. Later scores oscillated between `0.692` and `0.704`, preventing three consecutive recovery frames and forcing continuous fallback.

## Decision

Keep character acquisition at `0.70`. After the same character has been established, accept local tracking candidates at `0.68`. This creates a two-point hysteresis band for animation and rendering variation without widening the search area.

The lower threshold applies only to a previously established appearance track. Existing safeguards remain unchanged: the candidate must stay inside the local scaled `12px` anchor window, satisfy the `0.04` local peak margin, remain within the maximum jump, and provide three consecutive new frames after a transient loss. A score below `0.68`, ambiguity, a jump, a stale frame, or capture failure still revokes movement authorization immediately.

## Scope

Only the appearance tracking threshold and its tests change. Acquisition, template matching, search geometry, random movement, platform correction, fallback timing, and name-template compatibility remain unchanged.

## Verification

- A previously trusted local appearance remains trusted at score `0.692`.
- Initial acquisition at score `0.692` remains rejected.
- An established local track is revoked at score `0.67`.
- Existing ambiguity and jump tests continue to pass.
