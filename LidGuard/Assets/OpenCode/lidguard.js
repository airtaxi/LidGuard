// <LidGuard OpenCode plugin start>
// LidGuard OpenCode plugin version: 1
import { spawn } from "node:child_process";

const lidGuardHookCommand = __LIDGUARD_HOOK_COMMAND_JSON__;
const trackedEventTypes = new Set([
  "permission.asked",
  "permission.replied",
  "question.asked",
  "question.rejected",
  "question.replied",
  "question.v2.asked",
  "question.v2.rejected",
  "question.v2.replied",
  "session.deleted",
  "session.error",
  "session.idle",
  "session.status"
]);

function collectText(parts) {
  if (!Array.isArray(parts)) return "";
  return parts
    .filter((part) => part && part.type === "text" && typeof part.text === "string")
    .map((part) => part.text)
    .join("\n");
}

function createBasePayload(eventName, directory, worktree) {
  return {
    eventName,
    workingDirectory: directory || worktree || "",
    worktree: worktree || ""
  };
}

function extractSessionID(event) {
  const properties = event?.properties || {};
  return properties.sessionID || properties.sessionId || properties.info?.id || "";
}

function extractSessionStatus(event) {
  const status = event?.properties?.status;
  return typeof status?.type === "string" ? status.type : "";
}

function runHook(eventName, payload) {
  return new Promise((resolve) => {
    const child = spawn(lidGuardHookCommand, ["--event", eventName], {
      shell: true,
      windowsHide: true,
      stdio: ["pipe", "pipe", "pipe"]
    });

    let stdout = "";
    child.stdout.on("data", (chunk) => { stdout += chunk.toString(); });
    child.on("error", () => resolve(""));
    child.on("close", () => resolve(stdout.trim()));
    child.stdin.end(JSON.stringify(payload));
  });
}

function applyPermissionDecision(stdout, output) {
  if (!stdout) return;
  try {
    const decision = JSON.parse(stdout);
    if (decision.status === "allow" || decision.status === "deny" || decision.status === "ask") output.status = decision.status;
  } catch {}
}

export const LidGuardOpenCodePlugin = async ({ directory, worktree }) => ({
  "chat.message": async (input, output) => {
    await runHook("chat.message", {
      ...createBasePayload("chat.message", directory, worktree),
      sessionID: input.sessionID || "",
      messageID: input.messageID || "",
      prompt: collectText(output.parts),
      agent: input.agent || ""
    });
  },

  "permission.ask": async (input, output) => {
    const stdout = await runHook("permission.ask", {
      ...createBasePayload("permission.ask", directory, worktree),
      sessionID: input.sessionID || "",
      messageID: input.messageID || "",
      callID: input.callID || "",
      permission: input.type || "",
      patterns: input.pattern || []
    });
    applyPermissionDecision(stdout, output);
  },

  "tool.execute.before": async (input, output) => {
    await runHook("tool.execute.before", {
      ...createBasePayload("tool.execute.before", directory, worktree),
      sessionID: input.sessionID || "",
      callID: input.callID || "",
      toolName: input.tool || "",
      toolInput: output.args || {}
    });
  },

  "tool.execute.after": async (input, output) => {
    await runHook("tool.execute.after", {
      ...createBasePayload("tool.execute.after", directory, worktree),
      sessionID: input.sessionID || "",
      callID: input.callID || "",
      toolName: input.tool || "",
      toolInput: input.args || {},
      toolOutput: output.output || ""
    });
  },

  event: async ({ event }) => {
    if (!event || !trackedEventTypes.has(event.type)) return;
    await runHook(event.type, {
      ...createBasePayload(event.type, directory, worktree),
      sessionID: extractSessionID(event),
      sessionStatus: extractSessionStatus(event),
      event
    });
  }
});

export default LidGuardOpenCodePlugin;
// <LidGuard OpenCode plugin end>
