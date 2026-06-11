// <LidGuard OpenCode plugin start>
// LidGuard OpenCode plugin version: 1
import { spawn } from "node:child_process";

const lidGuardHookCommand = __LIDGUARD_HOOK_COMMAND_JSON__;
const trackedEventTypes = new Set([
  "message.part.updated",
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

const lastAssistantMessageBySession = new Map();

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
  const part = properties.part || {};
  return properties.sessionID || properties.sessionId || part.sessionID || part.sessionId || properties.info?.id || "";
}

function extractPartText(event) {
  const part = event?.properties?.part;
  if (!part || part.type !== "text" || typeof part.text !== "string") return "";
  return part.text.trim();
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

    const sessionID = extractSessionID(event);

    if (event.type === "message.part.updated") {
      const text = extractPartText(event);
      if (text.length > 0 && sessionID) lastAssistantMessageBySession.set(sessionID, text);
      return;
    }

    const isIdleStatus = event.type === "session.status" && extractSessionStatus(event) === "idle";
    const isStopEvent = event.type === "session.idle" || event.type === "session.deleted" || event.type === "session.error" || isIdleStatus;

    const payload = {
      ...createBasePayload(event.type, directory, worktree),
      sessionID,
      sessionStatus: extractSessionStatus(event),
      event
    };

    if (isStopEvent) payload.lastAssistantMessage = lastAssistantMessageBySession.get(sessionID) || "";

    await runHook(event.type, payload);

    if (isStopEvent) lastAssistantMessageBySession.delete(sessionID);
  }
});

export default LidGuardOpenCodePlugin;
// <LidGuard OpenCode plugin end>
