import { ReactNode, useEffect, useMemo, useRef, useState } from "react";

import CloseIcon from "@mui/icons-material/Close";
import AutoAwesomeIcon from "@mui/icons-material/AutoAwesome";
import RefreshIcon from "@mui/icons-material/Refresh";
import SendIcon from "@mui/icons-material/Send";
import ThumbDownOutlinedIcon from "@mui/icons-material/ThumbDownOutlined";
import ThumbUpOutlinedIcon from "@mui/icons-material/ThumbUpOutlined";
import {
  Box,
  Button,
  Chip,
  Divider,
  Drawer,
  IconButton,
  Link,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";

import { ChatMessage } from "../types/chat.types";

const IS_ENGLISH =
  typeof navigator !== "undefined" && navigator.language.toLowerCase().startsWith("en");

type Props = {
  open: boolean;
  onClose: () => void;
  messages: ChatMessage[];
  onSend: (message: string) => void;
  onRateMessage: (messageId: string, rating: "positive" | "negative") => void;
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
    bottomRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
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

  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      aria-label="The Learning Matchmaker"
    >
      <Box
        sx={{
          width: { xs: "100vw", sm: 440 },
          height: "100%",
          display: "flex",
          flexDirection: "column",
          p: 2,
        }}
      >
        <Box
          display="flex"
          justifyContent="space-between"
          alignItems="center"
          sx={{
            px: 1.25,
            py: 1.1,
            borderRadius: 2,
            background: "linear-gradient(135deg, #eef6ff 0%, #f7f4ff 100%)",
            border: "1px solid",
            borderColor: "rgba(25, 118, 210, 0.14)",
          }}
        >
          <Box display="flex" alignItems="center" gap={1.25}>
            <Box
              sx={{
                width: 38,
                height: 38,
                display: "grid",
                placeItems: "center",
                borderRadius: "50%",
                color: "primary.main",
                backgroundColor: "rgba(255, 255, 255, 0.9)",
                boxShadow: "0 4px 14px rgba(25, 118, 210, 0.16)",
              }}
            >
              <AutoAwesomeIcon fontSize="small" />
            </Box>

            <Box>
              <Typography variant="subtitle1" component="h2" sx={{ fontWeight: 800, lineHeight: 1.2 }}>
                The Learning Matchmaker
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ letterSpacing: 0.25 }}>
                {IS_ENGLISH
                  ? "Ready to find your next learning step"
                  : "Bereit, deinen nächsten Lernschritt zu finden"}
              </Typography>
            </Box>
          </Box>

          <IconButton
            onClick={onClose}
            aria-label={IS_ENGLISH ? "Close chat" : "Chat schließen"}
          >
            <CloseIcon />
          </IconButton>
        </Box>

        <Box
          role="log"
          aria-live="polite"
          aria-relevant="additions text"
          sx={{ flex: 1, overflowY: "auto", mt: 2, mb: 2, pr: 0.5 }}
        >
          {messages.length === 0 ? (
            <EmptyState />
          ) : (
            messages.map((msg) => (
              <MessageRow
                key={msg.id}
                message={msg}
                onSend={submit}
                onRateMessage={onRateMessage}
              />
            ))
          )}

          <div ref={bottomRef} />
        </Box>

        <Box display="flex" gap={1} alignItems="flex-end">
          <TextField
            inputRef={inputRef}
            fullWidth
            size="small"
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
                ? "Ask about courses, skills or profiles..."
                : "Frage zu Kursen, Skills oder Profilen..."
            }
            inputProps={{ "aria-label": "Nachricht eingeben" }}
          />

          <Button
            variant="contained"
            onClick={() => submit(message)}
            disabled={isBusy || message.trim().length === 0}
            endIcon={<SendIcon fontSize="small" />}
          >
            {IS_ENGLISH ? "Send" : "Senden"}
          </Button>
        </Box>
      </Box>
    </Drawer>
  );
};

const EmptyState = () => (
  <Stack gap={1.5} sx={{ pt: 1 }}>
    <Typography variant="body2" sx={{ lineHeight: 1.6 }}>
      {IS_ENGLISH
        ? "I can help you navigate and search the learning platform for courses, skills, learning profiles and learning paths."
        : "Ich helfe dir bei der Navigation und Suche in der Lernplattform – Kurse, Skills, Lernprofile und Lernpfade."}
    </Typography>
  </Stack>
);

const MessageRow = ({
  message,
  onSend,
  onRateMessage,
}: {
  message: ChatMessage;
  onSend: (value: string) => void;
  onRateMessage: (messageId: string, rating: "positive" | "negative") => void;
}) => {
  const isUser = message.role === "user";

  return (
    <Box
      display="flex"
      justifyContent={isUser ? "flex-end" : "flex-start"}
      mb={isUser ? 1 : 2}
    >
      <Box
        sx={{
          maxWidth: isUser ? "80%" : "94%",
          p: isUser ? 1.5 : 1.75,
          borderRadius: 2,
          bgcolor: isUser ? "primary.main" : "grey.100",
          color: isUser ? "primary.contrastText" : "text.primary",
          ...(message.isError && {
            borderLeft: "3px solid",
            borderLeftColor: "error.main",
          }),
        }}
      >
        <Typography
          variant="caption"
          fontWeight={600}
          display="block"
          mb={isUser ? 0.5 : 1}
          sx={{ opacity: 0.75, letterSpacing: 0.3 }}
        >
          {isUser ? "Du" : "The Learning Matchmaker"}
        </Typography>

        {message.isLoading ? (
          <Box display="flex" alignItems="center" gap={0.5} py={0.25}>
            <LoadingDots />
          </Box>
        ) : (
          <AssistantContent content={message.content} onSend={onSend} />
        )}

        {message.role === "assistant" &&
          !message.isLoading &&
          !message.isError &&
          message.id !== "greeting" && (
            <Box display="flex" alignItems="center" gap={0.5} mt={1}>
              <Typography variant="caption" color="text.secondary">
                {IS_ENGLISH ? "Was this answer helpful?" : "War diese Antwort hilfreich?"}
              </Typography>
              <Tooltip title={IS_ENGLISH ? "Helpful" : "Hilfreich"}>
                <IconButton
                  size="small"
                  aria-label={IS_ENGLISH ? "Helpful answer" : "Antwort hilfreich"}
                  color={message.feedback === "positive" ? "primary" : "default"}
                  onClick={() => onRateMessage(message.id, "positive")}
                >
                  <ThumbUpOutlinedIcon fontSize="small" />
                </IconButton>
              </Tooltip>
              <Tooltip title={IS_ENGLISH ? "Not helpful" : "Nicht hilfreich"}>
                <IconButton
                  size="small"
                  aria-label={IS_ENGLISH ? "Answer not helpful" : "Antwort nicht hilfreich"}
                  color={message.feedback === "negative" ? "error" : "default"}
                  onClick={() => onRateMessage(message.id, "negative")}
                >
                  <ThumbDownOutlinedIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </Box>
          )}

        {message.isError && message.retryPrompt && (
          <Button
            size="small"
            variant="text"
            startIcon={<RefreshIcon fontSize="small" />}
            onClick={() => onSend(message.retryPrompt!)}
            sx={{ mt: 0.5, ml: -0.5 }}
          >
            {IS_ENGLISH ? "Try again" : "Erneut versuchen"}
          </Button>
        )}
      </Box>
    </Box>
  );
};

const LoadingDots = () => (
  <Box
    sx={{
      display: "inline-flex",
      alignItems: "center",
      gap: 0.75,
      "@keyframes chat-typing-dot": {
        "0%": { opacity: 0.35, transform: "translateY(0px) scale(0.85)" },
        "40%": { opacity: 1, transform: "translateY(-1px) scale(1)" },
        "100%": { opacity: 0.35, transform: "translateY(0px) scale(0.85)" },
      },
    }}
  >
    <Typography variant="body2" sx={{ lineHeight: 1.5 }}>
      {IS_ENGLISH
        ? "The Learning Matchmaker is typing"
        : "The Learning Matchmaker schreibt"}
    </Typography>

    <Box sx={{ display: "inline-flex", alignItems: "center", gap: 0.45 }}>
      {[0, 1, 2].map((index) => (
        <Box
          key={`typing-dot-${index}`}
          sx={{
            width: 5,
            height: 5,
            borderRadius: "50%",
            backgroundColor: "text.secondary",
            animation: "chat-typing-dot 1s ease-in-out infinite",
            animationDelay: `${index * 0.18}s`,
          }}
        />
      ))}
    </Box>
  </Box>
);

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
  REQUIRED: "Pflichtkurse",
  OPTIONAL: "Optionale Kurse",
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
  onSend,
}: {
  content: string;
  onSend: (value: string) => void;
}) => {
  // Guarded because a failed backend response can leave content undefined at
  // runtime even though the type says otherwise.
  const safeContent = typeof content === "string" ? content : "";
  const blocks = useMemo(() => parseBlocks(safeContent), [safeContent]);

  const sections = useMemo(() => groupIntoSections(blocks), [blocks]);

  if (sections.length === 0) {
    return <FormattedText text={safeContent} keyPrefix="plain" />;
  }

  return (
    <Stack gap={1.75}>
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
              <Box key={`chips-${index}`}>
                <Divider sx={{ mb: 1 }} />

                <SuggestionChips items={section.items} onPick={onSend} />
              </Box>
            );

          default:
            return (
              <FormattedText
                key={`text-${index}`}
                text={section.text}
                keyPrefix={`text-${index}`}
              />
            );
        }
      })}
    </Stack>
  );
};

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
  course: "primary.main",
  profile: "secondary.main",
  collection: "warning.main",
  division: "info.main",
};

const ContentCard = ({ card }: { card: CardData }) => {
  const clickable = Boolean(card.url);

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
        border: "1px solid",
        borderColor: "divider",
        borderLeft: "3px solid",
        borderLeftColor: ACCENT_BY_KIND[card.kind],
        borderRadius: 2,
        p: 1.5,
        cursor: clickable ? "pointer" : "default",
        backgroundColor: "background.paper",
        "&:hover": { bgcolor: clickable ? "action.hover" : undefined },
        "&:focus-visible": { outline: "2px solid", outlineColor: "primary.main" },
      }}
    >
      <Box display="flex" alignItems="flex-start" gap={1} mb={card.description ? 1 : 0.75}>
        <Typography variant="body2" sx={{ fontWeight: 700, flex: 1, lineHeight: 1.4 }}>
          {card.title}
        </Typography>
      </Box>

      {card.description && (
        <Typography
          variant="body2"
          sx={{
            lineHeight: 1.6,
            mb: 1,
            color: "text.primary",
            display: "-webkit-box",
            WebkitLineClamp: 3,
            WebkitBoxOrient: "vertical",
            overflow: "hidden",
          }}
        >
          {card.description}
        </Typography>
      )}

      {card.facts.map((fact) => (
        <Typography
          key={fact.label}
          variant="caption"
          color="text.secondary"
          display="block"
          sx={{ lineHeight: 1.75 }}
        >
          <strong>{fact.label}:</strong> {fact.value}
        </Typography>
      ))}

      {card.chips.length > 0 && (
        <Box display="flex" flexWrap="wrap" gap={0.5} mt={1}>
          {card.chips.map((chip) => (
            <Chip key={chip} label={chip} size="small" variant="outlined" />
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
  <Box display="flex" flexWrap="wrap" gap={0.75} mt={0.5}>
    {items.map((item, index) => (
      <Chip
        key={`${index}-${item}`}
        label={item}
        size="small"
        variant="outlined"
        clickable
        onClick={() => onPick(item)}
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

const renderLine = (line: string, key: string) => {
  const wholeLineBold = line.match(/^\*\*(.+?)\*\*$/);
  const body = wholeLineBold ? wholeLineBold[1] : line;

  return (
    <Typography
      key={key}
      variant="body2"
      sx={{ lineHeight: 1.65, fontWeight: wholeLineBold ? 700 : 400 }}
    >
      {renderInline(body, key)}
    </Typography>
  );
};

const FormattedText = ({
  text,
  keyPrefix,
}: {
  text: string;
  keyPrefix: string;
}) => {
  const blocks = useMemo(() => parseTextBlocks(text), [text]);

  return (
    <Stack gap={1.25}>
      {blocks.map((block, blockIndex) =>
        block.kind === "list" ? (
          <Stack key={`${keyPrefix}-list-${blockIndex}`} gap={0.5}>
            {block.items.map((item, itemIndex) => (
              <Box
                key={`${keyPrefix}-item-${blockIndex}-${itemIndex}`}
                sx={{ display: "flex", alignItems: "flex-start", gap: 1 }}
              >
                <Typography
                  variant="body2"
                  sx={{ fontWeight: 600, minWidth: 16, lineHeight: 1.65 }}
                >
                  {item.marker}
                </Typography>

                {renderLine(
                  item.text,
                  `${keyPrefix}-item-${blockIndex}-${itemIndex}-body`,
                )}
              </Box>
            ))}
          </Stack>
        ) : (
          <Stack key={`${keyPrefix}-par-${blockIndex}`} gap={0.25}>
            {block.lines.map((line, lineIndex) =>
              renderLine(line, `${keyPrefix}-line-${blockIndex}-${lineIndex}`),
            )}
          </Stack>
        ),
      )}
    </Stack>
  );
};
