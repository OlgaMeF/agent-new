# Architecture Overview

Architectural structure of the MXC.ComTools project and how components interact.

## Table of Contents

- [Overview](#overview)
- [Project Structure](#project-structure)
- [Layer Architecture](#layer-architecture)
- [Technology Stack](#technology-stack)
- [Database Architecture](#database-architecture)
- [Act As (User Impersonation)](#act-as-user-impersonation)

## Overview

MXC.ComTools is a multi-application content management platform serving internal web applications. Clean layered architecture with four main projects:

```
┌─────────────────────────────────────────────┐
│      MXC.ComTools.Apps (Web Host)           │
│  - Razor Pages (SSR)                        │
│  - API Controllers                          │
│  - React Frontend Apps                      │
└──────────────┬──────────────────────────────┘
               │
        ┌──────┴──────────┐
        ▼                 ▼
┌──────────────┐   ┌─────────────────────────┐
│   Clients    │   │  Content/Services       │
│  (External)  │   │  (Business Logic)       │
└──────────────┘   └───────────┬─────────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │  DataServicesUI       │
                    │  (Dapper Queries)     │
                    └──────────┬────────────┘
                               │
                               ▼
┌────────────────────────────────────────────┐
│        MXC.ComTools.Core                   │
│  (Domain Models & Base Classes)            │
└────────────────────────────────────────────┘
                    │
                    ▼
          ┌─────────────────────┐
          │   PostgreSQL DB      │
          │  (mb_v25_v001)       │
          └─────────────────────┘
```

## Project Structure

### 1. MXC.ComTools.Core

**Purpose**: Domain layer with core entities, enums, base classes, shared utilities.

**Key Components**:

- **Common/BaseClasses/**
  - `ContentBase`: Base for all content (Title, Summary, SiteUrl, Tags, InteractionJson)
  - `PublishableEntity`: Publishing lifecycle (PublishedAt, ArchivedAt)
  - `EntityBase`: Basic entity (Id, CreatedAt, ModifiedAt)

- **Common/Enums/**
  - `PermissionRole`: Viewer, Editor, Admin, MasterAdmin
  - `PublishingStatus`: Draft, Published, Archived, Scheduled
  - `ContentType`: MediaEntry, SkillProfile, etc.

- **Content/Models/**
  - `SiteDefinition`: Site config (URL, theme, languages, content types)
  - `SiteSection`: Navigation sections
  - `ContentPlacement`: Content positioning

- **Domain/**
  - `MediaEntry`: Video/media content
  - `SkillProfile`: Learning profiles
  - `CourseEntry`: Learning courses
  - `CollectionEntry`: User collections
  - `Comment`, `Like`, `Bookmark`: User interactions

- **Identity/**
  - `AppUser`: User profiles
  - `UserPermission`: Site-specific permissions

**Dependencies**: None (pure domain)

---

### 2. MXC.ComTools.Clients

**Purpose**: External service integrations.

**Key Clients**:
- **IdentityServiceClient**: User lookup from Mercedes-Benz identity system
- **ProcessingServiceClient**: Content/image processing, media transcoding
- **External APIs**: LinkedIn Learning (Skills@MS), Jive (Social Collaboration), AWS S3 (Asset Storage)

**Dependencies**: MXC.ComTools.Core, HTTP client libraries

---

### 3. MXC.ComTools.Apps

**Purpose**: Main web application hosting multiple React SPAs.

**Backend Structure**:

- **Pages/** (Razor Pages for SSR)
  - `BaseIndexModel`: Base page with SSR data loading
  - App-specific pages with `window.__INITIAL_DATA__`

- **Controllers/** (REST API)
  - **Admin/**: AdminRootController (user init), ContentBaseController, SiteDefinitionController,
    `ImpersonationController` (Act As: start/stop/status/candidates)
  - **Common/**: InteractionController (likes/comments), TagsController
  - **DataServicesUI/**: MediaEntryUIController, SkillProfileUIController

- **DataServicesUI/** (Data Layer)
  - **Queries/**: Raw SQL with Dapper (MediaEntryQueries, SkillProfileQueries)
  - **Mappers/**: Database → DTO mapping
  - **DTOs/**: MediaPageUIDto, SkillProfileCardUIDto, etc.

- **Content/Services/** (Business Logic)
  - SiteDefinitionService, ContentPlacementService
  - MediaEntryService, CollectionService
  - InteractionService (likes, comments, progress)

- **Features/**
  - Import/, Search/, Publishing/, Authorization/
  - **Infrastructure/Middleware/**: `ImpersonationMiddleware` (principal rewrite for Act As)
  - **Infrastructure/Services/**: `ImpersonationCookieService` (DataProtection-encrypted cookie)
  - **Infrastructure/Authorization/**: `ImpersonationGuard` (env/admin/test-user gate helpers)

**Frontend Structure** (`clientapp/`):

- **apps/** (Individual Applications)
  - `learn-skills/`: Skills@MS (Home, MyLearn, Progress, Collections)
  - `app-admin/`: Administration interface
  - `learn/`: Shared library used by learn-skills
  - `smqg/`: Social Media Quality Gate (chapters, search, PDF export)

- **packages/** (Shared)
  - `components/`: InteractionButtons, MediaPlayer, ContentPicker,
    `ImpersonationBanner` (fixed warning bar shown during Act As session)
  - `features/`: SiteConfiguration, UserProgress, Collections
  - `modal/`: ModalManager system

- **config/**
  - `store/`: Redux store + RTK Query API (auto-generated from OpenAPI)
  - `intl/`: i18n translations (de.ts, en.ts)
  - `paths/`: URL builders

**Dependencies**: Core, Clients, ASP.NET Core, EF Core, Dapper

---

### 4. AdminThumbGenerator

**Purpose**: CLI tool for app teaser thumbnail generation.

---

## Layer Architecture

### Data Flow

```
User Request (Browser)
    ↓
React App (clientapp/apps/)
    ↓ HTTP via RTK Query
API Controllers
    ↓
Content Services
    ↓
┌─────────────┬────────────┐
│             │            │
▼             ▼            ▼
DataServicesUI  External   Database
(Dapper)      APIs      (PostgreSQL)
```

### Request Pipeline

1. **Client**: React app → RTK Query API call
2. **Routing**: ASP.NET Core matches endpoint
3. **Retirement Check**: AppRetirementMiddleware redirects retired sites
4. **Auth**: OIDC middleware validates JWT
5. **Impersonation**: `ImpersonationMiddleware` — if a valid Act As cookie is present and all
   gates pass (env is Dev/Staging, real user is MasterAdmin, target is a `[TESTER]` user),
   replaces `HttpContext.User` with the impersonated principal before authorization runs.
6. **Authorization**: Permission attributes check roles
7. **Controller**: Validates input → calls service
8. **Service**: Business logic → data layer
9. **Data**:
   - **Reads**: Dapper raw SQL (fast, optimized)
   - **Writes**: EF Core (consistency, migrations)
10. **Response**: DTO → JSON → client

**Site Retirement**: See [Site Retirement Guide](./SITE_RETIREMENT_GUIDE.md) for managing application lifecycle.

### Key Principles

- **CQRS-lite**: Dapper for reads, EF Core for writes
- **SSR + SPA**: Razor Pages for initial render, React for interactivity
- **Interface-Based**: Services use interfaces
- **Domain-Driven**: Core independent of infrastructure

---

## Technology Stack

### Backend
- **.NET 9.0**, **ASP.NET Core**
- **EF Core** (writes/migrations), **Dapper** (reads)
- **PostgreSQL**
- **Serilog**, **OIDC/JWT**
- **Mapster**, **FluentValidation**

### Frontend
- **React 18**, **TypeScript 5**
- **MUI 5** (Material-UI)
- **Redux Toolkit**, **RTK Query**
- **TanStack Router**
- **i18next**, **Framer Motion**
- **React Hook Form**, **Vite**

### External
- Mercedes-Benz Identity Service
- Processing Service (e.g. Jive)
- OIDC SSO

---

## Database Architecture

### Schema: `mb_v25_v001`

**Core Tables**:

- **ContentBases**: Polymorphic base (all content types)
  - Columns: Id, Title, Summary, SiteUrl, Tags, InteractionJson, PublishedAt, etc.
  - JSON: `InteractionJson` (viewCount, likeCount, commentCount), `MetadataJson`

- **AppEntries**: Application metadata
  - Columns: AppUrl, Category, TagsString (CSV), AttachmentId, AppDescription

- **MediaEntries**: Video/media content
  - Columns: MediaUrl, MediaSource, Duration, Year
  - JSON: Talent, product models, transcript

- **SkillProfiles**: Skills@MS learning profiles
  - JSON: Skills, learning paths

- **SiteDefinitions**: Site configuration
  - Columns: Title, AppBarTitle, DefaultTheme
  - JSON: `LanguageContentsJson`, `ContentTypeMappingsJson`

**Identity Tables**:

- **AppUsers**: User profiles (UserId, Email, FirstName, LastName)
- **UserPermissions**: Site-specific permissions (UserId, SiteUrl, PermissionRole)

**Interaction Tables**:

- **Comments**, **UserBookmarks**, **UserProgress**, **CollectionEntries**

### Migration Strategy

EF Core migrations:

```bash
# Create migration
dotnet ef migrations add MigrationName --context AppDbContext --project MXC.ComTools.Apps

# Apply
dotnet ef database update --context AppDbContext --project MXC.ComTools.Apps
```

Located in: `MXC.ComTools.Apps/Migrations/`

### Query Strategy

- **Dapper for Reads**: Raw optimized SQL in `DataServicesUI/Queries/`
- **EF Core for Writes**: CRUD in `Content/Services/`, transactions

---

## Security

### Authentication
- OIDC: Mercedes-Benz SSO
- JWT tokens with auto-refresh

### Authorization
- RBAC: Viewer, Editor, Admin, MasterAdmin
- Attributes: `[Authorize(Roles = "Admin,MasterAdmin")]`, `[RequireSitePermission("learn-skills")]`
- Resource-based: Ownership checks

### Act As (User Impersonation)

See the dedicated [Act As section](#act-as-user-impersonation) below for the full architecture.

### Data Security
- Connection strings in User Secrets/Environment
- Parameterized queries (Dapper/EF Core)
- React auto-escapes, CSP headers
- CORS restricted

### Geo-Blocking
- Geo-blocking can restrict access by primary Accept-Language header
- Skipped in Development

---

## Performance

### Backend
- Dapper for reads (3-5x faster than EF)
- Memory cache (10-15 min)
- Gzip/Brotli compression

### Frontend
- Code splitting per app
- RTK Query caching
- SSR initial data
- Lazy loading, virtual scrolling

### Database
- Indexes on SiteUrl, Tags, PublishedAt
- Connection pooling (max 100)

---

**Next**: [Getting Started Guide](./GETTING-STARTED.md)

---

## Act As (User Impersonation)

The "Act as" feature lets a **MasterAdmin** temporarily adopt the identity of a designated test
user to reproduce issues, test permission-sensitive flows, and validate UX without creating
real IAM accounts.

### Constraints

| Rule | Detail |
|------|--------|
| **Environments** | Development and Staging only. Completely inactive in Production (middleware is a no-op; any stale cookie is cleared). |
| **Who can use it** | MasterAdmin only (verified via DB `IsMasterAdmin` flag or `RootAdmins.UserIds` config). |
| **Who can be impersonated** | Only users whose `LastName` starts with `[TESTER]` (case-insensitive). No IAM/SSO account needed. |
| **Admin-on-admin blocked** | Cannot impersonate another MasterAdmin or RootAdmin. |
| **Self-impersonation blocked** | Cannot impersonate yourself. |

### How It Works

```
MasterAdmin (browser)                  Server
      │                                   │
      │  POST /api/impersonation/start     │
      │  { targetUserId }                 │
      │──────────────────────────────────>│ gate: env + MasterAdmin + target is [TESTER]
      │<── Set-Cookie: .AppHub.ActAs ─────│ DataProtection-encrypted ticket
      │                                   │
      │  subsequent requests              │
      │  (SSO cookie + ActAs cookie)      │
      │──────────────────────────────────>│ ImpersonationMiddleware (after UseAuthentication,
      │                                   │   before UseAuthorization):
      │                                   │   verify real admin → load target → rewrite
      │                                   │   HttpContext.User to impersonated principal
      │<── response as impersonated user──│
      │                                   │
      │  POST /api/impersonation/stop      │
      │──────────────────────────────────>│ delete ActAs cookie
      │<── 200 OK ────────────────────────│ real identity restored on next request
```

The SSO auth cookie is **never modified**. The ActAs cookie is a separate
DataProtection-signed/encrypted token holding `{ TargetUserId, OriginalAdminId, IssuedAtUtc }`.
Stopping impersonation simply deletes this cookie — no re-login required.

### Security Gates (re-checked on every request)

1. `ImpersonationGuard.IsAvailable` — env must be Development or Staging **and**
   `ImpersonationOptions.Enabled` must be `true`.
2. `ImpersonationGuard.IsRealUserMasterAdmin` — real user (from original SSO cookie) must be
   MasterAdmin. Uses `act_original_sub` claim when already impersonating so the real admin
   identity is never lost.
3. Target validation — target must exist in DB, not be deleted, not be MasterAdmin/RootAdmin,
   and `LastName` must start with `[TESTER]`.

If any gate fails, the cookie is cleared and the request proceeds unauthenticated.

### Key Components

| Component | Location | Purpose |
|-----------|----------|---------|
| `ImpersonationOptions` | `MXC.ComTools.Core/Options/` | Config: `Enabled`, `CookieName`, `MaxDurationMinutes`, `TestUserPrefix` |
| `ImpersonationGuard` | `Infrastructure/Authorization/` | Pure static gate helpers |
| `ImpersonationCookieService` | `Infrastructure/Services/` | DataProtection encrypt/decrypt of the ActAs cookie |
| `ImpersonationMiddleware` | `Infrastructure/Middleware/` | Principal rewrite in the ASP.NET Core pipeline |
| `ImpersonationController` | `Controllers/Admin/` | REST API: `POST start`, `POST stop`, `GET status`, `GET candidates` |
| `ImpersonationBanner` | `clientapp/packages/components/ImpersonationBanner/` | Persistent warning bar shown during an active Act As session |

### Configuration

Enabled per environment via `appsettings.<Env>.json`:

```json
// appsettings.Development.json and appsettings.Staging.json
{
  "Impersonation": {
    "Enabled": true
  }
}
```

The key is absent in `appsettings.Production.json` (defaults to `false`).

### Test Users (no IAM required)

Any user in the Identity store whose `LastName` starts with `[TESTER]` is a valid impersonation
target. These users are created via the normal admin user-management UI and do **not** require
an IAM/SSO registration — they are local Identity rows only.

```
LastName examples that qualify:
  [TESTER] Smith      ✓
  [tester] Reviewer   ✓
  Smith               ✗
  Admin Tester        ✗
```

See [Getting Started — Act As setup](./GETTING-STARTED.md#act-as-setup) for how to create test
users and start impersonating.
