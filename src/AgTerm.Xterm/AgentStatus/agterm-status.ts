// agterm-win-pi-status-extension
import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";

export default function (pi: ExtensionAPI) {
  const wrapper = __AGTERM_STATUS_WRAPPER__;

  async function report(state: string, option?: string): Promise<void> {
    if (!process.env.AGTERM_SESSION_ID) return;
    try {
      await pi.exec("cmd.exe", [
        "/d", "/v:on", "/c", "call", wrapper,
        state, ...(option ? [option] : []), "agterm-win",
      ], { timeout: 5_000 });
    } catch {
      // Status reporting must not interrupt Pi.
    }
  }

  pi.on("agent_start", async () => report("active", "--blink"));
  pi.on("agent_settled", async () => report("completed", "--auto-reset"));
}
