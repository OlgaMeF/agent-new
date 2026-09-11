import { ReactNode, useEffect, useMemo, useRef, useState } from "react";

import CloseIcon from "@mui/icons-material/Close";
import AutoAwesomeIcon from "@mui/icons-material/AutoAwesome";
import SchoolOutlinedIcon from "@mui/icons-material/SchoolOutlined";
import RefreshIcon from "@mui/icons-material/Refresh";
import SendIcon from "@mui/icons-material/Send";
import ThumbDownOutlinedIcon from "@mui/icons-material/ThumbDownOutlined";
import ThumbUpOutlinedIcon from "@mui/icons-material/ThumbUpOutlined";
import {
  Box,
  Button,
  Chip,
  Drawer,
  IconButton,
  Link,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";

import { FeedbackReason } from "../types/chatApi";
import { ChatMessage, ChatCard } from "../types/chat.types";

const IS_ENGLISH =
  typeof navigator !== "undefined" && navigator.language.toLowerCase().startsWith("en");

/** Soft slate / teal shell — calm learning UI, not purple “AI chrome”. */
const CHAT = {
  bg: "#F3F5F7",
  surface: "#FFFFFF",
  surfaceSoft: "#F8FAFC",

  primary: "#0078D6",
  primaryHover: "#0067B8",
  primarySoft: "#E8F3FC",

  bubbleUser: "#0078D6",
  bubbleAssistant: "#FFFFFF",

  ink: "rgba(0, 0, 0, 0.87)",
  muted: "rgba(0, 0, 0, 0.60)",
  disabled: "rgba(0, 0, 0, 0.38)",

  line: "#DDE3EA",
  composerBg: "#FFFFFF",
  error: "#D32F2F",
  success: "#238636",
} as const;

type Props = {
  open: boolean;
  onClose: () => void;
  messages: ChatMessage[];
  onSend: (message: string) => void;
  onRateMessage: (
    messageId: string,
    rating: "positive" | "negative",
    reason?: FeedbackReason,
  ) => void;
};

export const ChatWindow = ({
  open,
  onClose,
  messages,
  onSend,
  onRateMessage,
}: Props) => {
  const [message, setMessage] = useState("");
  const bottomRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const isBusy = useMemo(
    () => messages.some((item) => item.isLoading),
    [messages],
  );

  useEffect(() => {
    bottomRef.current?.scrollIntoView({
      behavior: "smooth",
      block: "end",
    });
  }, [messages, isBusy, open]);

  useEffect(() => {
    if (open && !isBusy) {
      inputRef.current?.focus();
    }
  }, [open, isBusy]);

  const submit = (text: string) => {
    const trimmed = text.trim();

    if (!trimmed || isBusy) {
      return;
    }

    onSend(trimmed);
    setMessage("");
  };

  const canSend = !isBusy && message.trim().length > 0;

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      aria-label="The Learning Matchmaker"
      PaperProps={{
        sx: {
          width: { xs: "100vw", sm: 440 },
          maxWidth: "100vw",
          overflow: "hidden",
          borderLeft: `1px solid ${CHAT.line}`,
          background:
            "linear-gradient(180deg, #F8FAFC 0%, #F3F5F7 55%, #EEF2F6 100%)",
          boxShadow: "-12px 0 40px rgba(15, 23, 42, 0.12)",
        },
      }}
    >
      <Box
        sx={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          position: "relative",

          "@keyframes thinking-line": {
            "0%": {
              backgroundPosition: "200% 0",
            },
            "100%": {
              backgroundPosition: "-200% 0",
            },
          },

          "@keyframes thinking-glow": {
            "0%, 100%": {
              boxShadow:
                "0 8px 28px rgba(15, 23, 42, 0.08), 0 0 0 rgba(0, 120, 214, 0)",
            },
            "50%": {
              boxShadow:
                "0 10px 34px rgba(15, 23, 42, 0.10), 0 0 26px rgba(0, 120, 214, 0.24)",
            },
          },

          "@keyframes thinking-pulse": {
            "0%, 100%": {
              opacity: 0.55,
              transform: "scale(0.9)",
            },
            "50%": {
              opacity: 1,
              transform: "scale(1)",
            },
          },
        }}
      >
        {/* Header */}
        <Box
          display="flex"
          justifyContent="space-between"
          alignItems="center"
          sx={{
            px: 2,
            py: 1.5,
            flexShrink: 0,
            backgroundColor: CHAT.surface,
            borderBottom: `1px solid ${CHAT.line}`,
            boxShadow: "0 1px 8px rgba(15, 23, 42, 0.04)",
            zIndex: 2,
          }}
        >
          <Box display="flex" alignItems="center" gap={1.25}>
            <Box
              sx={{
                width: 40,
                height: 40,
                display: "grid",
                placeItems: "center",
                flexShrink: 0,
                borderRadius: "12px",
                color: "#FFFFFF",
                background:
                  "linear-gradient(145deg, #0078D6 0%, #005EA8 100%)",
                boxShadow: "0 6px 18px rgba(0, 120, 214, 0.24)",
              }}
            >
              <SchoolOutlinedIcon
                sx={{
                  fontSize: 20,
                  color: "#FFFFFF"
                }}
              />

            </Box>

            <Box>
              <Typography
                variant="subtitle1"
                component="h2"
                sx={{
                  fontWeight: 700,
                  lineHeight: 1.2,
                  color: CHAT.ink,
                  letterSpacing: "-0.01em",
                }}
              >
                The Learning Matchmaker
              </Typography>

              <Typography
                variant="caption"
                sx={{
                  color: CHAT.muted,
                  display: "flex",
                  alignItems: "center",
                  gap: 0.7,
                  mt: 0.25,
                }}
              >
                <Box
                  component="span"
                  sx={{
                    width: 7,
                    height: 7,
                    borderRadius: "50%",
                    display: "inline-block",
                    backgroundColor: isBusy
                      ? CHAT.primary
                      : CHAT.success,
                    boxShadow: isBusy
                      ? "0 0 9px rgba(0, 120, 214, 0.65)"
                      : "none",
                    animation: isBusy
                      ? "thinking-pulse 1.4s ease-in-out infinite"
                      : "none",
                  }}
                />

                {isBusy
                  ? IS_ENGLISH
                    ? "Working on your question…"
                    : "Arbeitet an deiner Frage…"
                  : IS_ENGLISH
                    ? "Ready when you are"
                    : "Bereit für deine Frage"}
              </Typography>
            </Box>
          </Box>

          <IconButton
            onClick={onClose}
            size="small"
            aria-label={IS_ENGLISH ? "Close chat" : "Chat schließen"}
            sx={{
              color: CHAT.muted,
              borderRadius: 2,
              "&:hover": {
                color: CHAT.ink,
                backgroundColor: "#F0F3F6",
              },
            }}
          >
            <CloseIcon fontSize="small" />
          </IconButton>
        </Box>

        {/* Messages */}
        <Box
          role="log"
          aria-live="polite"
          aria-relevant="additions text"
          sx={{
            flex: 1,
            minHeight: 0,
            overflowY: "auto",
            px: 1.75,
            py: 2,
            scrollBehavior: "smooth",

            "&::-webkit-scrollbar": {
              width: 6,
            },

            "&::-webkit-scrollbar-track": {
              backgroundColor: "transparent",
            },

            "&::-webkit-scrollbar-thumb": {
              borderRadius: 3,
              backgroundColor: "#C4CBD3",
            },

            "&::-webkit-scrollbar-thumb:hover": {
              backgroundColor: "#AAB3BD",
            },
          }}
        >
          {messages.length === 0 ? (
            <EmptyState />
          ) : (
            <Stack gap={1.75}>
              {messages.map((msg) => (
                <MessageRow
                  key={msg.id}
                  message={msg}
                  onSend={submit}
                  onRateMessage={onRateMessage}
                />
              ))}
            </Stack>
          )}

          <div ref={bottomRef} />
        </Box>

        {/* Composer area */}
        <Box
          sx={{
            px: 1.75,
            pb: 1.75,
            pt: 1.25,
            flexShrink: 0,
            background:
              "linear-gradient(180deg, rgba(243,245,247,0) 0%, #F3F5F7 24%, #F3F5F7 100%)",
          }}
        >
          <Box
            sx={{
              position: "relative",
              display: "flex",
              alignItems: "flex-end",
              gap: 1,
              p: 0.75,
              pl: 1.25,
              overflow: "hidden",
              borderRadius: 3,
              backgroundColor: CHAT.composerBg,
              border: isBusy
                ? `1px solid ${CHAT.primary}`
                : `1px solid ${CHAT.line}`,
              boxShadow: isBusy
                ? "0 10px 20px rgba(0, 0, 0, 0.1)"
                : "none",
            }}
          >
            <TextField
              inputRef={inputRef}
              fullWidth
              multiline
              maxRows={4}
              value={message}
              disabled={isBusy}
              onChange={(event) => setMessage(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter" && !event.shiftKey) {
                  event.preventDefault();
                  submit(message);
                }
              }}
              placeholder={
                IS_ENGLISH
                  ? "Ask about courses, skills or profiles…"
                  : "Frage zu Kursen, Skills oder Profilen…"
              }
              variant="standard"
              InputProps={{
                disableUnderline: true,
                sx: {
                  fontSize: "0.925rem",
                  lineHeight: 1.5,
                  color: CHAT.ink,
                  py: 0.75,
                },
              }}
              inputProps={{ "aria-label": IS_ENGLISH ? "Message" : "Nachricht eingeben" }}
            />

            <IconButton
              color="primary"
              onClick={() => submit(message)}
              disabled={!canSend}
              aria-label={IS_ENGLISH ? "Send" : "Senden"}
              sx={{
                mb: 0.15,
                width: 40,
                height: 40,
                backgroundColor: canSend ? CHAT.bubbleUser : "transparent",
                color: canSend ? "#fff" : CHAT.muted,
                "&:hover": {
                  backgroundColor: canSend ? "#0078D6" : "rgba(28, 42, 50, 0.06)",
                },
                "&.Mui-disabled": { color: "rgba(90, 107, 117, 0.35)" },
              }}
            >
              <SendIcon sx={{ fontSize: 18 }} />
            </IconButton>
          </Box>
        </Box>
      </Box>
    </Drawer>
  );
};

const EmptyState = () => (
  <Box
    sx={{
      mt: 1,
      p: 2,
      borderRadius: 3,
      backgroundColor: CHAT.bubbleAssistant,
      border: `1px solid ${CHAT.line}`,
    }}
  >
    <Typography variant="body2" sx={{ lineHeight: 1.65, color: CHAT.ink }}>
      {IS_ENGLISH
        ? "I help you find courses, skills, learning profiles and paths on this platform."
        : "Ich helfe dir, Kurse, Skills, Lernprofile und Lernpfade auf der Plattform zu finden."}
    </Typography>
  </Box>
);

const MessageRow = ({
  message,
  onSend,
  onRateMessage,
}: {
  message: ChatMessage;
  onSend: (value: string) => void;
  onRateMessage: (
    messageId: string,
    rating: "positive" | "negative",
    reason?: FeedbackReason,
  ) => void;
}) => {
  const isUser = message.role === "user";

  return (
    <Box
      display="flex"
      justifyContent={isUser ? "flex-end" : "flex-start"}
      alignItems="flex-end"
      gap={1}
      sx={{
        "@keyframes chat-msg-in": {
          from: { opacity: 0, transform: "translateY(6px)" },
          to: { opacity: 1, transform: "translateY(0)" },
        },
        animation: "chat-msg-in 0.28s ease-out",
      }}
    >
      {!isUser && (
        <Box
          sx={{
            width: 28,
            height: 28,
            flexShrink: 0,
            mb: 0.25,
            borderRadius: "9px",
            display: "grid",
            placeItems: "center",
            background: `linear-gradient(145deg, ${CHAT.primary}, #005EA8)`,
            boxShadow: "0 3px 10px rgba(249, 250, 250, 0.24)",
          }}
          aria-hidden
        >
          <AutoAwesomeIcon
            sx={{
              fontSize: 14,
              color: "#FFFFFF"
            }}
          />

        </Box>
      )}

      <Box sx={{ maxWidth: isUser ? "82%" : "88%", minWidth: 0 }}>
        <Box
          sx={{
            px: isUser ? 1.6 : 1.7,
            py: isUser ? 1.15 : 1.35,
            borderRadius: isUser ? "18px 18px 6px 18px" : "18px 18px 18px 6px",
            bgcolor: isUser ? CHAT.bubbleUser : CHAT.bubbleAssistant,
            color: isUser ? "#FFFFFF" : CHAT.ink,
            border: isUser ? "none" : `1px solid ${CHAT.line}`,
            boxShadow: isUser
              ? "0 6px 16px rgba(0, 120, 214, 0.22)"
              : "0 4px 14px rgba(15, 23, 42, 0.06)",
            ...(message.isError && {
              borderLeft: "3px solid",
              borderLeftColor: "error.main",
            }),
          }}
        >
          {message.isLoading ? (
            <LoadingDots statusText={message.content} />
          ) : (
            <AssistantContent
              content={message.content}
              structuredCards={message.cards}
              structuredSuggestions={message.suggestions}
              onSend={onSend}
              isUser={isUser}
            />
          )}

          {message.isError && message.retryPrompt && (
            <Button
              size="small"
              variant="outlined"
              startIcon={<RefreshIcon fontSize="small" />}
              onClick={() => onSend(message.retryPrompt!)}
              sx={{
                mt: 1,
                borderRadius: 2,
                textTransform: "none",
                borderColor: "rgba(28, 42, 50, 0.2)",
                color: CHAT.ink,
              }}
            >
              {IS_ENGLISH ? "Try again" : "Erneut versuchen"}
            </Button>
          )}
        </Box>

        {message.role === "assistant" &&
          !message.isLoading &&
          !message.isError &&
          message.id !== "greeting" && (
            <MessageFeedback
              messageId={message.id}
              feedback={message.feedback}
              onRateMessage={onRateMessage}
            />
          )}
      </Box>
    </Box>
  );
};

const FEEDBACK_REASONS: { id: FeedbackReason; de: string; en: string }[] = [
  { id: "wrong_result", de: "Falsches Ergebnis", en: "Wrong result" },
  { id: "wrong_context", de: "Falscher Kontext", en: "Wrong context" },
  { id: "missing_data", de: "Daten fehlen", en: "Missing data" },
  { id: "unclear", de: "Unklar", en: "Unclear" },
  { id: "other", de: "Sonstiges", en: "Other" },
];

const MessageFeedback = ({
  messageId,
  feedback,
  onRateMessage,
}: {
  messageId: string;
  feedback?: "positive" | "negative";
  onRateMessage: (
    messageId: string,
    rating: "positive" | "negative",
    reason?: FeedbackReason,
  ) => void;
}) => {
  const [pickingReason, setPickingReason] = useState(false);

  if (feedback === "positive") {
    return (
      <Typography variant="caption" sx={{ mt: 0.75, ml: 0.5, color: CHAT.muted, display: "block" }}>
        {IS_ENGLISH ? "Thanks for the feedback." : "Danke für dein Feedback."}
      </Typography>
    );
  }

  if (feedback === "negative") {
    return (
      <Typography variant="caption" sx={{ mt: 0.75, ml: 0.5, color: CHAT.muted, display: "block" }}>
        {IS_ENGLISH
          ? "Thanks — that helps us improve."
          : "Danke — das hilft uns zu verbessern."}
      </Typography>
    );
  }

  if (pickingReason) {
    return (
      <Box sx={{ mt: 0.85, ml: 0.25 }}>
        <Typography variant="caption" sx={{ color: CHAT.muted, display: "block", mb: 0.6 }}>
          {IS_ENGLISH ? "What went wrong?" : "Was war das Problem?"}
        </Typography>
        <Box display="flex" flexWrap="wrap" gap={0.5}>
          {FEEDBACK_REASONS.map((reason) => (
            <Chip
              key={reason.id}
              size="small"
              label={IS_ENGLISH ? reason.en : reason.de}
              onClick={() => onRateMessage(messageId, "negative", reason.id)}
              sx={{
                height: 26,
                fontSize: "0.72rem",
                borderRadius: 1.5,
                backgroundColor: "#FFFFFF",
                border: `1px solid ${CHAT.line}`,
                color: CHAT.ink,

                "&:hover": {
                  backgroundColor: CHAT.primarySoft,
                  borderColor: CHAT.primary,
                },
              }}
            />
          ))}
        </Box>
      </Box>
    );
  }

  return (
    <Box display="flex" alignItems="center" gap={0.25} mt={0.65} ml={0.15}>
      <Typography variant="caption" sx={{ color: CHAT.muted, mr: 0.35 }}>
        {IS_ENGLISH ? "Helpful?" : "Hilfreich?"}
      </Typography>
      <Tooltip title={IS_ENGLISH ? "Yes" : "Ja"}>
        <IconButton
          size="small"
          aria-label={IS_ENGLISH ? "Helpful answer" : "Antwort hilfreich"}
          onClick={() => onRateMessage(messageId, "positive")}
          sx={{ color: CHAT.muted, "&:hover": { color: CHAT.bubbleUser } }}
        >
          <ThumbUpOutlinedIcon sx={{ fontSize: 16 }} />
        </IconButton>
      </Tooltip>
      <Tooltip title={IS_ENGLISH ? "No" : "Nein"}>
        <IconButton
          size="small"
          aria-label={IS_ENGLISH ? "Answer not helpful" : "Antwort nicht hilfreich"}
          onClick={() => setPickingReason(true)}
          sx={{ color: CHAT.muted, "&:hover": { color: "error.main" } }}
        >
          <ThumbDownOutlinedIcon sx={{ fontSize: 16 }} />
        </IconButton>
      </Tooltip>
    </Box>
  );
};

const LoadingDots = ({ statusText }: { statusText?: string }) => {
  const label =
    statusText && statusText.trim().length > 0
      ? statusText
      : IS_ENGLISH
        ? "Thinking…"
        : "Denkt nach…";

  return (
    <Box
      sx={{
        display: "inline-flex",
        alignItems: "center",
        gap: 1,
        "@keyframes chat-typing-dot": {
          "0%, 80%, 100%": { opacity: 0.28, transform: "translateY(0)" },
          "40%": { opacity: 1, transform: "translateY(-2px)" },
        },
      }}
    >
      <Typography variant="body2" sx={{ lineHeight: 1.5, color: CHAT.muted }}>
        {label}
      </Typography>
      <Box sx={{ display: "inline-flex", alignItems: "center", gap: 0.4 }}>
        {[0, 1, 2].map((index) => (
          <Box
            key={`typing-dot-${index}`}
            sx={{
              width: 5,
              height: 5,
              borderRadius: "50%",
              backgroundColor: CHAT.bubbleUser,
              animation: "chat-typing-dot 1.1s ease-in-out infinite",
              animationDelay: `${index * 0.16}s`,
            }}
          />
        ))}
      </Box>
    </Box>
  );
};

/* ------------------------------------------------------------------ *
 * Block protocol
 *
 * The backend returns prose plus deterministic blocks built from MCP
 * data, so nothing rendered below is model-generated free text:
 *
 *   [COURSE_CARD] TITLE: … PLATFORM: … URL: … [/COURSE_CARD]
 *   [PROFILE_CARD] [COLLECTION_CARD] [DIVISION_CARD]
 *   [SUGGESTIONS] one per line [/SUGGESTIONS]
 *
 * Comparisons are answered in prose, so there is no table block.
 * ------------------------------------------------------------------ */

type CardKind = "course" | "profile" | "collection" | "division";

type CardData = {
  kind: CardKind;
  title: string;
  description?: string;
  url?: string;
  facts: { label: string; value: string }[];
  chips: string[];
};

type Block =
  | { kind: "text"; text: string }
  | { kind: "card"; card: CardData }
  | { kind: "suggestions"; items: string[] };

const BLOCK_REGEX =
  /\[(COURSE_CARD|PROFILE_CARD|COLLECTION_CARD|DIVISION_CARD|SUGGESTIONS)\]([\s\S]*?)\[\/\1\]/g;

// Leftover comparison-table markers from older backends must not leak into prose.
const STRIP_LEGACY_BLOCK_REGEX =
  /\[(\/?)COMPARE_TABLE\]/gi;

const CARD_KIND_BY_TAG: Record<string, CardKind> = {
  COURSE_CARD: "course",
  PROFILE_CARD: "profile",
  COLLECTION_CARD: "collection",
  DIVISION_CARD: "division",
};

const FACT_LABELS: Record<string, string> = {
  PLATFORM: "Plattform",
  DURATION: "Dauer",
  DIVISION: "Bereich",
  ITEMS: "Inhalte",
  PARENT: "Oberkategorie",
  COURSES: "Kurse",
};

const NO_DATA = "k. A.";

// Course cards stay uniform: these rows are always rendered, so cards do not
// change shape depending on how complete a course record is.
const ALWAYS_SHOWN_FACTS: Partial<Record<CardKind, string[]>> = {
  course: ["PLATFORM", "DURATION"],
};

const parseFields = (body: string): Map<string, string> => {
  const fields = new Map<string, string>();

  for (const line of body.split(/\r?\n/)) {
    const match = line.match(/^\s*([A-Z_]+):\s*(.*)$/);
    const key = match?.[1];
    const value = match?.[2]?.trim();

    if (key && value) {
      fields.set(key, value);
    }
  }

  return fields;
};

const stripTimeParentheses = (value: string): string =>
  value
    .replace(/\(\s*(ca\.?\s*\d+(?:[.,]\d+)?\s*(?:stunden?|std\.?|hours?))\s*\)/gi, "$1")
    .replace(/\(\s*(\d+(?:[.,]\d+)?\s*(?:stunden?|std\.?|hours?))\s*\)/gi, "$1")
    .replace(/\(\s*(\d+(?:[.,]\d+)?)\s*(?:h|hrs?|hours?)\s*\)/gi, "$1 h")
    .replace(/\s{2,}/g, " ")
    .trim();

const normalizeOptionalText = (value?: string): string | undefined => {
  if (!value) {
    return undefined;
  }

  const cleaned = stripTimeParentheses(value).replace(/\s+/g, " ").trim();

  if (
    !cleaned ||
    cleaned === "..." ||
    /^k\.?\s*a\.?$|^n\.?\s*a\.?$|^null$|^undefined$/i.test(cleaned)
  ) {
    return undefined;
  }

  return cleaned;
};

const buildFallbackDescription = (title: string, kind: CardKind): string => {
  switch (kind) {
    case "course":
      return `Kurs zu ${title}.`;
    case "profile":
      return `Profil: ${title}.`;
    case "collection":
      return `Sammlung zu ${title}.`;
    default:
      return `Themenbereich: ${title}.`;
  }
};

const parseCard = (kind: CardKind, body: string): CardData | null => {
  const fields = parseFields(body);
  const title = normalizeOptionalText(fields.get("TITLE"));

  if (!title) {
    return null;
  }

  const alwaysShown = ALWAYS_SHOWN_FACTS[kind] ?? [];

  const facts = Object.entries(FACT_LABELS)
    .filter(([key]) => fields.has(key) || alwaysShown.includes(key))
    .map(([key, label]) => {
      const rawValue =
        key === "DURATION"
          ? formatDuration(fields.get(key))
          : normalizeOptionalText(fields.get(key));

      const value = rawValue ?? (alwaysShown.includes(key) ? NO_DATA : undefined);

      return value ? { label, value } : null;
    })
    .filter((fact): fact is { label: string; value: string } => Boolean(fact));

  const chips = (fields.get("CHILDREN") ?? "")
    .split("|")
    .map((entry) => entry.trim())
    .filter(Boolean);

  return {
    kind,
    title,
    description: normalizeOptionalText(fields.get("DESCRIPTION")) ?? buildFallbackDescription(title, kind),
    url: normalizeOptionalText(fields.get("URL")),
    facts,
    chips,
  };
};

const parseBlocks = (content: string): Block[] => {
  const blocks: Block[] = [];
  let cursor = 0;
  const normalizedContent = content.replace(STRIP_LEGACY_BLOCK_REGEX, "");

  const pushText = (raw: string) => {
    if (raw.trim().length > 0) {
      blocks.push({ kind: "text", text: raw.trim() });
    }
  };

  for (const match of normalizedContent.matchAll(BLOCK_REGEX)) {
    const start = match.index ?? 0;
    pushText(normalizedContent.slice(cursor, start));

    const tag = match[1].toUpperCase();
    const body = match[2] ?? "";

    if (tag === "SUGGESTIONS") {
      const items = body
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter(Boolean);

      if (items.length > 0) {
        blocks.push({ kind: "suggestions", items });
      }
    } else {
      const card = parseCard(CARD_KIND_BY_TAG[tag], body);

      if (card) {
        blocks.push({ kind: "card", card });
      }
    }

    cursor = start + match[0].length;
  }

  pushText(normalizedContent.slice(cursor));

  return blocks;
};

const AssistantContent = ({
  content,
  structuredCards,
  structuredSuggestions,
  onSend,
  isUser = false,
}: {
  content: string;
  structuredCards?: ChatCard[];
  structuredSuggestions?: string[];
  onSend: (value: string) => void;
  isUser?: boolean;
}) => {
  const safeContent = typeof content === "string" ? content : "";

  // Prefer structured cards from the API — avoids fragile marker parsing.
  const useStructured =
    !isUser &&
    ((structuredCards && structuredCards.length > 0) ||
      (structuredSuggestions && structuredSuggestions.length > 0));

  const blocks = useMemo(
    () => (useStructured ? [] : parseBlocks(safeContent)),
    [safeContent, useStructured],
  );

  const sections = useMemo(() => {
    if (isUser) {
      return [{ kind: "text" as const, text: safeContent.trim() }];
    }

    if (useStructured) {
      const result: Section[] = [];

      if (safeContent.trim().length > 0 && !(structuredCards && structuredCards.length > 0)) {
        result.push({ kind: "text", text: safeContent.trim() });
      } else if (safeContent.trim().length > 0) {
        // Prose only — strip any leftover markers if both were sent.
        const proseOnly = safeContent
          .replace(BLOCK_REGEX, "")
          .replace(STRIP_LEGACY_BLOCK_REGEX, "")
          .trim();
        if (proseOnly.length > 0) {
          result.push({ kind: "text", text: proseOnly });
        }
      }

      if (structuredCards && structuredCards.length > 0) {
        result.push({
          kind: "cards",
          cards: structuredCards.map(mapApiCard),
        });
      }

      if (structuredSuggestions && structuredSuggestions.length > 0) {
        result.push({ kind: "suggestions", items: structuredSuggestions });
      }

      return result;
    }

    return groupIntoSections(blocks);
  }, [blocks, useStructured, safeContent, structuredCards, structuredSuggestions, isUser]);

  if (sections.length === 0) {
    return <FormattedText text={safeContent} keyPrefix="plain" light={isUser} />;
  }

  return (
    <Stack gap={1.5}>
      {sections.map((section, index) => {
        switch (section.kind) {
          case "cards":
            return (
              <Stack key={`cards-${index}`} gap={1}>
                {section.cards.map((card, cardIndex) => (
                  <ContentCard key={`card-${index}-${cardIndex}`} card={card} />
                ))}
              </Stack>
            );

          case "suggestions":
            return (
              <Box key={`chips-${index}`} sx={{ pt: 0.25 }}>
                <Typography
                  variant="caption"
                  sx={{ color: CHAT.muted, display: "block", mb: 0.75, fontWeight: 600 }}
                >
                  {IS_ENGLISH ? "Continue with" : "Weiter mit"}
                </Typography>
                <SuggestionChips items={section.items} onPick={onSend} />
              </Box>
            );

          default:
            return (
              <FormattedText
                key={`text-${index}`}
                text={section.text}
                keyPrefix={`text-${index}`}
                light={isUser}
              />
            );
        }
      })}
    </Stack>
  );
};

const mapApiCard = (card: ChatCard): CardData => ({
  kind: card.kind,
  title: card.title,
  description: card.description,
  url: card.url,
  facts: (card.facts ?? []).map((f) => ({
    label: f.label ?? (f as { Label?: string }).Label ?? "",
    value: f.value ?? (f as { Value?: string }).Value ?? "",
  })),
  chips: card.chips ?? [],
});

type Section =
  | { kind: "text"; text: string }
  | { kind: "cards"; cards: CardData[] }
  | { kind: "suggestions"; items: string[] };

/**
 * Consecutive cards become one section, so a result list reads as a single group
 * that is clearly separated from the answer text above it.
 */
const groupIntoSections = (blocks: Block[]): Section[] => {
  const sections: Section[] = [];

  for (const block of blocks) {
    const previous = sections[sections.length - 1];

    if (block.kind === "card") {
      if (previous?.kind === "cards") {
        previous.cards.push(block.card);
      } else {
        sections.push({ kind: "cards", cards: [block.card] });
      }

      continue;
    }

    if (block.kind === "suggestions") {
      sections.push({ kind: "suggestions", items: block.items });
      continue;
    }

    sections.push({ kind: "text", text: block.text });
  }

  return sections;
};

const openTarget = (url: string) => {
  try {
    const parsed = new URL(url, window.location.origin);

    if (parsed.origin === window.location.origin) {
      window.open(parsed.href, "_blank", "noopener,noreferrer");
      return;
    }

    window.open(parsed.href, "_blank", "noopener,noreferrer");
  } catch {
    window.location.href = url;
  }
};

const ACCENT_BY_KIND: Record<CardKind, string> = {
  course: "#0078D6",
  profile: "#005EA8",
  collection: "#3A7CA5",
  division: "#006B8F",
};

const KIND_LABEL: Record<CardKind, { de: string; en: string }> = {
  course: { de: "Kurs", en: "Course" },
  profile: { de: "Profil", en: "Profile" },
  collection: { de: "Sammlung", en: "Collection" },
  division: { de: "Bereich", en: "Division" },
};

const ContentCard = ({ card }: { card: CardData }) => {
  const clickable = Boolean(card.url);
  const accent = ACCENT_BY_KIND[card.kind];
  const kindLabel = IS_ENGLISH ? KIND_LABEL[card.kind].en : KIND_LABEL[card.kind].de;

  return (
    <Box
      role={clickable ? "link" : undefined}
      tabIndex={clickable ? 0 : undefined}
      aria-label={clickable ? `${card.title} öffnen` : undefined}
      onClick={clickable ? () => openTarget(card.url!) : undefined}
      onKeyDown={
        clickable
          ? (event) => {
              if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                openTarget(card.url!);
              }
            }
          : undefined
      }
      sx={{
        position: "relative",
        overflow: "hidden",
        border: `1px solid ${CHAT.line}`,
        borderRadius: 2.5,
        p: 1.4,
        pl: 1.6,
        cursor: clickable ? "pointer" : "default",
        backgroundColor: "#fbfcfd",
        transition: "transform 0.15s ease, box-shadow 0.15s ease",
        "&::before": {
          content: '""',
          position: "absolute",
          left: 0,
          top: 0,
          bottom: 0,
          width: 3,
          backgroundColor: accent,
        },
        "&:hover": clickable
          ? {
              boxShadow: "0 8px 22px rgba(28, 42, 50, 0.1)",
              transform: "translateY(-1px)",
              backgroundColor: "#fff",
            }
          : undefined,
        "&:focus-visible": { outline: "2px solid", outlineColor: accent, outlineOffset: 2 },
      }}
    >
      <Typography
        variant="caption"
        sx={{
          color: accent,
          fontWeight: 700,
          letterSpacing: "0.04em",
          textTransform: "uppercase",
          fontSize: "0.65rem",
          display: "block",
          mb: 0.45,
        }}
      >
        {kindLabel}
      </Typography>

      <Typography variant="body2" sx={{ fontWeight: 700, lineHeight: 1.35, color: CHAT.ink }}>
        {card.title}
      </Typography>

      {card.description && (
        <Typography
          variant="body2"
          sx={{
            lineHeight: 1.55,
            mt: 0.65,
            color: CHAT.muted,
            display: "-webkit-box",
            WebkitLineClamp: 3,
            WebkitBoxOrient: "vertical",
            overflow: "hidden",
            fontSize: "0.82rem",
          }}
        >
          {card.description}
        </Typography>
      )}

      {card.facts.length > 0 && (
        <Box sx={{ mt: 0.85, display: "flex", flexDirection: "column", gap: 0.2 }}>
          {card.facts.map((fact) => (
            <Typography
              key={fact.label}
              variant="caption"
              sx={{ color: CHAT.muted, lineHeight: 1.65 }}
            >
              <Box component="span" sx={{ fontWeight: 600, color: CHAT.ink }}>
                {fact.label}:
              </Box>{" "}
              {fact.value}
            </Typography>
          ))}
        </Box>
      )}

      {card.chips.length > 0 && (
        <Box display="flex" flexWrap="wrap" gap={0.5} mt={1}>
          {card.chips.map((chip) => (
            <Chip
              key={chip}
              label={chip}
              size="small"
              sx={{
                height: 22,
                fontSize: "0.68rem",
                borderRadius: 1.25,
                backgroundColor: "rgba(31, 79, 90, 0.06)",
                border: "none",
                color: CHAT.ink,
              }}
            />
          ))}
        </Box>
      )}
    </Box>
  );
};

const SuggestionChips = ({
  items,
  onPick,
}: {
  items: string[];
  onPick: (value: string) => void;
}) => (
  <Box display="flex" flexWrap="wrap" gap={0.65}>
    {items.map((item, index) => (
      <Chip
        key={`${index}-${item}`}
        label={item}
        size="small"
        clickable
        onClick={() => onPick(item)}
        sx={{
          height: 30,
          borderRadius: 2,
          fontSize: "0.78rem",
          backgroundColor: CHAT.primarySoft,
          border: "1px solid #C9E4F8",
          color: CHAT.ink,

          "&:hover": {
            backgroundColor: "#D9ECFA",
            borderColor: CHAT.primary,
          },
        }}
      />
    ))}
  </Box>
);

const formatDuration = (raw?: string): string => {
  const value = normalizeOptionalText(raw);

  if (!value) {
    return NO_DATA;
  }

  const withoutOuterParens = value.replace(/^\((.*)\)$/, "$1").trim();
  const cleaned = withoutOuterParens.replace(/\(([^)]+)\)/g, "$1").trim();

  if (
    /\bstunde(n)?\b/i.test(cleaned) ||
    /\bstd\.?\b/i.test(cleaned) ||
    /\bhours?\b/i.test(cleaned)
  ) {
    return cleaned;
  }

  const numericMatch = cleaned.match(/^(\d+(?:[.,]\d+)?)$/);

  if (numericMatch) {
    return `${numericMatch[1].replace(",", ".")} Stunden`;
  }

  return cleaned;
};

const LINK_TOKEN_REGEX =
  /\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)|(https?:\/\/\S+)/gi;

const splitTrailingPunctuation = (
  url: string,
): { cleanedUrl: string; trailing: string } => {
  let cleanedUrl = url;
  let trailing = "";

  while (/[),.!?;:]$/.test(cleanedUrl)) {
    trailing = cleanedUrl.slice(-1) + trailing;
    cleanedUrl = cleanedUrl.slice(0, -1);
  }

  return { cleanedUrl, trailing };
};

const normalizeTextFormatting = (text: string): string =>
  stripTimeParentheses(text)
    .replace(/\s{2,}/g, " ")
    .trim();

const renderInline = (text: string, keyPrefix: string): ReactNode[] => {
  const normalized = normalizeTextFormatting(text);
  const nodes: ReactNode[] = [];
  let cursor = 0;
  let index = 0;

  const pushPlain = (raw: string) => {
    if (raw.length === 0) {
      return;
    }

    // Inline bold, so "**Kurs A** ist kürzer" renders correctly.
    const segments = normalizedSplit(raw);

    segments.forEach((segment, segmentIndex) => {
      if (segment.length === 0) {
        return;
      }

      nodes.push(
        segmentIndex % 2 === 1 ? (
          <strong key={`${keyPrefix}-b-${index}-${segmentIndex}`}>{segment}</strong>
        ) : (
          <span key={`${keyPrefix}-t-${index}-${segmentIndex}`}>{segment}</span>
        ),
      );
    });

    index += 1;
  };

  for (const match of normalized.matchAll(LINK_TOKEN_REGEX)) {
    const start = match.index ?? 0;
    pushPlain(normalized.slice(cursor, start));

    const label = match[1];
    const rawUrl = match[2] ?? match[3];

    if (rawUrl) {
      const { cleanedUrl, trailing } = splitTrailingPunctuation(rawUrl);

      if (cleanedUrl.length > 0) {
        nodes.push(
          <Link
            key={`${keyPrefix}-link-${index}`}
            href={cleanedUrl}
            target="_blank"
            rel="noopener noreferrer"
            underline="always"
          >
            {label ?? "Link"}
          </Link>,
        );
        index += 1;
      }

      if (trailing.length > 0) {
        pushPlain(trailing);
      }
    }

    cursor = start + (match[0]?.length ?? 0);
  }

  pushPlain(normalized.slice(cursor));

  return nodes.length > 0 ? nodes : [normalized];
};

const normalizedSplit = (value: string): string[] => value.split(/\*\*(.+?)\*\*/g);

type TextBlock =
  | { kind: "paragraph"; lines: string[] }
  | { kind: "list"; items: { marker: string; text: string }[] };

const LIST_PREFIX_REGEX = /^(\d+[.)]|[-*•])\s+/;

/**
 * Groups the answer into paragraphs and lists. A blank line closes the current
 * block, which is what gives the answer visible sections instead of one run of
 * equally spaced lines.
 */
const parseTextBlocks = (text: string): TextBlock[] => {
  const blocks: TextBlock[] = [];
  let blockIsOpen = false;

  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();

    if (line.length === 0) {
      blockIsOpen = false;
      continue;
    }

    const previous = blocks[blocks.length - 1];
    const listMatch = line.match(LIST_PREFIX_REGEX);

    if (listMatch) {
      const marker = /^\d/.test(listMatch[1]) ? listMatch[1] : "•";
      const item = { marker, text: line.slice(listMatch[0].length) };

      if (blockIsOpen && previous?.kind === "list") {
        previous.items.push(item);
      } else {
        blocks.push({ kind: "list", items: [item] });
        blockIsOpen = true;
      }

      continue;
    }

    if (blockIsOpen && previous?.kind === "paragraph") {
      previous.lines.push(line);
    } else {
      blocks.push({ kind: "paragraph", lines: [line] });
      blockIsOpen = true;
    }
  }

  return blocks;
};

const renderLine = (line: string, key: string, light = false) => {
  const wholeLineBold = line.match(/^\*\*(.+?)\*\*$/);
  const body = wholeLineBold ? wholeLineBold[1] : line;

  return (
    <Typography
      key={key}
      variant="body2"
      sx={{
        lineHeight: 1.6,
        fontWeight: wholeLineBold ? 700 : 400,
        color: light ? "inherit" : CHAT.ink,
        fontSize: "0.9rem",
      }}
    >
      {renderInline(body, key)}
    </Typography>
  );
};

const FormattedText = ({
  text,
  keyPrefix,
  light = false,
}: {
  text: string;
  keyPrefix: string;
  light?: boolean;
}) => {
  const blocks = useMemo(() => parseTextBlocks(text), [text]);

  return (
    <Stack gap={1.1}>
      {blocks.map((block, blockIndex) =>
        block.kind === "list" ? (
          <Stack key={`${keyPrefix}-list-${blockIndex}`} gap={0.45}>
            {block.items.map((item, itemIndex) => (
              <Box
                key={`${keyPrefix}-item-${blockIndex}-${itemIndex}`}
                sx={{ display: "flex", alignItems: "flex-start", gap: 1 }}
              >
                <Typography
                  variant="body2"
                  sx={{
                    fontWeight: 600,
                    minWidth: 16,
                    lineHeight: 1.6,
                    color: light ? "inherit" : CHAT.ink,
                    fontSize: "0.9rem",
                  }}
                >
                  {item.marker}
                </Typography>

                {renderLine(
                  item.text,
                  `${keyPrefix}-item-${blockIndex}-${itemIndex}-body`,
                  light,
                )}
              </Box>
            ))}
          </Stack>
        ) : (
          <Stack key={`${keyPrefix}-par-${blockIndex}`} gap={0.25}>
            {block.lines.map((line, lineIndex) =>
              renderLine(line, `${keyPrefix}-line-${blockIndex}-${lineIndex}`, light),
            )}
          </Stack>
        ),
      )}
    </Stack>
  );
};
