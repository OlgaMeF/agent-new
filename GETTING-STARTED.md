# Getting Started Guide

Step-by-step guide to set up your local MXC.ComTools development environment.

## Prerequisites

### Before you begin, ensure you have:

- **.NET 10.0 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
  - optional (e.g. no admin rights) use script to install software - [Script Download Page](https://dotnet.microsoft.com/en-us/download/dotnet/scripts)

    Install command (set to current version): 
  
    ``./dotnet-install.ps1 -Version 10.0.100`` 
- **Node.js 20+** - [Download](https://nodejs.org/)
  - to install Node.js without admin rights, download the ZIP, unzip it and add the path to windows environment settings for your account (Path). 

    Maybe it is needed to unblock the downloaded files first (powershell):

    ``Get-ChildItem -Path "C:\Users\...\node-v24.13.1-win-x64" -Recurse | Unblock-File``
- **Yarn** - `npm install -g yarn`
- **PostgreSQL 15+** - [Download](https://www.postgresql.org/download/)
  - to install postgreSQL without admin rights, download ZIP and unzip it somewhere. You can create a script to start/stop DB like:

    ```bash
    # adapt variable
    $PG_DIR = "C:\Users\DEINNAME\pgsql18"
    $DATA_DIR = "$PG_DIR\data"
    $USERNAME = "deinuser"

    # init DB (only 1st time needed)
    if (!(Test-Path $DATA_DIR)) {
        & "$PG_DIR\bin\initdb.exe" -D "$DATA_DIR" -U $USERNAME -A password -W
    }

    # start PostgreSQL-Server
    & "$PG_DIR\bin\pg_ctl.exe" -D "$DATA_DIR" -l "$PG_DIR\logfile.txt" start

    Write-Host "PostgreSQL started."
    ``` 
    or a stop script (like ):
    ```bash
    # adapt variables
    $PG_DIR = "C:\Users\DEINNAME\pgsql18"
    $DATA_DIR = "$PG_DIR\data"

    # stop PostgreSQL-Server
    & "$PG_DIR\bin\pg_ctl.exe" -D "$DATA_DIR" stop

    Write-Host "PostgreSQL stopped."
    ```

- **Git** - Version control [download](https://git-scm.com/install/windows)
- **Visual Studio Code** or **Visual Studio 2022+** (IDE) - [download](https://code.visualstudio.com/)

### Optional:
- **pgAdmin** or **DBeaver** - Database management GUI
- **Postman** or **Thunder Client** or **Bruno** - API testing

### 'npm' repository configuration

Add a file to project folder with name .npmrc

    registry=https://registry.npmjs.org/
    @mercedes-benz:registry=https://artifacts.i.mercedes-benz.com/artifactory/api/npm/switch-main-npm-releases/
    //artifacts.i.mercedes-benz.com/artifactory/api/npm/switch-main-npm-releases/:_authToken=<PERSONAL_AUTH_TOKEN>

You can generate your personal AUTH_TOKEN in the Mercedes-Benz repository (JFrog) and replace the placeholder <PERSONAL_AUTH_TOKEN> in the .npmrc file

## Quick Start (5 Minutes)

```bash
# 1. Clone repository
git clone https://github.com/your-org/ms-comtools.git
cd ms-comtools/MXC.ComTools.Apps

# 2. Get appsettings from team (contains DB connection, secrets)
# Copy appsettings.Development.json to MXC.ComTools.Apps/

# 3. Install EF Core tools (must match the .NET SDK major version)
dotnet tool install --global dotnet-ef --version 10.0.0

# 4. Create database and run migrations
dotnet ef database update --context AppDbContext

# 5. Install frontend dependencies
cd clientapp
yarn install
cd ..

# 6. Initialize admin users
# Start app first:
dotnet run

# Then in another terminal:
# if you get a SSL Certificat error see https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-9.0&tabs=visual-studio%2Clinux-sles#os-x---certificate-not-trusted
curl -X GET https://localhost:5001/api/AppUser/runonce

# 7. Start development server
dotnet watch
```

Access at: **https://localhost:5001**

---

## Detailed Setup

### 1. Clone Repository

```bash
git clone https://github.com/your-org/ms-comtools.git
cd ms-comtools
```

Project structure:
```
ms-comtools/
├── MXC.ComTools.Apps/          # Main application
├── MXC.ComTools.Core/          # Domain models
├── MXC.ComTools.Clients/       # External clients
└── docs/                       # Documentation
```

### 2. Database Setup

#### Create PostgreSQL Database

**Option A: Using psql (CLI)**
```bash
# Connect to PostgreSQL
psql -U postgres

# Create database
CREATE DATABASE test_25_1;

# Create user (optional, if not using postgres user)
CREATE USER mxc_user WITH PASSWORD 'your_password';
GRANT ALL PRIVILEGES ON DATABASE test_25_1 TO mxc_user;

# Exit
\q
```

**Option B: Using pgAdmin (GUI)**
1. Open pgAdmin
2. Right-click **Databases** → **Create** → **Database...**
3. Name: `test_25_1`
4. Owner: `postgres` or your custom user
5. Click **Save**

#### Connection String

Default connection string (localhost):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=test_25_1;Username=postgres;Password=your_password"
  }
}
```

**macOS/Linux Users**: PostgreSQL may use Unix socket authentication. Try:
```json
"DefaultConnection": "Host=localhost;Database=test_25_1;Username=postgres"
```

Or connect via socket:
```json
"DefaultConnection": "Host=/tmp;Database=test_25_1;Username=postgres"
```

### 3. Configuration Files

#### Configuration architecture

The app uses a layered configuration strategy:

| File | Tracked in git | Purpose |
|---|---|---|
| `appsettings.json` | Yes | Non-secret base defaults |
| `appsettings.Development.json` | No (gitignored) | Local credentials — DB connection, OIDC secret, S3 credentials |
| `appsettings.Production.json` | No (gitignored) | Non-credential production overrides only |

In production (Cloud Foundry), the database and S3 credentials are **never read from any file**. They are injected automatically at container startup via `VCAP_SERVICES` from the bound CF services (`learning_db` and `learning_s3`). See [DEPLOYMENT.md](DEPLOYMENT.md) for details.

Locally (via `dotnet watch`), `VCAP_SERVICES` is not set, so `appsettings.Development.json` is the only source of credentials.

#### Get appsettings.Development.json

Request the complete `appsettings.Development.json` from your colleagues. This file contains:
- PostgreSQL connection strings (local database)
- AWS S3 credentials (for asset storage)
- OIDC client secrets
- External service API keys (IdentityService, ProcessingService)
- Feature flags

**Location**: `MXC.ComTools.Apps/appsettings.Development.json`

#### Minimal appsettings.Development.json Template

If creating manually:

```json
{
  "ConnectionStrings": {
    "PostgresDbConnection": "Host=localhost;Port=5432;Database=test_25_1;Username=comtools;Password=comtools;maxPoolSize=10;minPoolSize=2;ConnectionLifetime=600;CommandTimeout=30;Timeout=30;Pooling=true",
    "MediaLibraryDbConnection": "Host=localhost;Port=5432;Username=comtools;Password=comtools;Database=mx_podcasts;"
  },
  "StorageOptions": {
    "Profile": "default",
    "Region": "eu-central-1",
    "ServiceURL": "<s3-endpoint-url — ask team lead or get from CF service key>",
    "AccessKey": "<ask-team-lead>",
    "SecretKey": "<ask-team-lead>",
    "BucketName": "<ask-team-lead>",
    "PublishRoot": "wwwroot_2025",
    "VideoPath": "learn_videos"
  },
  "GenAi": {
    "ApiKey": "<api_key from CF service key — see GenAI section below>",
    "ApiBase": "<api_base from CF service key>",
    "ConfigUrl": "<config_url from CF service key>"
  },
  "RootAdmins": {
    "UserIds": ["YOUR_USERID"]
  },
  "OidcService": {
    "ClientId": "<ask-team-lead>",
    "ClientSecret": "<ask-team-lead>",
    "OAuthAuthority": "https://sso.mercedes-benz.com/",
    "ApiBaseUrl": "https://sso.mercedes-benz.com/",
    "TokenEndpoint": "https://sso.mercedes-benz.com/as/token.oauth2",
    "OAuthAuthorize": "/as/authorization.oauth2",
    "OAuthToken": "/as/token.oauth2",
    "OAuthRedirectUrl": "/signin-oidc",
    "OAuthMetadataAddress": "https://sso.mercedes-benz.com/.well-known/openid-configuration"
  },
  "Database": {
    "Schema": "mb_v25_v001",
    "CommandTimeoutMinutes": 5,
    "EnableSensitiveDataLogging": false
  },
  "Mcp": {
    "Enabled": true,
    "ApiKey": "dev-mcp-key-change-in-production",
    "RateLimitPerMinute": 500,
    "CacheTtlMinutes": 2
  },
  "Impersonation": {
    "Enabled": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

**Never commit appsettings.Development.json to Git** (already in .gitignore)

### 4. Install Tools

#### Entity Framework Core CLI

```bash
# Install globally
dotnet tool install --global dotnet-ef --version 10.0.0

# Verify installation
dotnet ef --version
# Expected: Entity Framework Core .NET Command-line Tools 10.0.x
```

#### Verify .NET SDK

```bash
dotnet --version
# Expected: 10.0.x
```

### 5. Apply Database Migrations

The repository contains all database migrations. Simply apply them to create the database schema:

```bash
cd MXC.ComTools.Apps

# Apply all migrations to create database schema
dotnet ef database update --context AppDbContext
```

**Expected output:**
```
Build started...
Build succeeded.
Applying migration '20241201_InitialCreate'.
Applying migration '20241205_AddMediaEntries'.
...
Done.
```

**Verify the database schema was created:**
```bash
psql -U postgres -d test_25_1 -c "\dt"
# Should show tables: ContentBases, MediaEntries, Users, etc.
```

This creates all tables:
- ContentBases, MediaEntries, SiteDefinitions
- SiteDefinitions, SiteSection, ContentPlacements
- AppUsers, UserPermissions
- Comments, Likes, UserBookmarks, CollectionEntries

**Creating New Migrations (when YOU modify the data model):**

Only create migrations when you change entities in `MXC.ComTools.Core/Domain/`:

```bash
cd MXC.ComTools.Apps

# Create migration for your changes
dotnet ef migrations add YourFeatureName --context AppDbContext

# Apply it
dotnet ef database update --context AppDbContext

# Commit the migration files
git add Migrations/
git commit -m "Add migration: YourFeatureName"
```

**After pulling changes with new migrations:**
```bash
dotnet ef database update --context AppDbContext
```
Done.
```

This creates all tables:
- ContentBases, MediaEntries, SiteDefinitions
- SiteDefinitions, SiteSection, ContentPlacements
- AppUsers, UserPermissions
- Comments, Likes, UserBookmarks, CollectionEntries

**Troubleshooting**:

- **"Connection refused"**: PostgreSQL not running
  ```bash
  # macOS: Start PostgreSQL
  brew services start postgresql@15

  # Linux:
  sudo systemctl start postgresql

  # Windows: Start via Services app
  ```

- **"password authentication failed"**: Check connection string username/password

- **"database does not exist"**: Create database first (see step 2)

### 5a. (Optional) Import Production Data Snapshot

For development with realistic data, you can import a production database snapshot. **This step is optional** - the application works fine with an empty database.

#### Get Database Dump

Request the latest database dump from your colleagues:
- File: `mb_v25_v001_data_YYYYMMDD.dump`
- Contains: Production data only (anonymized if applicable)
- Schema: `mb_v25_v001`
- **Important**: Dump contains only data, not schema definitions

#### Import Dump to Local Database

**Prerequisites:**
- PostgreSQL running locally
- Database `test_25_1` created (see step 2)
- **Migrations already applied** (see step 5) - this creates the schema structure

**Import the dump:**

```bash
# Navigate to dump file location
cd /path/to/dumps

# Restore data-only dump to local database
# Schema must already exist (via migrations in step 5)
pg_restore -h localhost -U postgres -d test_25_1 \
  -n mb_v25_v001 \
  --data-only \
  --disable-triggers \
  mb_v25_v001_data_YYYYMMDD.dump
```

**Options explained:**
- `-h localhost` - Connect to local PostgreSQL
- `-U postgres` - Username (adjust if different)
- `-d test_25_1` - Target database name
- `-n mb_v25_v001` - Only restore this schema
- `--data-only` - Import only data, not schema definitions
- `--disable-triggers` - Disable triggers during import (faster, avoids conflicts)

**Expected output:**
```
pg_restore: processing data for table "mb_v25_v001.ContentBases"
pg_restore: processing data for table "mb_v25_v001.MediaEntries"
...
```

**Verify import:**
```bash
# Connect to database
psql -U postgres -d test_25_1

# Check tables in schema
\dt mb_v25_v001.*

# Count records (example)
SELECT COUNT(*) FROM mb_v25_v001."ContentBases";

# Exit
\q
```

#### Create Your Own Dump (For Data Export)

**From Production (via SSH tunnel):**

```bash
# 1. Create SSH tunnel to production database
cf ssh -L 63305:d15792c-psql-master-alias.node.dc1.a9s-internal-prod1:5432 MXC-COMTOOLS-MULTIAPP-PROD

# 2. In another terminal, create data-only dump
pg_dump -h localhost -p 63305 -U cfuser -d d15792c -n mb_v25_v001 --format=custom --data-only  --no-owner --no-privileges --file=mb_v25_v001_data_$(date +%Y%m%d).dump


# Options explained:
# -n mb_v25_v001       Only dump this schema
# --format=custom      Binary format (compressed, faster restore)
# --data-only          Export only data, not schema definitions (tables already exist via migrations)
# --no-owner           Don't restore ownership (allows import as any user)
# --no-privileges      Don't restore grants (avoid permission conflicts)
# --file=<name>        Output filename with date
```

**Expected output:**
```
pg_dump: dumping contents of table "mb_v25_v001.ContentBases"
...
```

**Share dump with team:**
```bash
# Check file size
ls -lh mb_v25_v001_data_*.dump

# Upload to shared location (Teams, OneDrive, etc.)
# Example file size: 50-200 MB (depending on data volume)
```

**⚠️ Security Notes:**
- **Check for sensitive data** before sharing dumps
- Consider anonymizing PII (personally identifiable information)
- Use secure file sharing (OneDrive, Teams, not email)
- Don't commit dumps to Git
- Dumps contain production data - treat as confidential

### 6. Frontend Setup

Navigate to the React frontend:

```bash
cd clientapp

# Install dependencies
yarn install

# This installs:
# - React, TypeScript, MUI
# - Redux Toolkit, RTK Query
# - TanStack Router, i18next
# - Vite, ESLint, etc.
```

Expected time: 1-2 minutes

### 7. Initialize Root Admin Users

The application requires at least one root admin user to bootstrap the system. Root admin users are configured in `appsettings.json` and initialized via a special bootstrap endpoint.

#### Configure Root Admins

Root admin User IDs are defined in `appsettings.json` (or `appsettings.Development.json`):

```json
{
  "RootAdmins": {
    "UserIds": ["SCHOEF1", "FLORJUD"]
  }
}
```

**Important**:
- These User IDs must match your Mercedes-Benz User IDs
- Add yourself to this list during development
- In production, keep this list restricted to actual administrators

#### Run Bootstrap Endpoint

**No authentication required** - this endpoint creates the configured root admins and all necessary Identity roles.

```bash
# Start the application first
cd MXC.ComTools.Apps
dotnet run
```

Wait for:
```
Now listening on: https://localhost:5001
Now listening on: http://localhost:5000
```

**In another terminal, initialize root admins:**

```bash
# Bootstrap root admin users from configuration
curl -X GET https://localhost:5001/api/AppUser/runonce
```

**Response:**
```json
{
  "message": "Root admin initialization completed",
  "rootAdmins": ["SCHOEF1", "FLORJUD"],
  "results": [
    "Role Viewer created",
    "Role Editor created",
    "Role Admin created",
    "Role MasterAdmin created",
    "User SCHOEF1 created",
    "User SCHOEF1 added to MasterAdmin role",
    "User FLORJUD created",
    "User FLORJUD added to MasterAdmin role"
  ]
}
```

**What this does:**
- Creates all Identity roles (Viewer, Editor, Admin, MasterAdmin)
- Creates user accounts for all configured root admins
- Assigns MasterAdmin role to all root admin users
- Can be run multiple times safely (idempotent)

**Security Notes:**
- This endpoint has **no authentication** to allow initial bootstrap
- Only creates users configured in appsettings.json
- Should be protected/removed in production after initial setup
- Consider restricting via IP allowlist or removing the endpoint entirely after team onboarding

---

### 7a. (Optional) Act As Setup — Test Users for Impersonation {#act-as-setup}

The "Act as" feature lets a MasterAdmin temporarily adopt a test user's identity to reproduce
issues and test permission-sensitive flows **without a real IAM/SSO account**. It is active
only in Development and Staging.

#### Verify the feature is enabled

`appsettings.Development.json` and `appsettings.Staging.json` must contain:

```json
{
  "Impersonation": {
    "Enabled": true
  }
}
```

This is already shipped in the repository defaults. Do **not** add this key to
`appsettings.Production.json`.

#### Create a test user (no IAM required)

1. Log in as a MasterAdmin and open **App Admin** → **Users**.
2. Click **Create User**.
3. Fill in the form:
   - **User ID**: any arbitrary local id, e.g. `TESTER001`
   - **Last Name**: must start with `[TESTER]`, e.g. `[TESTER] Doe`
   - **First Name**, **Email**: any value
   - **Role**: Reader (default is fine — impersonation tests non-admin flows)
4. Save. The user is stored in the local Identity store only; no SSO registration needed.

> The `[TESTER]` prefix on `LastName` is the sole marker that makes a user selectable for
> impersonation. It is case-insensitive. Users without this prefix cannot be impersonated.

#### Start an Act As session

1. Open **App Admin** → **Users**.
2. Find a `[TESTER]` user — they are marked with a **TESTER** chip in the grid.
3. Click the **Act as** button in that row.
4. The browser redirects to `/learn-skills` and a **yellow warning banner** appears at the
   top of every page confirming which user you are acting as.

All subsequent requests are processed as that user until you stop the session.

#### Stop an Act As session

Click **Stop impersonating** in the yellow banner. The page reloads and your MasterAdmin
identity is restored. No re-login is required.

#### API reference

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/impersonation/start` | `POST` | Start impersonating `{ targetUserId }` |
| `/api/impersonation/stop` | `POST` | Stop current session |
| `/api/impersonation/status` | `GET` | Returns `{ isAvailable, isImpersonating, targetUserId?, targetName? }` |
| `/api/impersonation/candidates` | `GET` | Lists all `[TESTER]` users available as targets |

All endpoints require MasterAdmin. In Production they return `404`.

#### Security summary

| What | Behaviour |
|------|-----------|
| Environment guard | Completely inactive in Production (middleware no-ops) |
| Role guard | Only MasterAdmin can start/stop |
| Target guard | Only `[TESTER]` `LastName` users, never another MasterAdmin/RootAdmin |
| Cookie | DataProtection-encrypted, HttpOnly, Secure, bounded to `MaxDurationMinutes` (120 min) |
| Audit | Start, stop, and rejected attempts logged via Serilog with real admin id + target id |

See [ARCHITECTURE.md — Act As](./ARCHITECTURE.md#act-as-user-impersonation) for the
full architecture documentation.

---

### 8. Run Development Server

#### Option A: dotnet watch (Recommended)

```bash
cd MXC.ComTools.Apps

# Start with hot reload
dotnet watch
```

This starts:
- **Backend** (ASP.NET Core): Port 5001 (HTTPS), 5000 (HTTP)
- **Frontend** (Vite dev server): Port 5173 (proxied through backend)

**Hot Reload**:
- C# changes: Automatically recompile and reload
- React changes: Instant HMR (Hot Module Replacement)

#### Option B: dotnet run (Simple)

```bash
dotnet run
```

No hot reload. Restart manually after changes.

#### Option C: Separate Frontend Dev Server

For faster frontend development:

**Terminal 1** (Backend):
```bash
cd MXC.ComTools.Apps
dotnet run
```

**Terminal 2** (Frontend):
```bash
cd MXC.ComTools.Apps/clientapp
yarn dev
```

Frontend runs on **http://localhost:5173** with API proxy to backend.

### 9. Access the Application

Open browser:

- **Skills@MS**: https://localhost:5001/learn-skills
- **App Admin**: https://localhost:5001/app-admin
- **SMQG**: https://localhost:5001/qualitygate

**First Time Login**:
1. Click "Login" (redirects to OIDC SSO)
2. Use your Mercedes-Benz credentials
3. After redirect, you're logged in

If you added yourself as admin (step 7), you'll have full access.

### 10. Background Jobs & Hangfire Dashboard

The application uses **Hangfire** for background job processing and scheduled tasks.

#### Access Hangfire Dashboard

Once the application is running, access the dashboard at:

**https://localhost:5001/hangfire**

**What you'll see:**
- **Jobs**: Active, scheduled, and completed background jobs
- **Recurring Jobs**: Scheduled tasks (cron jobs)
- **Servers**: Hangfire server status
- **Retries**: Failed jobs with retry information
- **Succeeded/Failed**: Job execution history

#### Current Background Jobs

**Publishing Status Sync Job:**
- **Purpose**: Syncs publishing status for content items
- **Schedule**: Hourly (via `Cron.Hourly`)
- **Automatic**: Runs in background

#### Managing Jobs

**View Job Details:**
1. Open Hangfire dashboard: https://localhost:5001/hangfire
2. Navigate to **Jobs** tab
3. Click on any job to see execution details, parameters, and logs

**Trigger Manual Job:**
1. Go to **Recurring Jobs** tab
2. Find the desired job
3. Click **Trigger now** button

**Retry Failed Job:**
1. Go to **Failed** tab
2. Select failed job
3. Click **Retry** button

#### Development Notes

**Job Configuration:**
- Jobs are registered in `Infrastructure/Jobs/` folder
- Recurring jobs configured in `JobsServiceCollectionExtensions.cs`
- Cron schedules: `Cron.Hourly`, `Cron.Daily`, `Cron.Weekly`, or custom expressions

**Common Cron Expressions:**
```csharp
Cron.Minutely          // Every minute
Cron.Hourly            // Every hour
Cron.Daily             // Every day at midnight
Cron.Weekly            // Every Sunday at midnight
Cron.Monthly           // First day of month at midnight
"0 */6 * * *"          // Every 6 hours
"0 2 * * *"            // Every day at 2 AM
```

**Disable Jobs (Development):**
If background jobs interfere with development, disable them in `appsettings.Development.json`:
```json
{
  "Hangfire": {
    "Enabled": false
  }
}
```

**Security:**
- Dashboard is protected by authentication in production
- Requires Admin or MasterAdmin role
- Local development: No authentication required

---

## External APIs Configuration

### LinkedIn Learning API
Für Skills@MS Integration in `appsettings.json`:
```json
"LinkedInService": {
  "ClientId": "your-client-id",
  "ClientSecret": "your-client-secret"
}
```

### Jive API
Für Social Collaboration in `appsettings.json`:
```json
"JiveService": {
  "ApiBaseUrl": "https://social-api.intra.corpintra.net",
  "Username": "your-username",
  "Password": "your-password"
}
```

### AWS S3 Storage

S3 is used for asset storage (images, videos). In production (Cloud Foundry), credentials are injected automatically via `VCAP_SERVICES` (`learning_s3` service binding). Locally, the credentials must be set manually in `appsettings.Development.json`.

The staging S3 bucket can be used for local development — get the values from the CF staging service key:

```bash
cf target -o <your-org> -s staging
cf service-key learning_s3 local-dev-key
# If the key doesn't exist yet:
cf create-service-key learning_s3 local-dev-key
```

The credential JSON will contain `access_key_id`, `secret_access_key`, `bucket_name`, and a custom `endpoint` URL. Map these to `appsettings.Development.json`:

```json
"StorageOptions": {
  "Profile": "default",
  "Region": "eu-central-1",
  "ServiceURL": "<endpoint from service key>",
  "AccessKey": "<access_key_id from service key>",
  "SecretKey": "<secret_access_key from service key>",
  "BucketName": "<bucket_name from service key>",
  "PublishRoot": "wwwroot_2025",
  "VideoPath": "learn_videos"
}
```

### GenAI Chat-Bot

The chat-bot feature requires a connection to the `genai-chat-model` Cloud Foundry service. In production and staging, credentials are provided automatically via `VCAP_SERVICES`. Locally, they must be set manually in `appsettings.Development.json`.

#### Get credentials from CF staging

```bash
cf target -o <your-org> -s staging
cf service-key genai-chat-model local-dev-key
# If the key doesn't exist yet:
cf create-service-key genai-chat-model local-dev-key
```

The output contains a `credentials.endpoint` object with `api_key`, `api_base`, and `config_url`.

#### Add to appsettings.Development.json

```json
"GenAi": {
  "ApiKey": "<api_key from credentials.endpoint>",
  "ApiBase": "<api_base from credentials.endpoint>",
  "ConfigUrl": "<config_url from credentials.endpoint>"
}
```

**How it works locally:**
- `ApiBase` → base URL for the `GenAiService` HTTP client
- `ApiKey` → Bearer token for all requests (chat completions + config endpoint)
- `ConfigUrl` → called once at startup to resolve the model name from `advertisedModels`
- Optional override: set `GenAi:Model` directly to skip the config endpoint call

In production, all three values are mapped automatically by `VcapGenAiConfigurationProvider` from the bound CF service — no file configuration needed.

---

## Development Workflow

### Project Structure

```
MXC.ComTools.Apps/
├── Controllers/              # API endpoints
├── Pages/                    # Razor Pages (SSR)
├── DataServicesUI/           # Data layer (Dapper)
├── Content/Services/         # Business logic
├── clientapp/                # React frontend
│   ├── apps/                 # Individual applications
│   │   ├── adlib/
│   │   └── learn/
│   ├── packages/             # Shared components
│   └── config/               # Store, routing, i18n
├── appsettings.json          # Base config
├── appsettings.Development.json  # Local overrides (DO NOT COMMIT)
└── Program.cs                # Application entry
```

### Making Changes

#### Backend Changes (C#)

1. Edit files in `Controllers/`, `Content/Services/`, or `DataServicesUI/`
2. `dotnet watch` auto-reloads on save
3. Check terminal for compilation errors

Example: Add new API endpoint
```csharp
// Controllers/Common/TagsController.cs
[HttpGet("popular")]
public async Task<IActionResult> GetPopularTags()
{
    var tags = await _tagService.GetPopularTagsAsync();
    return Ok(tags);
}
```

#### Frontend Changes (React/TypeScript)

1. Edit files in `clientapp/apps/` or `clientapp/packages/`
2. Vite HMR updates instantly in browser
3. Check browser console for errors

Example: Add new component
```tsx
// clientapp/apps/learn/components/CourseBadge.tsx
export const CourseBadge: React.FC<{ course: CourseDto }> = ({ course }) => (
  <Chip label={course.category} size="small" />
);
```

#### Database Changes (Migrations)

```bash
# Create migration for schema change
dotnet ef migrations add AddNewFeature

# Apply migration
dotnet ef database update

# Rollback if needed
dotnet ef database update PreviousMigrationName
```

### API Testing

#### Using Thunder Client (VS Code Extension)

1. Install **Thunder Client** extension
2. Create request: GET `https://localhost:5001/api/ui/mediathek/cards/featured`
3. Add header: `Authorization: Bearer <your-token>` (copy from browser DevTools)

#### Using Browser DevTools

1. Open https://localhost:5001/learn
2. Open DevTools (F12) → Network tab
3. Click "Featured" → See API call
4. Copy request as cURL or fetch for testing

#### Using Swagger (Optional)

If enabled in appsettings:
```json
"Features": { "EnableSwagger": true }
```

Access: https://localhost:5001/swagger

---

## Troubleshooting

### Backend Issues

#### "Connection refused" or "No connection could be made"

**Problem**: PostgreSQL not running

**Solution**:
```bash
# macOS
brew services start postgresql@15

# Linux
sudo systemctl start postgresql

# Check status
psql -U postgres -c "SELECT version();"
```

#### "Login failed for user..."

**Problem**: Wrong database credentials

**Solution**:
1. Check `appsettings.Development.json` → `ConnectionStrings.DefaultConnection`
2. Verify username/password match PostgreSQL user
3. Try connecting via psql to test: `psql -U postgres -d test_25_1`

#### "Could not find a part of the path...wwwroot"

**Problem**: Frontend not built yet

**Solution**:
```bash
cd clientapp
yarn build
```

Or use `dotnet watch` which builds automatically.

#### Port 5001 already in use

**Problem**: Another process using port

**Solution**:
```bash
# Find process
lsof -i :5001  # macOS/Linux
netstat -ano | findstr :5001  # Windows

# Kill process or change port in appsettings
"Kestrel": {
  "Endpoints": {
    "Https": { "Url": "https://localhost:5002" }
  }
}
```

### Frontend Issues

#### "Cannot find module '@mui/material'"

**Problem**: Dependencies not installed

**Solution**:
```bash
cd clientapp
rm -rf node_modules yarn.lock
yarn install
```

#### "Unexpected token" or TypeScript errors

**Problem**: Outdated packages or cache

**Solution**:
```bash
cd clientapp
yarn cache clean
yarn install
yarn dev
```

#### Hot reload not working

**Problem**: File watcher limits (Linux)

**Solution**:
```bash
# Increase inotify limit
echo fs.inotify.max_user_watches=524288 | sudo tee -a /etc/sysctl.conf
sudo sysctl -p
```

#### API calls return 404

**Problem**: Vite proxy not configured

**Solution**:
Check `clientapp/vite.config.ts`:
```ts
server: {
  proxy: {
    '/api': 'https://localhost:5001'
  }
}
```

### Database Issues

#### "Table does not exist"

**Problem**: Migrations not applied

**Solution**:
```bash
cd MXC.ComTools.Apps
dotnet ef database update
```

#### "Cannot drop database because it is currently in use"

**Problem**: Active connections

**Solution**:
```sql
-- In psql
SELECT pg_terminate_backend(pg_stat_activity.pid)
FROM pg_stat_activity
WHERE pg_stat_activity.datname = 'test_25_1'
  AND pid <> pg_backend_pid();

DROP DATABASE test_25_1;
CREATE DATABASE test_25_1;
```

Then re-run migrations.

---

## IDE Setup

### Visual Studio Code (Recommended)

**Extensions**:
```bash
# Install recommended extensions
code --install-extension ms-dotnettools.csharp
code --install-extension ms-dotnettools.vscode-dotnet-runtime
code --install-extension esbenp.prettier-vscode
code --install-extension dbaeumer.vscode-eslint
code --install-extension bradlc.vscode-tailwindcss
code --install-extension rangav.vscode-thunder-client
```

**Settings** (.vscode/settings.json):
```json
{
  "editor.formatOnSave": true,
  "editor.defaultFormatter": "esbenp.prettier-vscode",
  "[csharp]": {
    "editor.defaultFormatter": "ms-dotnettools.csharp"
  },
  "typescript.preferences.importModuleSpecifier": "relative"
}
```

**Launch Config** (.vscode/launch.json):
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": ".NET Core Launch (web)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/MXC.ComTools.Apps/bin/Debug/net10.0/MXC.ComTools.Apps.dll",
      "cwd": "${workspaceFolder}/MXC.ComTools.Apps",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      },
      "sourceFileMap": {
        "/Views": "${workspaceFolder}/Views"
      }
    }
  ]
}
```

### Visual Studio 2022+

1. Open `MXC.ComTools.Repo.sln`
2. Set **MXC.ComTools.Apps** as startup project
3. F5 to run with debugger
4. Tools → Options → Text Editor → C# → Code Style → Configure code style

---

## Testing

### Run Tests

```bash
# Unit tests (if available)
dotnet test

# Frontend tests
cd clientapp
yarn test
```

### Manual Testing Checklist

After setup, verify:

- [ ] Backend starts without errors: `dotnet run`
- [ ] Database connection works: Check logs for "Database connection successful"
- [ ] Frontend builds: `cd clientapp && yarn build`
- [ ] Login works: Access `/learn` → Login redirects → Returns to app
- [ ] API works: Media library loads featured content
- [ ] Admin access: Can access `/admin` routes

---

## Common Commands Reference

```bash
# Backend
dotnet watch                   # Start with hot reload
dotnet build                   # Build solution
dotnet clean                   # Clean build artifacts
dotnet ef migrations add Name  # Create migration
dotnet ef database update      # Apply migrations
dotnet ef database drop        # Drop database

# Frontend
cd clientapp
yarn install                   # Install dependencies
yarn dev                       # Start Vite dev server
yarn build                     # Production build
yarn lint                      # Run ESLint
yarn type-check                # TypeScript check

# Database
psql -U postgres              # Connect to PostgreSQL
\l                            # List databases
\c test_25_1               # Connect to database
\dt                           # List tables
\d "ContentBases"            # Describe table
```

---

## Cleaning Up Disk Space

Over time, build outputs, deployment folders, log files and caches accumulate —
especially after .NET version upgrades, branch switches or repeated deployments.
These can easily consume **1–2 GB** or more. A helper script at the repository root,
`cleanup.ps1`, removes these regenerable artifacts safely.

**Everything it deletes is git-ignored and can be recreated** with a normal build
(`dotnet build`), restore (`yarn install`), or client build. Source folders such as
`Publishing/` (which contain `.cs` files) are never touched.

### What it removes

| Category | Targets |
|---|---|
| .NET build output | every `bin/` and `obj/` next to a `.csproj` |
| Deployment output | `publish/` and `publish_bak/` folders |
| Frontend build/cache | `wwwroot/dist`, `clientapp/dist`, `.vite` caches |
| Logs | `MXC.ComTools.Apps/logs/*.txt` |
| IDE caches | `.vs/`, `TestResults/` |
| node_modules | only with `-IncludeNodeModules` (slow to reinstall) |
| Machine-wide caches | only with `-IncludeGlobalCaches` — NuGet caches + VS Code cache folders |

### Usage

```powershell
# Preview only – shows what would be deleted and how much space it frees
./cleanup.ps1 -DryRun

# Delete build/deploy/cache artifacts (keeps node_modules)
./cleanup.ps1

# Full cleanup including node_modules, without the confirmation prompt
./cleanup.ps1 -IncludeNodeModules -Force

# Also clear machine-wide NuGet + VS Code caches (affects all projects)
./cleanup.ps1 -IncludeGlobalCaches
```

**Parameters:**
- `-DryRun` – report the reclaimable space without deleting anything
- `-IncludeNodeModules` – also delete `node_modules` (requires `yarn install` afterwards)
- `-IncludeGlobalCaches` – also clear the user-profile NuGet caches and VS Code cache
  folders. These are machine-wide (all projects) but fully safe: NuGet re-downloads on
  the next restore and VS Code rebuilds its cache on restart. Equivalent NuGet command:
  `dotnet nuget locals all --clear`.
- `-Force` – skip the confirmation prompt

**Notes:**
- Close Visual Studio / stop `dotnet` before running so locked `bin` files can be removed.
  Any locked item is reported and skipped rather than aborting the whole run.
- After cleanup, `dotnet build` regenerates `bin/obj`, and `yarn install` (in
  `clientapp`) restores `node_modules` if you removed it.

---

## Next Steps

- **[Architecture Overview](./ARCHITECTURE.md)** - Learn about the system design
- **[Deployment Guide](./DEPLOYMENT.md)** - Deploy to Cloud Foundry
- **[Site Retirement Guide](./SITE_RETIREMENT_GUIDE.md)** - Manage site lifecycle and retirement
- **API Documentation** - Access Swagger at `/swagger` (if enabled)
- **Component Library** - Explore MUI components: https://mui.com/

---

**Need Help?**

- Check logs in `MXC.ComTools.Apps/logs/`
- Ask your colleagues for appsettings.Development.json or database dumps
- Review existing code for patterns
- Check browser DevTools Network tab for API errors

Happy Coding! 🚀
