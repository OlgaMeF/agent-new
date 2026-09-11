import { useEffect, useRef, useState } from "react";

import { ChatButton } from "./components/ChatButton";
import { ChatWindow } from "./components/ChatWindow";
import { FeedbackReason, sendChatFeedback, streamChatMessage } from "./types/chatApi";
import { ChatCard, ChatMessage } from "./types/chat.types";

const IS_ENGLISH =
  typeof navigator !== "undefined" && navigator.language.toLowerCase().startsWith("en");

const GREETING = IS_ENGLISH
  ? "Hello 👋 I am The Learning Matchmaker. I connect you with relevant courses, skills, learning profiles and learning paths."
  : "Hallo 👋 Ich bin The Learning Matchmaker. Ich verbinde dich mit passenden Kursen, Skills, Lernprofilen und Lernpfaden.";

const NO_ANSWER_TEXT = IS_ENGLISH
  ? "I did not receive an answer. Please try again."
  : "Ich habe dazu gerade keine Antwort erhalten. Bitte versuche es erneut.";

const REQUEST_FAILED_TEXT = IS_ENGLISH
  ? "I could not load an answer right now. Please try again."
  : "Ich konnte gerade keine Antwort laden. Bitte versuche es erneut.";

const statusLabel = (status?: string): string => {
  switch (status) {
    case "routing":
      return IS_ENGLISH ? "Understanding your question…" : "Frage wird verstanden…";
    case "fetching":
      return IS_ENGLISH ? "Searching the learning catalogue…" : "Lernkatalog wird durchsucht…";
    case "writing":
      return IS_ENGLISH ? "Writing the answer…" : "Antwort wird formuliert…";
    default:
      return IS_ENGLISH
        ? "The Learning Matchmaker is thinking..."
        : "The Learning Matchmaker denkt...";
  }
};

const createMessageId = (): string =>
  typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`;

const serializeStructuredContent = (
  prose: string,
  cards: ChatCard[],
  suggestions: string[],
): string => {
  // Keep a marker string so older render paths and copy/paste still work.
  const parts = [prose.trim()];

  for (const card of cards) {
    const tag =
      card.kind === "profile"
        ? "PROFILE_CARD"
        : card.kind === "collection"
          ? "COLLECTION_CARD"
          : card.kind === "division"
            ? "DIVISION_CARD"
            : "COURSE_CARD";

    parts.push("");
    parts.push(`[${tag}]`);
    parts.push(`TITLE: ${card.title}`);
    if (card.description) {
      parts.push(`DESCRIPTION: ${card.description}`);
    }
    for (const fact of card.facts ?? []) {
      const key =
        fact.label === "Plattform"
          ? "PLATFORM"
          : fact.label === "Dauer"
            ? "DURATION"
            : fact.label === "Bereich"
              ? "DIVISION"
              : fact.label === "Inhalte"
                ? "ITEMS"
                : fact.label.toUpperCase();
      parts.push(`${key}: ${fact.value}`);
    }
    if (card.chips?.length) {
      parts.push(`CHILDREN: ${card.chips.join(" | ")}`);
    }
    if (card.url) {
      parts.push(`URL: ${card.url}`);
    }
    parts.push(`[/${tag}]`);
  }

  if (suggestions.length > 0) {
    parts.push("");
    parts.push("[SUGGESTIONS]");
    parts.push(...suggestions);
    parts.push("[/SUGGESTIONS]");
  }

  return parts.join("\n");
};

export const ChatBot = () => {
  const [open, setOpen] = useState(false);

  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: "greeting",
      role: "assistant",
      content: GREETING,
    },
  ]);

  const requestRef = useRef<AbortController | null>(null);

  useEffect(
    () => () => {
      requestRef.current?.abort();
    },
    [],
  );

  const sendMessage = async (message: string) => {
    const trimmedMessage = message.trim();

    if (!trimmedMessage) {
      return;
    }

    const loadingMessageId = createMessageId();

    const patchLoading = (patch: Partial<ChatMessage>) => {
      setMessages((prev) =>
        prev.map((item) =>
          item.id === loadingMessageId ? { ...item, ...patch } : item,
        ),
      );
    };

    const dropLoading = () => {
      setMessages((prev) => prev.filter((item) => item.id !== loadingMessageId));
    };

    setMessages((prev) => [
      ...prev,
      {
        id: createMessageId(),
        role: "user",
        content: trimmedMessage,
      },
      {
        id: loadingMessageId,
        role: "assistant",
        content: statusLabel("routing"),
        isLoading: true,
        status: "routing",
        cards: [],
        suggestions: [],
      },
    ]);

    requestRef.current?.abort();

    const controller = new AbortController();
    requestRef.current = controller;

    try {
      const collectedCards: ChatCard[] = [];
      let collectedProse = "";
      let collectedSuggestions: string[] = [];

      const turn = await streamChatMessage(
        trimmedMessage,
        (evt) => {
          if (evt.type === "status" && evt.message) {
            patchLoading({
              status: evt.message,
              content: statusLabel(evt.message),
              isLoading: true,
            });
            return;
          }

          if (evt.type === "prose" && evt.prose) {
            collectedProse = evt.prose;
            patchLoading({
              content: collectedProse,
              isLoading: true,
              status: "writing",
            });
            return;
          }

          if (evt.type === "card" && evt.card) {
            collectedCards.push(evt.card);
            patchLoading({
              cards: [...collectedCards],
              content: serializeStructuredContent(
                collectedProse,
                collectedCards,
                collectedSuggestions,
              ),
              isLoading: true,
            });
            return;
          }

          if (evt.type === "suggestions" && evt.suggestions) {
            collectedSuggestions = evt.suggestions;
            patchLoading({
              suggestions: collectedSuggestions,
              content: serializeStructuredContent(
                collectedProse,
                collectedCards,
                collectedSuggestions,
              ),
              isLoading: true,
            });
          }
        },
        controller.signal,
      );

      const prose =
        (typeof turn?.prose === "string" && turn.prose.trim()) ||
        collectedProse ||
        (typeof turn?.answer === "string" ? turn.answer : "");

      const cards = turn?.cards?.length ? turn.cards : collectedCards;
      const suggestions = turn?.suggestions?.length
        ? turn.suggestions
        : collectedSuggestions;

      if (prose.trim().length > 0 || cards.length > 0) {
        patchLoading({
          role: "assistant",
          content:
            cards.length > 0
              ? serializeStructuredContent(prose, cards, suggestions)
              : prose,
          cards,
          suggestions,
          isLoading: false,
          status: undefined,
          id: turn?.messageId ?? loadingMessageId,
          turnId: turn?.turnId,
          conversationId: turn?.conversationId,
          intent: turn?.intent,
          language: turn?.language,
          cardIds:
            turn?.cardIds ??
            cards.map((c) => c.id).filter((id): id is string => Boolean(id)),
        });
      } else {
        patchLoading({
          role: "assistant",
          content: NO_ANSWER_TEXT,
          isError: true,
          isLoading: false,
          retryPrompt: trimmedMessage,
        });
      }
    } catch {
      if (controller.signal.aborted) {
        dropLoading();
        return;
      }

      patchLoading({
        role: "assistant",
        content: REQUEST_FAILED_TEXT,
        isError: true,
        isLoading: false,
        retryPrompt: trimmedMessage,
      });
    } finally {
      if (requestRef.current === controller) {
        requestRef.current = null;
      }
    }
  };

  const rateMessage = async (
    messageId: string,
    rating: "positive" | "negative",
    reason?: FeedbackReason,
  ) => {
    const target = messages.find((item) => item.id === messageId);

    setMessages((prev) =>
      prev.map((item) =>
        item.id === messageId ? { ...item, feedback: rating } : item,
      ),
    );

    try {
      await sendChatFeedback({
        messageId,
        rating,
        reason,
        userMessage: messages[messages.findIndex((m) => m.id === messageId) - 1]?.content,
        assistantResponse: target?.content,
      });
    } catch {
      // Feedback is optional and must never interrupt the chat flow.
    }
  };

  return (
    <>
      {!open && <ChatButton onClick={() => setOpen(true)} />}

      <ChatWindow
        open={open}
        messages={messages}
        onSend={sendMessage}
        onRateMessage={rateMessage}
        onClose={() => setOpen(false)}
      />
    </>
  );
};
