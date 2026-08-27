export type ChatCardKind = "course" | "profile" | "collection" | "division";

export interface ChatFact {
  label: string;
  value: string;
}

export interface ChatCard {
  kind: ChatCardKind;
  id?: string;
  title: string;
  description?: string;
  url?: string;
  facts: ChatFact[];
  chips: string[];
}

export interface ChatTurnResponse {
  /** Legacy marker string — kept for fallback parsing. */
  answer?: string;
  messageId?: string;
  prose?: string;
  cards?: ChatCard[];
  suggestions?: string[];
  intent?: string;
  language?: string;
  conversationId?: string;
  turnId?: string;
  tools?: string[];
  cardIds?: string[];
  totalElapsedMs?: number;
}

export type ChatStreamEventType =
  | "status"
  | "prose"
  | "card"
  | "suggestions"
  | "done"
  | "error";

export interface ChatStreamEvent {
  type: ChatStreamEventType;
  message?: string;
  prose?: string;
  card?: ChatCard;
  suggestions?: string[];
  result?: ChatTurnResponse;
}

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  /** Structured cards from the backend (preferred over marker parsing). */
  cards?: ChatCard[];
  suggestions?: string[];
  status?: string;
  isLoading?: boolean;
  isError?: boolean;
  retryPrompt?: string;
  feedback?: "positive" | "negative";
  /** Telemetry fields for feedback persistence / admin review. */
  turnId?: string;
  conversationId?: string;
  intent?: string;
  language?: string;
  cardIds?: string[];
}
