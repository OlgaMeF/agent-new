# Golden Dialogues

Multi-turn eval cases for the learn-skills chatbot. Machine-readable twin:
[`golden-dialogues.json`](./golden-dialogues.json).

Use these to verify follow-up resolution (positional cards, active profile/course,
clarify when a position is missing) without relying on live GenAI flakiness for
the *expected* assertions — run against a fixed fixture or recorded MCP payloads
when available.

## Cases

| Id | Theme |
|----|--------|
| `gd-bookmarks-second-card` | Bookmarks → second card |
| `gd-profile-kurse-dazu` | Profile → “Kurse dazu” keeps active profile |
| `gd-course-weitere-kurse` | Course detail → “weitere Kurse” |
| `gd-positional-collection` | Positional collection (“die zweite”) |
| `gd-mixed-cards` | Mixed course + profile cards, then position |
| `gd-clarify-missing-position` | Clarify when position missing |

---

### gd-bookmarks-second-card

1. User: *Zeig mir meine Merkliste* → expect intent `bookmarks`, cards present.
2. User: *Erzähl mir mehr zur zweiten* → expect intent `course_details` (or profile_details if 2nd is a profile), `expectCardKindAtIndex` on prior turn’s addressable list: index 1.

### gd-profile-kurse-dazu

1. User: *Zeig mir das Profil Data Analyst* → expect `profile_details`.
2. User: *Kurse dazu* → expect `profile_courses`, `expectActiveProfileKept: true`.

### gd-course-weitere-kurse

1. User: *Was ist der Kurs Power BI Foundations?* → expect `course_details`.
2. User: *Zeig mir weitere Kurse zu dem Thema* → expect `course_search` (or `skill_courses` if routed via skill).

### gd-positional-collection

1. User: *Welche Lernsammlungen gibt es?* → expect `collections`.
2. User: *Öffne die zweite* → expect `collections` / collection detail with `expectCardKindAtIndex: { index: 1, kind: "collection" }`.

### gd-mixed-cards

1. User: *Suche etwas zu SQL* (mixed course + profile cards) → cards of kinds `course` and `profile`.
2. User: *Nimm die erste Karte* → resolve against `LastAddressableItems[0]` only.

### gd-clarify-missing-position

1. User: *Zeig mir Kurse zu Python* → expect `course_search` with ≥2 cards.
2. User: *Erzähl mir mehr dazu* (no ordinal / no active course) → expect `clarify`.

## How to run (manual / CI later)

1. Start a clean conversation (new `x-chat-session-id`).
2. For each case, send `steps[].user` in order.
3. Assert `expectIntent` on the structured turn (or router log).
4. After step *n*, check card kind at `expectCardKindAtIndex` against the **previous** turn’s addressable cards when the follow-up is positional.
5. For `expectActiveProfileKept`, confirm `ActiveProfile` in the conversation snapshot did not clear between turns.
