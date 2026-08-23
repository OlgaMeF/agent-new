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

export async function sendChatMessage(
  message: string,
  signal?: AbortSignal,
): Promise<string> {
  const response = await fetch("/api/chat", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "x-chat-session-id": resolveSessionId(),
    },
    body: JSON.stringify({
      message,
    }),
    signal,
  });

  const data = await response.json();

  return data.answer;
}

export type FeedbackReason =
  | "wrong_result"
  | "wrong_context"
  | "missing_data"
  | "unclear"
  | "other";

export async function sendChatFeedback(
  messageId: string,
  rating: "positive" | "negative",
  reason?: FeedbackReason,
): Promise<void> {
  await fetch("/api/chat/feedback", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ messageId, rating, reason }),
  });
}
