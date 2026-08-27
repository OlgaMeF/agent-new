import type {
  ChatStreamEvent,
  ChatTurnResponse,
} from "./chat.types";

const SESSION_STORAGE_KEY = "chat-session-id";

/**
 * The backend combines this id with the signed-in user to key its conversation
 * memory. Without it a user's browser tabs would share one follow-up context.
 */
const resolveSessionId = (): string => {
  try {
    const existing = sessionStorage.getItem(SESSION_STORAGE_KEY);

    if (existing) {
      return existing;
    }

    const created =
      typeof crypto !== "undefined" && "randomUUID" in crypto
        ? crypto.randomUUID()
        : `${Date.now()}-${Math.random().toString(36).slice(2)}`;

    sessionStorage.setItem(SESSION_STORAGE_KEY, created);

    return created;
  } catch {
    return "default";
  }
};

const sessionHeaders = (): HeadersInit => ({
  "Content-Type": "application/json",
  "x-chat-session-id": resolveSessionId(),
});

/** Normalizes API card payloads (PascalCase or camelCase). */
const normalizeTurn = (data: Record<string, unknown> | null): ChatTurnResponse | undefined => {
  if (!data) {
    return undefined;
  }

  const prose =
    typeof data.prose === "string"
      ? data.prose
      : typeof data.Prose === "string"
        ? data.Prose
        : undefined;

  const answer =
    typeof data.answer === "string"
      ? data.answer
      : typeof data.AnswerLegacy === "string"
        ? data.AnswerLegacy
        : undefined;

  const cardsRaw = (data.cards ?? data.Cards) as unknown;
  const suggestionsRaw = (data.suggestions ?? data.Suggestions) as unknown;

  return {
    answer,
    prose,
    messageId:
      typeof data.messageId === "string"
        ? data.messageId
        : typeof data.MessageId === "string"
          ? data.MessageId
          : undefined,
    cards: Array.isArray(cardsRaw) ? (cardsRaw as ChatTurnResponse["cards"]) : undefined,
    suggestions: Array.isArray(suggestionsRaw)
      ? (suggestionsRaw as string[])
      : undefined,
    intent: typeof data.intent === "string" ? data.intent : undefined,
    language: typeof data.language === "string" ? data.language : undefined,
    conversationId:
      typeof data.conversationId === "string" ? data.conversationId : undefined,
    turnId: typeof data.turnId === "string" ? data.turnId : undefined,
    tools: Array.isArray(data.tools) ? (data.tools as string[]) : undefined,
    cardIds: Array.isArray(data.cardIds) ? (data.cardIds as string[]) : undefined,
    totalElapsedMs:
      typeof data.totalElapsedMs === "number" ? data.totalElapsedMs : undefined,
  };
};

/**
 * Preferred path: structured JSON turn (prose + cards + suggestions).
 * Falls back to legacy `answer` marker string when cards are absent.
 */
export async function sendChatMessage(
  message: string,
  signal?: AbortSignal,
): Promise<ChatTurnResponse | undefined> {
  const response = await fetch("/api/chat", {
    method: "POST",
    headers: sessionHeaders(),
    body: JSON.stringify({ message }),
    signal,
  });

  const data = (await response.json().catch(() => null)) as Record<
    string,
    unknown
  > | null;

  return normalizeTurn(data);
}

/**
 * SSE stream against POST /api/chat/stream.
 * Emits status / prose / card / suggestions / done | error.
 */
export async function streamChatMessage(
  message: string,
  onEvent: (event: ChatStreamEvent) => void,
  signal?: AbortSignal,
): Promise<ChatTurnResponse | undefined> {
  const response = await fetch("/api/chat/stream", {
    method: "POST",
    headers: {
      ...sessionHeaders(),
      Accept: "text/event-stream",
    },
    body: JSON.stringify({ message }),
    signal,
  });

  if (!response.ok || !response.body) {
    // Fall back to non-streaming turn.
    return sendChatMessage(message, signal);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let finalResult: ChatTurnResponse | undefined;
  let eventName = "message";

  const flushBlock = (block: string) => {
    const lines = block.split(/\r?\n/);
    let dataLine = "";

    for (const line of lines) {
      if (line.startsWith("event:")) {
        eventName = line.slice(6).trim();
      } else if (line.startsWith("data:")) {
        dataLine += line.slice(5).trim();
      }
    }

    if (!dataLine) {
      return;
    }

    try {
      const parsed = JSON.parse(dataLine) as Record<string, unknown>;
      const type = (parsed.type as string) || eventName;

      const evt: ChatStreamEvent = {
        type: type as ChatStreamEvent["type"],
        message: typeof parsed.message === "string" ? parsed.message : undefined,
        prose: typeof parsed.prose === "string" ? parsed.prose : undefined,
        card: parsed.card as ChatStreamEvent["card"],
        suggestions: Array.isArray(parsed.suggestions)
          ? (parsed.suggestions as string[])
          : undefined,
        result: parsed.result
          ? normalizeTurn(parsed.result as Record<string, unknown>)
          : undefined,
      };

      onEvent(evt);

      if (evt.type === "done" || evt.type === "error") {
        finalResult = evt.result;
      }
    } catch {
      // Ignore malformed SSE chunks.
    }

    eventName = "message";
  };

  while (true) {
    const { done, value } = await reader.read();

    if (done) {
      break;
    }

    buffer += decoder.decode(value, { stream: true });

    let separator = buffer.indexOf("\n\n");

    while (separator >= 0) {
      const block = buffer.slice(0, separator);
      buffer = buffer.slice(separator + 2);
      flushBlock(block);
      separator = buffer.indexOf("\n\n");
    }
  }

  if (buffer.trim().length > 0) {
    flushBlock(buffer);
  }

  return finalResult;
}

export type FeedbackReason =
  | "wrong_result"
  | "wrong_context"
  | "missing_data"
  | "unclear"
  | "other";

export interface ChatFeedbackPayload {
  messageId: string;
  rating: "positive" | "negative";
  reason?: FeedbackReason;
  userMessage?: string;
  assistantResponse?: string;
}

export async function sendChatFeedback(
  payload: ChatFeedbackPayload,
): Promise<void> {
  await fetch("/api/chat/feedback", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
}
