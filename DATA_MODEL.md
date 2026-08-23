# Datenbankmodell — `mb_v25_v001`
(initially created by AI)

## Übersicht

| Eigenschaft       | Wert                         |
|-------------------|------------------------------|
| Schema            | `mb_v25_v001`                |
| PostgreSQL Version | 17.7                        |
| Dump-Version      | pg_dump 17.9 (Homebrew)      |
| Eigentümer        | `comtools`                   |
| ORM               | Entity Framework Core (EF Core) |
| Authentifizierung | ASP.NET Core Identity        |

Die Datenbank ist das Backend eines **Content-Management-Systems** (ComTools). Sie verwaltet mehrsprachige Inhalte (Artikel, Blogposts, Events, Kurse, Media, etc.), ein Lernmanagement-System sowie Benutzer und Berechtigungen.

---

## Tabellenübersicht

| Tabelle | Beschreibung | Primärschlüssel |
|---------|-------------|-----------------|
| `AppUsers` | Benutzerkonten (ASP.NET Identity) | `Id` |
| `Articles` | Artikel-Inhalte | `BaseEntryId` |
| `BlogPosts` | Blog-Beiträge | `BaseEntryId` |
| `Bookmarks` | Lesezeichen-Inhaltstyp | `BaseEntryId` |
| `Channels` | Inhaltskanäle | `BaseEntryId` |
| `Comments` | Kommentare zu Inhalten | `BaseEntryId` |
| `ContentBases` | **Zentrale Inhaltsbasis** (TPH-Basisklasse) | `BaseEntryId` |
| `ContentCollections` | Inhaltssammlungen (Nutzer- oder System-Playlisten) | `BaseEntryId` |
| `ContentTypeDefinitions` | Metadatendefinitionen für Inhaltstypen | `Id` |
| `DataSources` | Datenquellen für Inhalte | `BaseEntryId` |
| `EventEntries` | Veranstaltungen / Events | `BaseEntryId` |
| `ImageJobEntries` | Asynchrone Bildverarbeitungs-Jobs | `ImageJobEntryId` |
| `LearnCourseTypes` | Kurstypen im Lernmodul | `BaseEntryId` |
| `LearnCourses` | Lernkurse (intern & LinkedIn Learning etc.) | `BaseEntryId` |
| `LearnDepartmentProfileAssignments` | Zuordnung Abteilungen ↔ Lernprofile (n:m) | `ProfileId`, `DepartmentId` |
| `LearnDepartments` | Abteilungen im Lernmodul | `BaseEntryId` |
| `LearnDivisions` | Divisionen im Lernmodul | `BaseEntryId` |
| `LearnProfileCourseAssignments` | Zuordnung Lernprofile ↔ Kurse (n:m) | `ProfileId`, `CourseId` |
| `LearnProfiles` | Lernprofile (Curriculum pro Abteilung) | `BaseEntryId` |
| `MediaEntries` | Medieninhalte (Videos, Audio etc.) | `BaseEntryId` |
| `MicroStories` | Micro-Story-Inhaltstyp | `BaseEntryId` |
| `ObjectPermissions` | Objektbezogene Berechtigungen | `PermissionId` |
| `Podcasts` | Podcast-Episoden | `BaseEntryId` |
| `RegisteredPlaces` | Importierte Orte/Spaces (z. B. Jive-Gruppen) | `PlaceId` |
| `RoleClaims` | ASP.NET Identity: Claims je Rolle | `Id` |
| `Roles` | ASP.NET Identity: Rollen | `Id` |
| `SiteCategories` | Kategorien innerhalb einer Site | `BaseEntryId` |
| `SiteDefinitions` | Site-Konfigurationen | `BaseEntryId` |
| `SiteSections` | Seitenabschnitte einer Site | `BaseEntryId` |
| `TagAssignments` | Zuordnung von Tags zu Inhalten | `Id` |
| `TagTranslations` | Sprachspezifische Tag-Übersetzungen | `Id` |
| `Tags` | Tags / Taxonomie | `BaseEntryId` |
| `UserBookmarks` | Nutzer-Lesezeichen | `Id` |
| `UserClaims` | ASP.NET Identity: Nutzer-Claims | `Id` |
| `UserCompletions` | Kursabschlüsse / Lernfortschritt | `Id` |
| `UserLikes` | Nutzer-Likes auf Inhalte | `Id` |
| `UserLogins` | ASP.NET Identity: Externe Login-Provider | `LoginProvider`, `ProviderKey` |
| `UserRatings` | Nutzer-Bewertungen zu Inhalten | `Id` |
| `UserRoles` | ASP.NET Identity: Nutzer ↔ Rollen (n:m) | `UserId`, `RoleId` |
| `UserTokens` | ASP.NET Identity: Auth-Tokens | `UserId`, `LoginProvider`, `Name` |
| `__EFMigrationsHistory` | EF Core Migrationshistorie | `MigrationId` |

---

## Detaillierte Tabellenbeschreibungen

### `ContentBases` — Zentrale Inhaltsbasis

Die wichtigste Tabelle des Systems. Alle inhaltlichen Entitäten erben über das **Table-Per-Hierarchy (TPH)**-Muster von dieser Tabelle. Das `Discriminator`-Feld bestimmt den konkreten Inhaltstyp.

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | Primärschlüssel |
| `Discriminator` | `varchar(21)` | Inhaltstyp-Unterscheidung (TPH) |
| `SiteUrl` | `text` | Zugehörige Site |
| `ChannelUrl` | `text` | Zugehöriger Kanal |
| `ReadableId` | `text` | Lesbarer Bezeichner (Slug) |
| `ContentId` | `text` | Externe Inhalts-ID |
| `EntryType` | `integer` | Enum: Inhaltstyp |
| `Overline` | `text` | Überzeile |
| `Title` | `text` | Titel |
| `Summary` | `text` | Zusammenfassung |
| `Language` | `integer` | Hauptsprache (Enum) |
| `TeaserImageName` | `text` | Vorschau-Bild |
| `HeroVisualName` | `text` | Hero-Bild |
| `ContentJson` | `text` | Vollständiger Inhalt (JSON) |
| `InteractionJson` | `text` | Interaktionsdaten (JSON) |
| `TagsJson` | `text` | Tags (JSON) |
| `LanguageVersionsJson` | `text` | Sprachvarianten (JSON) |
| `ImageAssetsJson` | `text` | Bild-Assets (JSON) |
| `AttachmentsJson` | `text` | Anhänge (JSON) |
| `VideoAssetJson` | `text` | Video-Asset (JSON) |
| `ShortUrl` | `text` | Kurz-URL |
| `LiveUrl` | `text` | Live-URL |
| `JsonPermissions` | `text` | Berechtigungen (JSON) |
| `CreatedAt` | `timestamptz` | Erstellungsdatum |
| `CreatedById` | `text` | Erstellt von (User-ID) |
| `CreatedByName` | `text` | Erstellt von (Name) |
| `ModifiedAt` | `timestamptz` | Änderungsdatum |
| `ModifiedById` | `text` | Geändert von (User-ID) |
| `PublishedAt` | `timestamptz` | Veröffentlichungs­datum |
| `PublishVersion` | `integer` | Versionsnummer |
| `LiveStatus` | `integer` | Veröffentlichungs­status (Enum) |
| `DisplayDate` | `timestamptz` | Anzeige­datum |
| `PlannedLiveDate` | `timestamptz` | Geplante Veröffentlichung |
| `PlannedOfflineDate` | `timestamptz` | Geplante Deaktivierung |
| `MarkAsNew` | `boolean` | Als neu markiert |
| `MarkAsEditorialPick` | `boolean` | Redaktionelle Empfehlung |
| `EditorialPickUntil` | `timestamptz` | Redaktions-Pick bis |
| `MarkAsUpdated` | `boolean` | Als aktualisiert markiert |
| `IsVisible` | `boolean` | Sichtbar |
| `IsDeleted` | `boolean` | Soft-Delete |
| `SearchVector` | `tsvector` | Volltext-Suchindex |
| `ParentTagId` | `text` | FK → `Tags` — **Hauptkategorie** (älteres Einzelwert-Muster) |
| `ChildTagId` | `text` | FK → `Tags` — **Unterkategorie** (älteres Einzelwert-Muster) |
| `NeedsStatusSync` | `boolean` | Statussync erforderlich |
| `IsPreview` | `boolean` | Preview-Modus |
| `LayoutJson` | `text` | Layout-Konfiguration (JSON) |

---

### `AppUsers` — Benutzerkonten

Erweiterte ASP.NET Core Identity Benutzertabelle mit Zusatzfeldern für das Portal.

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `Id` | `text` | PK |
| `UserId` | `text` | Externer Nutzer-Identifier |
| `FirstName` | `text` | Vorname |
| `LastName` | `text` | Nachname |
| `DisplayName` | `text` | Anzeigename |
| `AvatarUrl` | `text` | Avatar-URL |
| `PermissionRole` | `integer` | Berechtigungsrolle (Enum) |
| `IsMasterAdmin` | `boolean` | Master-Admin |
| `IsChannelCreator` | `boolean` | Kanal-Ersteller |
| `IsChannelOwner` | `boolean` | Kanal-Eigentümer |
| `IsChannelEditor` | `boolean` | Kanal-Editor |
| `IsDeleted` | `boolean` | Soft-Delete |
| `IsVisible` | `boolean` | Sichtbar |
| `HasTermsApproved` | `boolean` | AGB akzeptiert |
| `HistoryIsEnabled` | `boolean` | Verlauf aktiviert |
| `LastVisitDate` | `timestamptz` | Letzter Besuch |
| `AccountCreateDate` | `timestamptz` | Kontoerstellungs­datum |
| *Standard Identity-Felder* | — | `UserName`, `Email`, `PasswordHash`, `SecurityStamp`, `TwoFactorEnabled`, `LockoutEnd`, etc. |

---

### `Articles` — Artikel

Importierte oder redaktionell erstellte Artikel.

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | PK |
| `AuthorName` | `text` | Autorenname |
| `AuthorJiveId` | `text` | Jive-ID des Autors |
| `PlaceId` | `text` | Zugeordneter Ort/Space |
| `CategoryName` | `text` | Kategoriename |
| `CategoryId` | `text` | Kategorie-ID |
| `CategoryNumber` | `integer` | Kategorienummer |
| `Position` / `Level1`–`Level4` | `integer` | Hierarchische Position |
| `ChapterString` | `text` | Kapitel-String |
| `ReplyCount` | `integer` | Anzahl Antworten |
| `RestrictComments` | `boolean` | Kommentare gesperrt |
| `ImportJobId` | `text` | Import-Job Referenz |
| `ImportDate` | `timestamptz` | Import-Datum |
| `ArticleEditorDataJson` | `text` | Editor-Daten (JSON) |

---

### `BlogPosts` — Blog-Beiträge

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | PK |
| `AuthorId` | `text` | Autor-ID |
| `AuthorName` | `text` | Autorenname |
| `Text` | `text` | Textinhalt |
| `ArticleBody` | `text` | Artikel-Body |
| `MainImageId` | `text` | Hauptbild |
| `ReadingTimeMinutes` | `integer` | Lesezeit in Minuten |
| `Format` | `integer` | Format-Enum |
| `AllowComments` | `boolean` | Kommentare erlaubt |
| `WordCount` | `text` | Wortanzahl |
| `LayoutOptionsJson` | `text` | Layout-Optionen (JSON) |

---

### `EventEntries` — Veranstaltungen

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | PK |
| `EventStartDate` | `timestamptz` | Startdatum |
| `EventEndDate` | `timestamptz` | Enddatum |
| `Location` | `text` | Ort |
| `OrganizerId` | `text` | Organisator-ID |
| `IsVirtual` | `boolean` | Virtuelle Veranstaltung |
| `JoinUrl` | `text` | Teilnahme-URL |
| `MaxParticipants` | `integer` | Max. Teilnehmer |
| `RegistrationType` | `text` | Anmeldetyp |
| `SpeakerIds` | `text[]` | Sprecher-IDs (Array) |
| `TypeId` / `TypeName` | `text` | Veranstaltungstyp |
| `CategoryId` / `CategoryTag` | `text` | Kategorie |
| `DurationInHours` | `text` | Dauer |
| `SkillLevel` | `integer` | Schwierigkeitsgrad |
| `TargetGroup` | `text` | Zielgruppe |

---

### `LearnCourses` — Lernkurse

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | PK |
| `TypeId` / `TypeName` | `text` | Kurstyp |
| `Instructor` | `text` | Kursleiter |
| `DurationInHours` | `text` | Dauer |
| `TrainingUrl` | `text` | Trainings-URL |
| `SsoTrainingUrl` | `text` | SSO-Trainings-URL |
| `ClusterId` / `ClusterName` | `text` | Lerncluster |
| `SubClusterId` / `SubClusterName` | `text` | Lern-Subcluster |
| `SourcePlatform` | `text` | Quellplattform (LinkedIn, intern, etc.) |
| `ExternalCourseId` | `text` | Externe Kurs-ID |
| `LinkedInCourseDetailsJson` | `text` | LinkedIn-Details (JSON) |
| `LmsTags` | `text` | LMS-Tags |
| `IsActive` | `boolean` | Aktiv |
| `IsUpdated` | `boolean` | Aktualisiert |

---

### `MediaEntries` — Medieninhalte

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | PK |
| `SeriesId` / `SeriesName` | `text` | Medien-Serie |
| `ChannelId` / `ChannelName` | `text` | Kanal |
| `MediaAssetJson` | `text` | Media-Asset (JSON) |
| `MediaItemUrl` | `text` | Medien-URL |
| `Year` | `text` | Erscheinungsjahr |
| `ExternalUrl` | `text` | Externe URL |
| `ExternalVideoId` | `text` | Externe Video-ID |
| `MediaSource` | `integer` | Medienquelle (Enum) |
| `Markets` | `text` | Märkte |
| `LegalAssessment` | `text` | Rechtliche Bewertung |
| `MusicLicenses` | `text` | Musiklizenzen |

---

### `Podcasts` — Podcast-Episoden

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | PK, FK → `ContentBases` (CASCADE) |
| `PodcastSeriesId` | `text` | Serie |
| `EpisodeNumber` | `text` | Episodennummer |
| `SeasonNumber` | `text` | Staffelnummer |
| `AudioUrl` | `text` | Audio-URL |
| `Duration` | `interval` | Dauer |
| `AudioFileSize` | `text` | Dateigröße |
| `AudioMimeType` | `text` | MIME-Typ |
| `SpeakersJson` | `text` | Sprecher (JSON) |
| `SegmentsJson` | `text` | Segmente (JSON) |
| `Transcript` | `text` | Transkript |
| `SpotifyUrl` | `text` | Spotify-Link |
| `ApplePodcastUrl` | `text` | Apple Podcasts-Link |

---

### `Tags` & Tag-System

Das Tag-System besteht aus drei Tabellen und unterstützt hierarchische, mehrsprachige Taxonomie.

**`Tags`** — Taxonomie-Einträge

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | PK |
| `Name` | `text` | Interner Name |
| `Type` | `integer` | Tag-Typ (Enum) |
| `Group` | `text` | Tag-Gruppe |
| `SiteUrl` | `text` | Zugehörige Site |
| `ParentTagId` | `text` | FK → `Tags` (Hierarchie, RESTRICT) |
| `DepartmentId` | `text` | Abteilungszuordnung |
| `DefaultLanguage` | `integer` | Standardsprache |
| `IsDeleted` / `IsVisible` | `boolean` | Soft-Delete / Sichtbarkeit |

**`TagTranslations`** — Sprachspezifische Tag-Namen

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `Id` | `text` | PK |
| `TagId` | `text` | FK → `Tags` (CASCADE) |
| `Language` | `integer` | Sprache (Enum) |
| `Name` | `varchar(100)` | Übersetzter Name |
| `Description` | `varchar(500)` | Beschreibung |

**`TagAssignments`** — Tag–Inhalt-Zuordnungen

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `Id` | `text` | PK |
| `TagId` | `text` | FK → `Tags` (CASCADE) |
| `ContentId` | `text` | Referenzierter Inhalt |
| `AssignmentType` | `integer` | Zuordnungstyp (Enum) |
| `AssignmentContext` | `varchar(50)` | Kontext der Zuordnung |
| `IsAutoAssigned` | `boolean` | Automatisch zugewiesen |
| `Weight` | `double precision` | Gewichtung |
| `SortOrder` | `integer` | Sortierreihenfolge |
| `AssignedAt` | `timestamptz` | Zeitpunkt der Zuordnung |
| `SiteUrl` | `text` | Site-Kontext |

---

### `SiteDefinitions` — Site-Konfigurationen

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | PK |
| `AppBarTitle` | `text` | App-Bar Titel |
| `WindowTitle` | `text` | Fenstertitel |
| `DefaultTheme` | `text` | Standard-Theme |
| `DefaultRoute` | `text` | Standard-Route |
| `ProviderName` | `text` | Auth-Provider |
| `HasSearch` / `HasAdminMenu` etc. | `boolean` | Feature-Flags |
| `LanguagesJson` | `text` | Unterstützte Sprachen (JSON) |
| `ContentTypeMappingsJson` | `text` | Inhaltstyp-Mappings (JSON) |
| `IsRetired` | `boolean` | Site deaktiviert |
| `RetiredRedirectUrl` | `text` | Weiterleitungs-URL |

---

### `SiteSections` — Seitenabschnitte

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `BaseEntryId` | `text` | PK, FK → `ContentBases` (CASCADE) |
| `SectionKey` | `text` | Eindeutiger Schlüssel des Abschnitts |
| `PageType` | `text` | Seitentyp |
| `OrderIndex` | `integer` | Reihenfolge |
| `ComponentKey` | `text` | Frontend-Komponente |
| `DataSourceIdentifier` | `text` | Datenquellen-Identifier |
| `DisplayConfigJson` | `text` | Anzeige-Konfiguration (JSON) |
| `LanguageContentsJson` | `text` | Mehrsprachige Inhalte (JSON) |

---

### `RegisteredPlaces` — Registrierte Orte/Spaces

Importierte Orte aus externen Systemen (z. B. Jive-Gruppen, Communities).

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `PlaceId` | `text` | PK |
| `BaseEntryId` | `text` | Inhaltsbasis-Referenz |
| `Name` | `text` | Ortsname |
| `PlaceType` | `text` | Ortstyp |
| `PlaceStatus` | `text` | Status |
| `IsClosedGroup` | `boolean` | Geschlossene Gruppe |
| `MainLanguage` | `integer` | Hauptsprache |
| `LiveSpaceUrl` | `text` | Live-URL des Spaces |
| `IsActiveForImport` | `boolean` | Aktiv für Import |
| `ViewCount` | `integer` | Aufrufzähler |
| `CategoryId` | `integer` | Kategorie-ID |

---

### `ContentTypeDefinitions` — Inhaltstyp-Definitionen

Konfigurationstabelle für dynamisch definierbare Inhaltstypen.

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `Id` | `varchar(100)` | PK |
| `DisplayName` | `varchar(200)` | Anzeigename |
| `Category` | `varchar(100)` | Kategorie |
| `EntryType` | `integer` | Enum-Wert |
| `EntityTypeName` | `varchar(200)` | .NET Typname |
| `FormComponentName` | `varchar(200)` | Frontend-Komponente |
| `IsActive` | `boolean` | Aktiv |
| `SupportsComments` / `SupportsLikes` / `SupportsVersioning` | `boolean` | Feature-Flags |
| `RequiresApproval` | `boolean` | Approval-Workflow |
| `ConfigurationJson` | `jsonb` | Typ-spezifische Konfiguration |

---

### Benutzer-Interaktionstabellen

| Tabelle | Beschreibung | Schlüsselfelder |
|---------|-------------|-----------------|
| `UserBookmarks` | Lesezeichen eines Nutzers | `UserId`, `BaseEntryId`, `CollectionId` |
| `UserLikes` | Likes eines Nutzers auf Inhalte | `UserId`, `BaseEntryId` |
| `UserRatings` | Bewertungen (Score + Kommentar) | `UserId`, `BaseEntryId`, `Score` |
| `UserCompletions` | Abschlüsse / Lernfortschritt | `UserId`, `BaseEntryId`, `ProgressPercentage` |
| `ContentCollections` | Inhaltssammlungen | `UserId`, `CollectionType`, `IsPublic` |

---

### ASP.NET Identity Tabellen

| Tabelle | Beschreibung |
|---------|-------------|
| `Roles` | Rollendefinitionen |
| `RoleClaims` | Claims je Rolle |
| `UserClaims` | Claims je Nutzer |
| `UserLogins` | Externe Login-Provider (OAuth etc.) |
| `UserRoles` | Nutzer ↔ Rollen Zuordnung |
| `UserTokens` | Auth-Tokens (Refresh Token, etc.) |

---

## Beziehungsdiagramm

```
AppUsers ─────────────────────────────────────┐
  │ 1:n UserClaims                             │
  │ 1:n UserLogins                             │
  │ 1:n UserTokens                             │
  │ n:m Roles (via UserRoles)                  │
  │ 1:n ObjectPermissions                      │
  │ 1:n UserBookmarks                          │
  │ 1:n UserLikes                              │
  │ 1:n UserRatings                            │
  └─ 1:n UserCompletions                       │
                                               │
ContentBases ◄── Podcasts (FK, CASCADE)        │
ContentBases ◄── SiteSections (FK, CASCADE)    │
ContentBases ──► Tags (ParentTagId, SET NULL)  │
ContentBases ──► Tags (ChildTagId, SET NULL)   │
                                               │
Tags ──────────────────────────────────────────┤
  │ 1:n TagTranslations (CASCADE)              │
  │ 1:n TagAssignments (CASCADE)               │
  └─ self-ref: ParentTagId (RESTRICT)          │
                                               │
LearnDivisions                                 │
  └─ 1:n LearnDepartments (CASCADE)            │
       └─ n:m LearnProfiles                    │
              (via LearnDepartmentProfile-      │
               Assignments, RESTRICT)          │
       LearnProfiles ──► LearnDepartments      │
              (DepartmentId, RESTRICT)         │
       LearnProfiles                           │
         └─ n:m LearnCourses                   │
                (via LearnProfileCourse-        │
                 Assignments, CASCADE)          │
                                               │
Roles ─────────────────────────────────────────┘
  └─ 1:n RoleClaims (CASCADE)
```

---

## Design-Muster

### 1. Table-Per-Hierarchy (TPH) Vererbung
`ContentBases` dient als Basisklasse für alle Inhaltstypen. Das `Discriminator`-Feld kennzeichnet den konkreten Typ. Spezialisierte Tabellen wie `BlogPosts`, `Articles`, `EventEntries`, `MediaEntries`, `Channels` etc. teilen sich die Basis-PK `BaseEntryId`.

### 2. Soft-Delete
Alle Hauptentitäten verwenden `IsDeleted` und `IsVisible`-Flags statt physischem Löschen.

### 3. Audit-Felder
Standardisiertes Muster: `CreatedAt`, `CreatedById`, `CreatedByName`, `ModifiedAt`, `ModifiedById`, `ModifiedByName`.

### 4. Mehrsprachigkeit
Sprachspezifische Inhalte werden als JSON in `*Json`-Felder gespeichert (z. B. `LanguageContentsJson`, `DescriptionsByLanguageJson`). `Language` und `Languages` werden als Integer-Enum kodiert.

### 5. Volltext-Suche
`ContentBases.SearchVector` (Typ `tsvector`) mit GIN-Index für PostgreSQL Volltextsuche.

### 6. LiveStatus / Workflow
`LiveStatus` (Enum-Integer) steuert den Veröffentlichungs-Workflow. Unterstützt durch `PlannedLiveDate` und `PlannedOfflineDate` für zeitgesteuerte Veröffentlichungen.

### 7. Dual-Tagging-System (zwei koexistierende Muster)

Es gibt **zwei verschiedene Mechanismen** zur Verknüpfung von Tags mit Inhalten — sie dürfen nicht verwechselt werden:

| Muster | Felder | Kardinalität | Verwendung |
|--------|--------|-------------|------------|
| **Älteres Einzelwert-FK-Muster** | `ContentBase.ParentTagId` → `ParentTag` (Hauptkategorie) und `ContentBase.ChildTagId` → `ChildTag` (Unterkategorie) | 1:1 pro Ebene | `LearnCourse`, und andere Typen, die genau eine Kategorie und eine Unterkategorie besitzen |
| **Neues flexibles Muster** | `TagAssignments`-Tabelle mit `TagAssignment.ContentId` → `ContentBase.BaseEntryId` | 1:n (beliebig viele Tags) | Neues System für Multi-Tag-Szenarien, z. B. Markt-Tags, Agentur-Tags, Keyword-Tags |

**Wichtig für Queries und Exporte:**
- Kategorie/Unterkategorie eines `LearnCourse` lesen: `.Include(c => c.ParentTag).Include(c => c.ChildTag)` — **nicht** über `TagAssignments` filtern.
- `TagAssignments` enthält für `LearnCourse`-Einträge typischerweise keine Kategorie-/Subkategorie-Einträge.
- `TagAssignment.ContentId` referenziert `ContentBase.BaseEntryId` (interne PK) — **nicht** `ContentBase.ContentId` (externe Import-ID).

---

## Indizes

### `ContentBases`
| Index | Felder | Typ | Zweck |
|-------|--------|-----|-------|
| `IX_ContentBases_BaseEntryId` | `BaseEntryId` | UNIQUE BTREE | PK-Lookup |
| `IX_ContentBases_SearchVector` | `SearchVector` | GIN | Volltext-Suche |
| `IX_ContentBases_SiteUrl_Published` | `SiteUrl`, `LiveStatus`, `IsVisible` | BTREE | Inhaltsabfragen |
| `IX_ContentBases_SiteUrl_Discriminator` | `SiteUrl`, `Discriminator` | BTREE | Typ-gefilterter Abruf |
| `IX_ContentBases_DisplayDate_DESC` | `DisplayDate DESC` | BTREE | Zeitliche Sortierung |
| `IX_ContentBases_FeaturedMedia_Complete` | `SiteUrl`, `IsDeleted`, `IsVisible`, `MarkAsEditorialPick`, `DisplayDate DESC` | BTREE | Featured-Content-Queries |
| `IX_ContentBases_EditorialPick` | `MarkAsEditorialPick` | BTREE | Redakt. Empfehlungen |

### `TagAssignments`
| Index | Felder | Typ |
|-------|--------|-----|
| `IX_TagAssignments_ContentId_TagId` | `ContentId`, `TagId` | UNIQUE BTREE |
| `IX_TagAssignments_ContentId_AssignmentType` | `ContentId`, `AssignmentType` | BTREE |
| `IX_TagAssignments_TagId` | `TagId` | BTREE |

### Weitere bemerkenswerte Indizes
- `IX_ObjectPermissions_ObjectId_UserId` — Unique; verhindert doppelte Berechtigungen
- `IX_ImageJobEntries_ImageUrl_SiteId` — Unique; verhindert doppelte Jobs
- `IX_LearnProfileCourseAssignments_CourseId` — Kurs-Lookup in Profil-Zuordnungen
- `UserNameIndex` / `EmailIndex` / `RoleNameIndex` — Identity-Indizes
