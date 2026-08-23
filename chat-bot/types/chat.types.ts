export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  isLoading?: boolean;
  /** Renders the message as a failed turn with a retry action. */
  isError?: boolean;
  /** The user message to send again when the retry action is used. */
  retryPrompt?: string;
  feedback?: "positive" | "negative";
}