# B6: message bus between panes

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B6 follows `docs/upstream/delta-ed831ab-to-v0.40.0.md` section 1 and the `message` event in section 10.

## Upstream commits

- `b0839692` (#847): `msg.send`, `msg.read`, `msg.ack`, `surfaces[].pendingMessages`, the `message` event, and `agtermctl msg send|read|ack`.

## Tasks

1. Core model: `MessageBus` (per-pane mailboxes with the body, mailbox and batch limits) and `MessageAddress` (`SESSION:PANE`, a role only for a recipient). Done.
2. Protocol: the three commands, the args and result fields, the `message` event and its payload, `pendingMessages` on the tree surface node. `MessageDispatcher` validates and runs the mailbox operations behind `IMessagePaneHost`. Done.
3. Store: live pane resolution, broadcast lists, the event emit, and the mailbox drops on session, split, scratch and main pane exits. Done.
4. Hosts: the headless host per window; the live host resolves addresses across open windows and drops mailboxes on window close and on a terminal page reload. Done.
5. CLI: `agtermctl msg send|read|ack`, the identity from `AGTERM_SESSION_ID`/`AGTERM_PANE_ID` or `--as`, `read --wait --timeout` with exit 124, `read --ack` confirmation, and the human event line. Done.
6. E2E over the pipe, parity lists, PLATFORM-NOTES, skill text, catalog lines, versions. Done.

## Decisions

- The mailbox key is the surface's pane token (`AGTERM_PANE_ID`), which is a GUID in the `N` form here. Addresses accept the `N` or the dashed form.
- A surface that is not realized yet is not a live pane.
- The port has no `session restart` and no main pane respawn, so those drops are not ported. A terminal page reload gives new tokens, so it drops the window's mail.
