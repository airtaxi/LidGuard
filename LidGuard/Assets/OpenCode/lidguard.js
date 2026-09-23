// <LidGuard OpenCode plugin start>
// LidGuard OpenCode plugin version: 2
import { spawn } from "node:child_process";

const lidGuardHookCommand = __LIDGUARD_HOOK_COMMAND_JSON__;

const v1TrackedEventTypes = new Set([
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
  "session.idle"
]);

const v2TrackedEventTypes = new Set([
  "session.text.ended",
  "permission.asked",
  "permission.replied",
  "form.created",
  "form.replied",
  "form.cancelled",
  "session.deleted",
  "session.execution.succeeded",
  "session.execution.failed",
  "session.execution.interrupted"
]);

const stopEventTypes = new Set([
  "session.idle",
  "session.deleted",
  "session.error",
  "session.execution.succeeded",
  "session.execution.failed",
  "session.execution.interrupted"
]);

const lastAssistantMessageBySession = new Map();
const continuedSessionIDs = new Set();
const stopInFlightSessionIDs = new Set();

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

function parsePermissionDecision(stdout) {
  if (!stdout) return null;
  try {
    const decision = JSON.parse(stdout);
    if (decision.status !== "allow" && decision.status !== "deny" && decision.status !== "ask") return null;
    return {
      status: decision.status,
      message: typeof decision.message === "string" ? decision.message : ""
    };
  } catch {
    return null;
  }
}

function parseStopContinuationPrompt(stdout) {
  if (!stdout) return "";
  try {
    const decision = JSON.parse(stdout);
    if (decision?.decision === "block" && typeof decision.reason === "string") return decision.reason.trim();
  } catch {}
  return "";
}

function normalizeSessionIdentifier(value) {
  if (typeof value !== "string") return "";
  const normalizedValue = value.trim();
  if (normalizedValue.length === 0 || normalizedValue === "global") return "";
  return normalizedValue;
}

function extractPromptText(prompt) {
  if (!prompt || typeof prompt !== "object") return "";
  return typeof prompt.text === "string" ? prompt.text : "";
}

function extractToolResultText(event) {
  if (event?.status === "error") {
    const errorMessage = event.error?.message;
    return typeof errorMessage === "string" ? errorMessage : "";
  }

  const content = event?.result?.content;
  if (typeof content === "string") return content;
  if (!Array.isArray(content)) return "";
  return content
    .filter((part) => part && part.type === "text" && typeof part.text === "string")
    .map((part) => part.text)
    .join("\n");
}

function isContinuableStopEvent(eventName) {
  return eventName === "session.idle" || eventName === "session.execution.succeeded";
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

function resolveEventDirectory(event) {
  const location = event?.location;
  return typeof location?.directory === "string" ? location.directory : "";
}

function isLocationEvent(event, directory) {
  const eventDirectory = resolveEventDirectory(event);
  return eventDirectory.length > 0 && directory.length > 0 && eventDirectory === directory;
}

async function processStopEvent(options) {
  const { stopEventName, sessionID, workingDirectory, worktree, sessionStatus, rawEvent, sendContinuationPrompt, logMessage } = options;
  const canContinue = isContinuableStopEvent(stopEventName);

  if (canContinue && sessionID) {
    if (stopInFlightSessionIDs.has(sessionID)) return;
    stopInFlightSessionIDs.add(sessionID);
  }

  const payload = {
    ...createBasePayload(stopEventName, workingDirectory, worktree),
    sessionID,
    sessionStatus: sessionStatus || "",
    event: rawEvent,
    lastAssistantMessage: lastAssistantMessageBySession.get(sessionID) || ""
  };
  if (canContinue) payload.stopHookActive = continuedSessionIDs.has(sessionID);

  let stdout = "";
  try {
    stdout = await runHook(stopEventName, payload);
  } finally {
    if (canContinue && sessionID) stopInFlightSessionIDs.delete(sessionID);
  }

  lastAssistantMessageBySession.delete(sessionID);

  if (!canContinue) {
    if (sessionID) continuedSessionIDs.delete(sessionID);
    return;
  }

  const stopContinuationPrompt = parseStopContinuationPrompt(stdout);
  if (!stopContinuationPrompt) {
    if (sessionID) continuedSessionIDs.delete(sessionID);
    return;
  }

  if (sessionID) continuedSessionIDs.add(sessionID);
  try {
    const promptSent = await sendContinuationPrompt(sessionID, stopContinuationPrompt);
    if (promptSent) return;

    if (sessionID) continuedSessionIDs.delete(sessionID);
    await logMessage("LidGuard could not send the ask-before-sleep reply because the OpenCode session prompt API is unavailable.");
    await runHook("session.error", { ...createBasePayload("session.error", workingDirectory, worktree), sessionID, sessionStatus: "", event: rawEvent, lastAssistantMessage: payload.lastAssistantMessage });
  } catch (error) {
    if (sessionID) continuedSessionIDs.delete(sessionID);
    await logMessage(`LidGuard could not send the ask-before-sleep reply to OpenCode: ${error?.message || error}`);
    await runHook("session.error", { ...createBasePayload("session.error", workingDirectory, worktree), sessionID, sessionStatus: "", event: rawEvent, lastAssistantMessage: payload.lastAssistantMessage });
  }
}

async function sendV1StopContinuationPrompt(client, sessionID, prompt) {
  if (!sessionID || !prompt || typeof client?.session?.prompt !== "function") return false;
  await client.session.prompt({
    path: { id: sessionID },
    body: {
      parts: [
        {
          type: "text",
          text: prompt
        }
      ]
    }
  });
  return true;
}

async function logV1PluginMessage(client, message) {
  try {
    if (typeof client?.app?.log === "function") await client.app.log({ body: { level: "warn", message } });
  } catch {}
}

function createV1Hooks(client, directory, worktree) {
  return {
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
      const decision = parsePermissionDecision(stdout);
      if (decision) output.status = decision.status;
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
      if (!event || !v1TrackedEventTypes.has(event.type)) return;

      const sessionID = extractSessionID(event);

      if (event.type === "message.part.updated") {
        const text = extractPartText(event);
        if (text.length > 0 && sessionID) lastAssistantMessageBySession.set(sessionID, text);
        return;
      }

      if (stopEventTypes.has(event.type)) {
        await processStopEvent({
          stopEventName: event.type,
          sessionID,
          workingDirectory: directory,
          worktree,
          sessionStatus: extractSessionStatus(event),
          rawEvent: event,
          sendContinuationPrompt: (targetSessionID, prompt) => sendV1StopContinuationPrompt(client, targetSessionID, prompt),
          logMessage: (message) => logV1PluginMessage(client, message)
        });
        return;
      }

      await runHook(event.type, {
        ...createBasePayload(event.type, directory, worktree),
        sessionID,
        sessionStatus: extractSessionStatus(event),
        event
      });
    }
  };
}

async function sendV2StopContinuationPrompt(ctx, sessionID, prompt) {
  if (!sessionID || !prompt || typeof ctx?.session?.prompt !== "function") return false;
  await ctx.session.prompt({ sessionID, text: prompt });
  return true;
}

function logV2PluginMessage(message) {
  try {
    console.warn(`LidGuard OpenCode plugin: ${message}`);
  } catch {}
}

async function handleV2Event(ctx, directory, event) {
  const eventName = typeof event?.type === "string" ? event.type : "";
  if (!v2TrackedEventTypes.has(eventName) || !isLocationEvent(event, directory)) return;

  const eventData = event?.data || {};

  if (eventName === "session.text.ended") {
    const sessionID = normalizeSessionIdentifier(eventData.sessionID);
    const text = typeof eventData.text === "string" ? eventData.text.trim() : "";
    if (sessionID && text.length > 0) lastAssistantMessageBySession.set(sessionID, text);
    return;
  }

  if (eventName === "session.execution.interrupted" && eventData.reason === "shutdown") return;

  const sessionID = normalizeSessionIdentifier(eventName === "form.created" ? eventData.form?.sessionID : eventData.sessionID);
  if (!sessionID) return;

  if (stopEventTypes.has(eventName)) {
    await processStopEvent({
      stopEventName: eventName,
      sessionID,
      workingDirectory: directory,
      worktree: "",
      sessionStatus: "",
      rawEvent: event,
      sendContinuationPrompt: (targetSessionID, prompt) => sendV2StopContinuationPrompt(ctx, targetSessionID, prompt),
      logMessage: logV2PluginMessage
    });
    return;
  }

  await runHook(eventName, {
    ...createBasePayload(eventName, directory, ""),
    sessionID,
    sessionStatus: "",
    event
  });
}

async function subscribeV2Events(ctx, directory, signal) {
  while (!signal.aborted) {
    try {
      for await (const event of ctx.event.subscribe({ signal })) await handleV2Event(ctx, directory, event);
    } catch (error) {
      if (signal.aborted) return;
      logV2PluginMessage(`LidGuard OpenCode event subscription ended unexpectedly: ${error?.message || error}`);
    }

    if (signal.aborted) return;
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
}

const lidGuardOpenCodePlugin = {
  id: "lidguard",

  async setup(ctx) {
    const directory = typeof ctx?.location?.directory === "string" ? ctx.location.directory : "";
    const abortController = new AbortController();

    await ctx.session.hook("prompt", async (event) => {
      await runHook("chat.message", {
        ...createBasePayload("chat.message", directory, ""),
        sessionID: normalizeSessionIdentifier(event?.sessionID),
        messageID: typeof event?.messageID === "string" ? event.messageID : "",
        prompt: extractPromptText(event?.prompt),
        agent: ""
      });
    });

    await ctx.permission.hook("evaluate", async (event) => {
      const stdout = await runHook("permission.ask", {
        ...createBasePayload("permission.ask", directory, ""),
        sessionID: normalizeSessionIdentifier(event?.sessionID),
        messageID: typeof event?.source?.messageID === "string" ? event.source.messageID : "",
        callID: typeof event?.source?.id === "string" ? event.source.id : "",
        permission: typeof event?.action === "string" ? event.action : "",
        patterns: Array.isArray(event?.resources) ? event.resources : []
      });
      const decision = parsePermissionDecision(stdout);
      if (!decision) return;
      event.effect = decision.status;
      if (decision.message.length > 0) event.message = decision.message;
    });

    await ctx.tool.hook("execute.before", async (event) => {
      await runHook("tool.execute.before", {
        ...createBasePayload("tool.execute.before", directory, ""),
        sessionID: normalizeSessionIdentifier(event?.sessionID),
        callID: typeof event?.id === "string" ? event.id : "",
        toolName: typeof event?.tool === "string" ? event.tool : "",
        toolInput: event?.input ?? {}
      });
    });

    await ctx.tool.hook("execute.after", async (event) => {
      await runHook("tool.execute.after", {
        ...createBasePayload("tool.execute.after", directory, ""),
        sessionID: normalizeSessionIdentifier(event?.sessionID),
        callID: typeof event?.id === "string" ? event.id : "",
        toolName: typeof event?.tool === "string" ? event.tool : "",
        toolInput: event?.input ?? {},
        toolOutput: extractToolResultText(event)
      });
    });

    void subscribeV2Events(ctx, directory, abortController.signal);

    return () => abortController.abort();
  },

  async server(input) {
    return createV1Hooks(input?.client, input?.directory || "", input?.worktree || "");
  }
};

export const LidGuardOpenCodePlugin = lidGuardOpenCodePlugin;
export default lidGuardOpenCodePlugin;
// <LidGuard OpenCode plugin end>
