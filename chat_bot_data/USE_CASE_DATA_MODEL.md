# USE_CASE_DATA_MODEL

## Ziel

Dieses Dokument beschreibt,
welche Daten für jeden Use Case benötigt werden.

Es dient als Grundlage für:

- MCP-Tool Design
- Agentenlogik
- Follow-up Verhalten
- Datenmodell-Lückenanalyse

---

# UC1 – Kurssuche

## Benötigte Informationen

- Kurstitel
- Kursbeschreibung
- Kursdauer
- Plattform
- Links

## Datenquellen

### LearnCourses

- Title
- Summary
- DurationInHours
- SourcePlatform
- TrainingUrl
- SsoTrainingUrl
- ClusterName
- SubClusterName

## Beziehungen

Keine

## Datenlücke

Keine bekannt.

---

# UC2 – Kursdetails

## Benötigte Informationen

Vollständige Kursinformationen
Lernziele
Plattform
Dauer
Links

## Datenquellen

LearnCourses

## Beziehungen

Keine

## Datenlücke

**Geklärt:** Die Lernziele liegen in `ContentJson`. `get_course` parst daraus das
Feld `description` und gibt es als `description` zurück. `LinkedInCourseDetailsJson`
wird nicht ausgelesen.

Einschränkung: `description` gibt es nur im Detail-Aufruf. Die Kurskarten aus
`search_courses` tragen ausschließlich `summary`.

# UC3 – Kursvergleich

## Benötigte Informationen

Beschreibung
Dauer
Plattform
Skilllevel
Kategorie

## Datenquellen

LearnCourses
Tags

## Datenlücke

**Geklärt: nein.** `SkillLevel` existiert nicht an `LearnCourse`. `search_courses`
nimmt einen Parameter `skillLevel` an, ignoriert ihn aber — ebenso `divisionId`.

Die Verwechslung ist naheliegend: `SkillLevel` gibt es im Schema, aber an
`EventEntries` (Veranstaltungen), nicht an `LearnCourses`.

Folge für den Vergleich: „Welcher Kurs eignet sich für Beginner?" ist nicht über
ein Datenfeld beantwortbar. Als Näherung bleiben Dauer, Cluster und der
Beschreibungstext.

Verfügbare Vergleichsmerkmale: `durationInHours`, `sourcePlatform`, `clusterName`,
`subClusterName`, `parentTagName`, `childTagName`, `instructor`, `language`,
`summary` und `description`.

---

# UC4 – Skillsuche

## Benötigte Informationen

- Skills
- Skillgruppen
- Beschreibungen
- Profilbezogene Skills
- Bereichsbezogene Skills

## Datenquellen

### Tags

- Name
- Type
- Group
- ParentTagId

### TagTranslations

- Name
- Description
- Language

### TagAssignments

- ContentId
- TagId

### LearnProfiles

- Title
- Summary

### LearnDepartments

### LearnDivisions

## Beziehungen

LearnProfile
→ TagAssignments
→ Tags

Tags
→ ParentTag
→ ChildTags

LearnDivision
→ LearnDepartment
→ LearnProfile

## Fragetypen

### Skills

Beispiel:

- Skills zu Kommunikation
- Data Analytics

### Profil → Skills

Beispiel:

- Welche Skills braucht Product Owner?

### Bereich → Skills

Beispiel:

- Welche Skills sind für PT/TS wichtig?

## Datenlücke

**Geprüft: Tags werden nicht am Profil gepflegt.** Es gibt keinen Pfad
`LearnProfile → TagAssignments → Tags` in den MCP-Tools. `get_profile_skills`
gruppiert stattdessen über `LearnCourse.ClusterName`, also über die Kurse des
Profils. Kurse ohne Cluster landen in `Allgemein`.

Die oben skizzierte Beziehung `LearnProfile → TagAssignments → Tags` ist damit
nicht implementiert. „Welche Skills braucht Product Owner?" wird über die Cluster
der zugeordneten Kurse beantwortet.

Bereich → Skills existiert ebenfalls nicht. `LearnDivision` führt nur über
`LearnDepartment` zu `LearnProfile`, nicht zu Tags.

---

# UC5 – Skilldetails

## Benötigte Informationen

Skillbeschreibung
Unter-Skills
Verwandte Skills
Kurse zum Skill

## Datenquellen

Tags
TagTranslations
TagAssignments
LearnCourses

### Beziehungen

Tag
→ ParentTag
→ ChildTags

## Datenlücke

`get_skill` liefert Beschreibung, Typ, Gruppe und den Elternnamen, aber **keine
Unter-Skills**. Die Hierarchie kommt nur aus `search_skills`.

„Verwandte Skills" existiert als Beziehung nicht. Näherung: Geschwisterknoten
unter demselben `parentTagId`.

Kurse zum Skill liefert `get_courses_by_tag` über zwei Wege gleichzeitig — die
Altspalten `ParentTagId` / `ChildTagId` und `TagAssignments`. Der Tag-Name wird
dabei **exakt** verglichen, ein Satzfragment findet nichts.

---

# UC6 – Profilsuche

## Benötigte Informationen

Profilname
Beschreibung
Bereich
Anzahl Kurse

## Datenquellen

LearnProfiles
LearnDepartments
LearnDivisions

## Beziehungen

LearnProfiles
LearnDepartments
LearnDivisions

---

# UC7 – Profildetails

## Benötigte Informationen

Profilbeschreibung
Bereich
Pflichtkurse
Optionalkurse
Skills

## Datenquellen

LearnProfiles
LearnDepartments
LearnDivisions
LearnProfileCourseAssignments
Tags
TagAssignments

## Beziehungen

Keine

## Datenlücke

**Geklärt: nein.** Skills liegen nicht am Profil, sondern werden aus dem
`ClusterName` der zugeordneten Kurse abgeleitet (siehe UC4).

Pflicht- und Optionalzahlen liefert das Profil direkt als
`requiredCoursesCount` / `optionalCoursesCount`.

# UC8 – Profilkurse

## Benötigte Informationen

- Profil
- Kursliste
- Pflichtkurse
- Optionale Kurse

## Datenquellen

### LearnProfiles

### LearnProfileCourseAssignments

### LearnCourses

## Beziehungen

LearnProfile
→ LearnProfileCourseAssignments
→ LearnCourses

## Datenlücke

`LearnProfileCourseAssignments` trägt `RequirementType` mit vier Werten:
`Mandatory`, `Optional`, `Recommended` und ein Standardfall. Nach außen werden sie
als `Required`, `Optional`, `Recommended` und `Assigned` ausgegeben. Eine reine
Pflicht/Optional-Trennung verliert `Recommended`.

Die Kurszuordnung liefert nur `courseId`, `courseTitle`, `requirementType`,
`sortOrder` und `deepLink`. Dauer und Plattform fehlen und brauchen je Kurs einen
`get_course`-Aufruf.

---

# UC9 – Lernempfehlung

## Benötigte Informationen

Aktuelles Ziel
Gewünschtes Ziel
Interessen

## Datenquellen

Keine direkte Datenquelle

## Beziehungen

## Datenlücke

Nur Klärungsdialog

---

# UC10 – Lernpfad

## Benötigte Informationen

Startprofil
Zielprofil
Zielkurse

## Datenquellen

Startprofil
Zielprofil
Zielkurse

## Beziehungen

## Datenlücke

Kein echter Skill-Gap vorhanden — und die Daten geben auch keinen her:

- kein `SkillLevel` an den Kursen (UC3)
- keine Skills am Profil, nur Cluster der Kurse (UC4, UC7)
- keine Vorgänger/Nachfolger-Beziehung zwischen Kursen
- kein Fortschritt je Profil (UC11)

Machbar ist damit ein Lernpfad als Kursliste des Zielprofils, geordnet nach
`sortOrder` und `requirementType` — die Pflichtkurse zuerst. Die Differenz zum
Startprofil lässt sich über die Kurs-IDs beider Profile bilden.

---

# UC11 – Meine Inhalte

## Benötigte Informationen

- Begonnene Kurse
- Abgeschlossene Kurse
- Bookmarks
- Lernfortschritt

## Datenquellen

### UserCompletions

### UserBookmarks

### LearnCourses

### LearnProfiles

## Datenlücke

Benötigt Benutzerkontext.
Nicht für anonyme Nutzer verfügbar.

Ohne Bearer-Token antworten `get_my_progress` und `get_my_bookmarks` mit HTTP 200
und einem `error`-Feld statt mit 401.

**Lernfortschritt pro Profil fehlt.** `UserCompletions` wird nur gegen alle Kurse
der Site gerechnet; `profileCompletionRates` enthält einen einzigen Eintrag für
`learn-skills`. Eine Quote je Lernprofil müsste über
`LearnProfileCourseAssignments` neu berechnet werden.




# UC12 – Bereichssuche

## Benötigte Informationen

Bereiche
Abteilungen
Profile
Skills
Kurse

## Datenquellen

LearnDivisions
LearnDepartments
LearnProfiles
LearnCourses
Tags

## Beziehungen

Division
→ Department
→ Profile

## Datenlücke

Es gibt **keine** Beziehung Division → Kurse und keine Division → Skills.
`search_courses` nimmt `divisionId` an, ignoriert es aber, weil das Feld an
`LearnCourse` fehlt.

Beantwortbar: Bereiche, Abteilungen und die Profile eines Bereichs
(`search_profiles(divisionId)`).
Nicht beantwortbar: „Anzahl verfügbarer Inhalte" je Bereich und
„Zugehörige Skills" je Bereich — beides nur indirekt über die Kurse der Profile
des Bereichs.

---

# UC13 – Collections

## Benötigte Informationen

Sammlungen
Enthaltene Inhalte
Sammlungsbeschreibung

## Datenquellen

ContentCollections
LearnCourses
LearnProfiles

## Beziehungen

## Datenlücke

**Offen und für den Agenten blockierend.** `search_collections` gibt nur
`id`, `title`, `summary`, `itemCount`, `collectionType` und `deepLink` zurück.
Die Verknüpfungstabelle zwischen `ContentCollections` und den Inhalten wird von
keinem MCP-Tool ausgeliefert.

Folge: „Enthaltene Themen" und „Welche Lernsammlung passt zu Projektmanagement?"
sind nicht datenbasiert beantwortbar. Möglich ist nur ein Textabgleich auf Titel
und Beschreibung der Sammlungen, da `search_collections` auch keinen
`query`-Parameter hat.

Welche ContentTypes erlaubt sind, geht aus `CollectionType` hervor, ist aber ohne
die Item-Liste nicht überprüfbar.

---



