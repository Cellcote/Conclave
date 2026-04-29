# Notifications & settings sync

**One-liner:** Audio + OS notifications when a session needs attention; sync settings/keybinds across machines.

**Tier:** QoL · **Status:** Backlog

## Why this matters

Two independent QoL features; bundled because each is small.

- **Notifications:** Cursor's "Audio notification on completion" thread has 19 replies — small feature, surprising engagement. Power users running 10 sessions can't watch them all; they want a chime when one needs them.
- **Settings sync:** Cursor's "Settings Sync Across Devices" has 139 replies — top-5 on the entire forum. Power users have multiple machines and hate reconfiguring.

## Sketch

### Notifications

- Triggers: status → `needs-attention`, status → `errored`, session completes, gate awaiting approval (overlap with enterprise file 08).
- Channels: OS-native notification (Notification Center / Action Center / libnotify), audio chime (configurable sound, mute toggle), optional Slack/Teams via hooks (overlap with file 06 power-user).
- Per-session opt-out (don't ping me for low-priority sessions).
- Quiet hours.

### Settings sync

- What syncs: keybindings, theme, density, accent, hooks config, default model, account bindings (file 10 power-user).
- What does *not* sync: per-machine paths (worktree location, repo paths), OS-native credentials.
- Transport options:
  - **Local file** (Dropbox/iCloud/syncthing-friendly): write settings to a known location, let user-level file sync handle it. Zero infra.
  - **Hosted** (later): Conclave-managed sync over the control plane.

## Open questions

- Default to "local file" for the v1 settings sync — good enough? Avoids a backend.
- For audio: bundled chimes, or system sounds, or user-supplied?
- Anything time-sensitive enough to warrant push (vs. local OS notifications)? Probably not for v1.

## Notes

Both features are individually small and beloved. Settings sync is on the road to a hosted account anyway (and overlaps with file 10's per-machine credential story) — start with local-file sync, upgrade to hosted later.
