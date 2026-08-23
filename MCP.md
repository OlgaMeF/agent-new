# MCP Interface — learn-skills

The MCP (Model Context Protocol) server exposes all learn-skills content to AI agents (GitHub Copilot, Claude Desktop, custom LLM agents).

**Endpoint:** `POST /mcp`  
**Transport:** Streamable HTTP (MCP standard)  
**Package:** `ModelContextProtocol.AspNetCore`

---

## Configuration

All settings live under the `"Mcp"` key in `appsettings.{Env}.json`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | bool | `true` | Set to `false` to disable the `/mcp` endpoint entirely (returns 503) |
| `ApiKey` | string | `""` | API key for machine clients — injected at runtime via env var `Mcp__ApiKey` |
| `RateLimitPerMinute` | int | `200` | Max requests per minute per API key |
| `CacheTtlMinutes` | int | `10` | In-process cache TTL for read-only resources and tools. Set to `0` to disable caching (test environments). |

### Environment defaults

| Environment | ApiKey | RateLimit | CacheTtl |
|-------------|--------|-----------|----------|
| Development | `dev-mcp-key-change-in-production` | 500 | 2 min |
| Staging | `""` ← inject via CF env | 200 | 10 min |
| Production | `""` ← inject via CF env | 200 | 10 min |
| Test | `test-mcp-key` | 1000 | 0 (disabled) |

**CF deployment:** Set the `Mcp__ApiKey` environment variable in the manifest or CF user-provided service. Never commit a real key to source.

---

## Authentication

### Machine clients (AI agents, scripts)

Supply the API key in the request header:

```
X-MCP-Key: <your-api-key>
```

### User-context tools (get_my_progress, get_my_bookmarks)

These tools require a valid OIDC bearer token — the same token the React frontend uses:

```
Authorization: Bearer <oidc-token>
```

Requests with an `Authorization` header bypass the `X-MCP-Key` check automatically.

**A missing token is not an HTTP error.** Both tools answer with HTTP 200 and a
regular result payload:

```json
{ "error": "Authentication required. Please provide a valid bearer token." }
```

Clients must inspect the payload for `error`; relying on a 401 or an empty body
will silently look like "the user has no bookmarks".

---

## Tool naming

Tool names are mostly `snake_case`, **but the exposed name is the name of the C#
handler method** — the SDK publishes each `[McpServerTool]` method under its own
method name. That is why the handlers in `Tools/` are deliberately written in
snake_case, against normal C# convention.

The handlers in `Resources/McpSupportingResources.cs` were never renamed, so three
tools are exposed in **PascalCase**: `GetDivisions`, `GetTags`, `GetSiteConfig`.
Calling them as `get_divisions`, `get_tags` or `get_site_config` fails with
"tool not found".

---

## Available Tools

✅ = signature verified against the handler source.
❔ = source file was not available for review; name and parameters are unconfirmed.

### Course Tools

| Tool | Auth | Parameters | Notes |
|------|------|-----------|-------|
| `search_courses` ✅ | API key | `query?`, `platform?`, `skillLevel?`, `clusterName?`, `divisionId?`, `limit=50`, `offset=0` | `query` matches title, summary and instructor (partial, case-insensitive). `platform` is an **exact** match, `clusterName` a partial one. **`skillLevel` and `divisionId` are accepted but ignored** — neither field exists on `LearnCourse`. `limit` is clamped to 1–500. |
| `get_course` ✅ | API key | `courseId` | ID only, no title resolution. `description` is parsed out of `ContentJson`; the card-level tools do not carry it. Returns `null` for unknown or deleted courses. |
| `get_courses_by_tag` ✅ | API key | `tagId?`, `tagName?`, `language?` | Needs at least one of `tagId` / `tagName`, otherwise returns an empty list. **`tagName` is matched exactly** (case-insensitive, no wildcards), so a phrase like `"GenAI courses"` finds nothing. Resolves translations first, then `Tag.Name`. Courses are collected from both the legacy `ParentTagId` / `ChildTagId` columns and `TagAssignments`. **No pagination** — all matches are returned. |

### Skills Tools

| Tool | Auth | Parameters | Notes |
|------|------|-----------|-------|
| `search_skills` ✅ | API key | none | Full skill taxonomy as a nested tree, built from a single query. Call this for hierarchy and to discover valid tag IDs. |
| `get_skill` ✅ | API key | `skillName` | Resolves by ID, `Tag.Name` or a translated name — exact match first, then partial. **Returns no children**, so sub-skills have to come from `search_skills`. |
| `GetTags` ✅ | API key | none | Flat list of all tags with translations and `parentTagId`. Documented as `get_tags` before; the real name is PascalCase. |

### Profile Tools

| Tool | Auth | Parameters | Notes |
|------|------|-----------|-------|
| `search_profiles` ✅ | API key | `divisionId?`, `departmentId?`, `query?`, `limit=50`, `offset=0` | `query` matches title and summary partially. `divisionId` is resolved through the `Department → Division` relationship. Returns required/optional **counts but no course list**. `limit` is clamped to 1–500. |
| `get_profile` ✅ | API key | `profileId` | ID only, no name resolution. Includes `courseAssignments` ordered by `SortOrder`. |
| `get_profile_skills` ✅ | API key | `query` | Accepts a profile name or ID. **Groups by `LearnCourse.ClusterName`, not by tag**; courses without a cluster land in a group called `Allgemein`. |

### Supporting Tools

| Tool | Auth | Parameters | Notes |
|------|------|-----------|-------|
| `GetDivisions` ✅ | API key | none | All divisions with nested departments. Use it to discover valid `divisionId` / `departmentId` values. Documented as `get_divisions` before. |
| `GetSiteConfig` ✅ | API key | none | Site feature flags and supported languages. **Throws** when no site definition for `learn-skills` exists. Documented as `get_site_config` before. |
| `list_courses` ❔ | API key | unconfirmed | Declared in `Resources/McpCourseResource.cs`, which was not available for review. |
| `list_profiles` ❔ | API key | unconfirmed | Declared in `Resources/McpProfileResource.cs`, which was not available for review. |

### User-Context Tools

| Tool | Auth | Parameters | Notes |
|------|------|-----------|-------|
| `get_my_progress` ✅ | Bearer token | none | Completed and in-progress courses plus a completion rate for the whole site. The rate is **site-wide, not per profile** — profiles are not evaluated separately. |
| `get_my_bookmarks` ✅ | Bearer token | none | Bookmarked courses and profiles, split by discriminator. |
| `search_collections` ✅ | API key | `limit=20`, `offset=0` | **No query parameter** — topic filtering has to happen on the client. Returns `itemCount` only, **not the contained items**. `limit` is clamped to 1–100. Not cached. |


### Prompt Templates ❔

These are **MCP prompts** (accessible via `prompts/list` and `prompts/get`, not `tools/list`). They return step-by-step AI instruction strings for guided workflows. `McpLearnPrompts.cs` was not available for review, so these names and parameters are unconfirmed:

| Tool | Parameters | Description |
|------|-----------|-------------|
| `get_prompt_recommend_courses` | `skillGap`, `preferredPlatform?`, `maxDurationHours?` | Instructs agent to match skill gap → tags → courses |
| `get_prompt_summarize_profile` | `profileId` | Instructs agent to fetch and format a profile summary |
| `get_prompt_compare_profiles` | `profileIdA`, `profileIdB` | Instructs agent to compare two profiles side by side |

---

## Response Fields

Field names are **not** consistent across tools. A client that assumes `title` and
`id` everywhere silently drops data, because the affected payloads still parse —
they just yield nothing.

### Courses appear under two different shapes

`search_courses`, `get_course` and `get_courses_by_tag` return course cards with
`id` and `title`. Courses nested in a profile (`get_profile.courseAssignments`,
`get_profile_skills.skillGroups[].courses`) and bookmarked courses use different
names, and carry far fewer fields:

| Full course card | Nested / bookmarked course |
|------------------|----------------------------|
| `id` | `courseId` |
| `title` | `courseTitle` |
| `summary`, `sourcePlatform`, `durationInHours`, `instructor`, `clusterName`, `isActive`, … | — not included |
| `deepLink` | `deepLink` |

A nested course therefore only offers title, requirement and link. Platform and
duration require a separate `get_course` call per course.

### Requirement is a string, not a boolean

`courseAssignments[].requirementType` is one of `Required`, `Optional`,
`Recommended` or `Assigned` (mapped from the `CourseRequirementType` enum, where
`Mandatory` becomes `Required`). There is no `isMandatory` flag — collapsing the
value into a boolean mislabels `Recommended`.

### Counts use the plural "Courses"

Profiles carry `requiredCoursesCount` and `optionalCoursesCount` — not
`requiredCourseCount`. Also present: `totalCourses`.

### Other names worth knowing

| Payload | Field | Note |
|---------|-------|------|
| `get_profile_skills` | `profileId` | not `id` |
| `get_profile_skills` | `skillGroups[].skillName` | not `name`; value is a `ClusterName` |
| `get_my_bookmarks` | `profiles[].profileTitle` | not `title` |
| `search_skills` | `roots[]`, `children[]` | recursive `McpTagNodeDto`, no description |
| `get_skill` | `parentTagName` | `group` is a tag group, **not** the parent |
| tag payloads | `translations[]` | `{ language, name }` — looks like a tag, but is not one |
| `search_collections` | `collections[].itemCount` | contained items are not exposed |

---

## Deep Links

Every item in every response includes a `deepLink` field pointing to the canonical UI URL.

| Content | Deep Link |
|---------|-----------|
| Course (internal) | `https://{host}/learn-skills/pages/content/{id}` |
| Course (external) | `ssoTrainingUrl` or `trainingUrl` |
| Learning Profile | `https://{host}/learn-skills/profile/view/{id}` |
| Division | `https://{host}/learn-skills/d/{divisionId}` |
| Collection | `https://{host}/learn-skills/c/{id}` |

The host is resolved from the incoming request — no hardcoded base URLs.

---

## Caching

Read-only tools are cached in-process (`IMemoryCache`) using a two-level key scheme:

```
mcp:courses:list:{limit}:{offset}          ❔ list_courses
mcp:courses:detail:{id}                    get_course
mcp:courses:search:{sha256-hash-of-params} search_courses
mcp:courses:skill:{sha256-hash-of-params}  get_courses_by_tag
mcp:profiles:list:{limit}:{offset}         ❔ list_profiles
mcp:profiles:detail:{id}                   get_profile
mcp:profiles:search:{sha256-hash-of-params} search_profiles
mcp:profiles:skills:{sha256-hash-of-params} get_profile_skills
mcp:tags:list                              GetTags
mcp:tags:hierarchy                         search_skills
mcp:tags:skill:{sha256-hash-of-params}     get_skill
mcp:divisions:list                         GetDivisions
mcp:site-config                            GetSiteConfig
```

Cache is invalidated per entity type (`courses`, `profiles`, `tags`) via `CancellationChangeToken`. Note that **`GetDivisions`, `GetSiteConfig` and `get_skill` are registered under the `tags` category** despite their key prefixes, so they are dropped whenever tags are invalidated.

Never cached: the user-context tools (`get_my_progress`, `get_my_bookmarks`) and `search_collections`.

---

## Source Files

All MCP code lives under `MXC.ComTools.Apps/Setup/Mcp/`:

```
Setup/Mcp/
├── McpOptions.cs                  — configuration POCO
├── McpApiKeyMiddleware.cs          — X-MCP-Key auth + bearer bypass
├── McpServerExtensions.cs         — AddLearnSkillsMcpServer() / UseLearnSkillsMcp()
├── McpCacheService.cs             — IMemoryCache wrapper + invalidation
├── McpLearnPrompts.cs             — prompt template tools
├── Dtos/
│   ├── McpCourseDto.cs
│   ├── McpProfileDto.cs           — includes McpProfileSkillsResponse, McpProfileSkillGroupDto
│   ├── McpDivisionDto.cs
│   ├── McpTagDto.cs               — includes McpTagNodeDto, McpSkillHierarchyResponse
│   └── McpSiteConfigDto.cs
├── Resources/
│   ├── McpCourseResource.cs       ❔ list_courses, get_course
│   ├── McpProfileResource.cs      ❔ list_profiles
│   └── McpSupportingResources.cs  ✅ GetDivisions, GetTags, GetSiteConfig
└── Tools/
    ├── McpCourseTools.cs          ✅ search_courses, get_course, get_courses_by_tag
    ├── McpProfileTools.cs         ✅ search_profiles, get_profile, get_profile_skills,
    │                                 get_skill, search_skills
    └── McpUserTools.cs            ✅ get_my_progress, get_my_bookmarks, search_collections
```

`get_course` is declared in `McpCourseTools.cs`; whether `McpCourseResource.cs` declares a second handler under the same name could not be checked.

`get_skill` lives in `McpProfileTools.cs`, not in a dedicated skills file.

---

## Quick Start (connect with Claude Desktop)

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "learn-skills": {
      "url": "https://<your-host>/mcp",
      "headers": {
        "X-MCP-Key": "<your-api-key>"
      }
    }
  }
}
```

## Quick Start (connect with GitHub Copilot / VS Code)

Add to `.vscode/mcp.json` in your workspace:

```json
{
  "servers": {
    "learn-skills": {
      "type": "http",
      "url": "https://<your-host>/mcp",
      "headers": {
        "X-MCP-Key": "<your-api-key>"
      }
    }
  }
}
```
