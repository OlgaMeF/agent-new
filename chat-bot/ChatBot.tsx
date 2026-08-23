import { useEffect, useRef, useState } from "react";

import { ChatButton } from "./components/ChatButton";
import { ChatWindow } from "./components/ChatWindow";
import { sendChatFeedback, sendChatMessage } from "./services/chatApi";
import { ChatMessage } from "./types/chat.types";

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

    const loadingMessageId = crypto.randomUUID();

    const replaceLoading = (replacement: Omit<ChatMessage, "id">) => {
      setMessages((prev) => [
        ...prev.filter((item) => item.id !== loadingMessageId),
        { id: crypto.randomUUID(), ...replacement },
      ]);
    };

    setMessages((prev) => [
      ...prev,
      {
        id: crypto.randomUUID(),
        role: "user",
        content: trimmedMessage,
      },
      {
        id: loadingMessageId,
        role: "assistant",
        content: IS_ENGLISH
          ? "The Learning Matchmaker is thinking..."
          : "The Learning Matchmaker denkt...",
        isLoading: true,
      },
    ]);

    requestRef.current?.abort();

    const controller = new AbortController();
    requestRef.current = controller;

    try {
      const answer = await sendChatMessage(trimmedMessage, controller.signal);

      // A disabled chatbot answers 503 with { message }, so answer can be
      // undefined even though the request itself succeeded.
      if (typeof answer === "string" && answer.trim().length > 0) {
        replaceLoading({ role: "assistant", content: answer });
      } else {
        replaceLoading({
          role: "assistant",
          content: NO_ANSWER_TEXT,
          isError: true,
          retryPrompt: trimmedMessage,
        });
      }
    } catch {
      if (controller.signal.aborted) {
        return;
      }

      replaceLoading({
        role: "assistant",
        content: REQUEST_FAILED_TEXT,
        isError: true,
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
  ) => {
    setMessages((prev) =>
      prev.map((item) =>
        item.id === messageId ? { ...item, feedback: rating } : item,
      ),
    );

    try {
      await sendChatFeedback(messageId, rating);
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
