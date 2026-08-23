# Learn Skills Chatbot – Use Cases (UC1–UC13)

# UC1 – Kurssuche

## Benutzerziel
Passende Kurse zu einem bestimmten Thema finden.

Kurse nach Thema finden
Kurse nach Stichwort finden
Kurse priorisieren

## Agentenlogik

1. Suchthema erkennen
2. Entity extrahieren
3. search_courses ausführen
4. Ergebnisse bewerten
5. Passende Kurse priorisieren
6. Kurse zusammenfassen

## Antwort

Kurskarten
- Titel
- Dauer
- Plattform
- Kurzbeschreibung
- Deep Link

## MCP-Tools

search_courses(query)

---

# UC2 – Kursdetails

## Benutzerziel

Benutzer möchte Details zu einem ausgewählten Kurs.

Kurs anhand ID laden
Kursdetails anzeigen

## Agentenlogik

1. Aktiven Kurskontext prüfen
2. LastCourseId verwenden
3. get_course ausführen
4. Kursdetails strukturieren
5. Ergebnisse darstellen

## Output 

Kursdetails

## Antwort

- Kursname
- Beschreibung
- Lernziele
- Dauer
- Plattform
- Deep Link

## MCP-Tools

search_courses(query) — um den Kurs anhand des Namens zu finden
get_course(courseId) — erwartet eine ID, löst keine Titel auf

Hinweis: Die Lernziele liefert `get_course` im Feld `description`. Es wird aus
`ContentJson` geparst und ist nur im Detail-Aufruf enthalten, nicht in den
Suchergebnissen.

---

# UC3 – Kursvergleich

## Benutzerziel

Kurse vergleichen.

Mehrere Kurse finden
Kursdetails vergleichen

## Agentenlogik

1. Kurse finden
2. Kursdetails laden
3. Unterschiede analysieren
4. Empfehlung begründen

## Antwort

Vergleichstabelle und Empfehlung.

## MCP-Tool

search_courses(query)/wenn keine Follow-up
get_course(courseId)

---

# UC4 – Skillsuche

## Benutzerziel

Benutzer möchte verstehen, welche Skills zu einem Bereich, Thema oder Beruf gehören.

## Agentenlogik

1. Thema, Bereich, Beruf oder Profil erkennen
2. Relevante Skillinformationen ermitteln
3. Skill-Struktur laden
4. Skills gruppieren
5. Skill-Struktur zusammenfassen
6. Skill-Liste darstellen

## Output 

Skills
Kompetenzen
Skill-Struktur

## Antwort

- Skillgruppen
- Zugehörige Skills
- Kurze Beschreibung

## MCP-Tools

search_skills() — ohne Parameter; liefert immer die vollständige Hierarchie.
Die Filterung auf ein Thema muss der Agent selbst vornehmen.

get_profile_skills(query) — für „Welche Skills braucht ein Product Owner?"

---

# UC5 – Skilldetails

## Benutzerziel

Benutzer möchte einen Skill genauer verstehen.

Skillbeschreibung
Unter-Skills
Verwandte Skills

## Agentenlogik

1. LastSkill prüfen
2. Skillinformationen laden

## Output

Skillbeschreibung
Verwandte Skills
Unter-Skills

## Antwort

- Skillname
- Beschreibung
- Verwandte Skills

## MCP-Tools

antwort generieren 
get_skill(skillName) — akzeptiert ID, Name oder übersetzten Namen

Hinweis: `get_skill` liefert **keine** Unter-Skills. Für „Welche Skills umfasst
Kommunikation?" ist `search_skills()` nötig und der Teilbaum wird daraus gelesen.

---

# UC6 – Profilsuche

## Benutzerziel

Benutzer möchte Profile finden oder verstehen.

Profile suchen
Profile filtern

## Agentenlogik

1. Profil-Suchbegriff erkennen
2. search_profiles ausführen
3. Ergebnisse bewerten

Wenn mehrere Profile gefunden:
4. Profiliste anzeigen

Wenn genau ein Profil gefunden:
4. Profil anzeigen
5. Profilkontext speichern

## Antwort

Profiliste
oder
Einzelprofil

## MCP- Tools

search_profiles(query)

---

# UC7 - Profildetails

## Benutzerziel

Der Benutzer möchte Details zu einem bereits gefundenen Profil anzeigen.

search_profiles(query)

Auch als follow-Up frage

## Agentenlogik

1. Aktiven Profilkontext prüfen
2. LastProfileId verwenden
3. get_profile ausführen
4. Profilinformationen aufbereiten
5. Ergebnis darstellen

## Antwort

- Profilname
- Profilbeschreibung
- Bereich / Division (falls vorhanden)
- Anzahl Pflichtkurse
- Anzahl optionaler Kurse
- Hinweis auf verfügbare Kurse

## MCP-Tools

search_profiles(query) — löst den Profilnamen zu einer ID auf
get_profile(profileId) — erwartet zwingend eine ID
get_profile_skills(query) — akzeptiert Name oder ID

Hinweis: Die „Skills" eines Profils sind der `ClusterName` seiner Kurse, keine
Tags. Kurse ohne Cluster erscheinen in einer Gruppe namens `Allgemein`.

---

# UC8 - Profilkurse

## Benutzerziel

Benutzer möchte die Kurse eines bereits ausgewählten Profils anzeigen.

## Agentenlogik

1. Aktiven Profilkontext prüfen
2. LastProfileId verwenden
3. get_profile ausführen
4. Pflicht- und optionale Kurse trennen
5. Kurse darstellen

## Antwort

Einleitungssatz
Kurskarten
Abschlusssatz

## MCP-Tools

get_profile(profileId) — enthält `courseAssignments` mit `requirementType`
get_profile_skills(query) — dieselben Kurse, nach Skill gruppiert

Hinweis: `get_profile_courses` existiert nicht. Die Kurszuordnung kommt aus
`get_profile`. Sie trägt nur Titel (`courseTitle`), Verbindlichkeit und Link —
Dauer und Plattform erfordern je Kurs einen `get_course`-Aufruf.

`requirementType` hat vier Werte: `Required`, `Optional`, `Recommended`,
`Assigned`. „Pflicht vs. optional" ist also keine Ja/Nein-Trennung.

---

# UC9 – Lernempfehlung (Klärungsdialog)

## Benutzerziel

Benutzer möchte passende Lernempfehlungen erhalten, hat aber noch kein Ziel oder Profil angegeben.

## Agentenlogik

1. Prüfen, ob aktuelles Profil oder Zielprofil bekannt ist.
2. Falls nicht bekannt:
   - gezielte Rückfragen stellen.
3. Benötigte Informationen sammeln.
4. Empfehlung an UC10 übergeben.

## Antwort

Für eine passende Empfehlung brauche ich mehr Informationen.

- Welches Profil hast du aktuell?
- Oder welches Profil möchtest du erreichen?
- Alternativ: Für welches Thema interessierst du dich?

## MCP-Tools

Keine

---

# UC10 – Lernpfad zum Zielprofil

## Benutzerziel

Einen datenbasierten Lernpfad zu einem gewünschten Zielprofil erhalten.

Eine personalisierte Empfehlung ist nur möglich, wenn der MCP-Server dafür
authentifizierte Nutzerdaten liefert. Der Agent darf keine persönlichen Skills,
Ziele oder Lernstände annehmen oder aus der Chatnachricht als dauerhaftes
Nutzerprofil speichern.

## Agentenlogik

1. Startprofil bestimmen
2. Zielprofil bestimmen
3. Profile suchen
4. Profilkurse laden
5. Lernpfad ableiten
6. Kurse priorisieren

## Antwort

- Zielprofil
- Empfohlene Kurse
- Reihenfolge der Lernschritte
- Nächste Empfehlungen

Wenn `get_my_progress` und `get_my_bookmarks` erfolgreich authentifizierte Daten
liefern, dürfen abgeschlossene Kurse ausgeschlossen und gespeicherte Inhalte als
zusätzlicher Kontext berücksichtigt werden. Der Fortschritt ist jedoch nur
site-weit verfügbar, nicht pro Profil. Ohne diese Daten erstellt der Agent nur
einen allgemeinen, nicht-personalisierten Lernpfad aus dem Zielprofil.

## MCP-Tools

search_profiles(query) — Start- und Zielprofil auflösen
get_profile(profileId) — Kurse des Zielprofils

Hinweis: `get_profile_courses` existiert nicht. `get_my_progress` und
`get_my_bookmarks` sind nur mit gültigem Bearer-Token nutzbar. Fehlt der Token,
muss der Agent den personalisierten Teil überspringen und darf nicht behaupten,
den persönlichen Lernstand zu kennen.

---

# UC11 – Meine Inhalte

## Benutzerziel

Authentifizierte eigene Lerninhalte anzeigen. Dieser Use Case ist auf die Daten
begrenzt, die der MCP-Server tatsächlich bereitstellt: Bookmarks sowie den
site-weiten Lernfortschritt.

## Agentenlogik

1. get_my_progress ausführen
2. Begonnene Kurse laden
3. Abgeschlossene Kurse laden
4. Site-weite Fortschrittsquote auswerten
5. Ergebnisse darstellen

1. get_my_bookmarks ausführen
2. Gespeicherte Kurse laden
3. Gespeicherte Profile laden
4. Ergebnisse darstellen

## Antwort

- Begonnene Kurse
- Abgeschlossene Kurse
- Site-weite Fortschrittsquote

- Gespeicherte Kurse
- Gespeicherte Profile
- Direkte Links zu gespeicherten Inhalten

## MCP

get_my_bookmarks()
get_my_progress()

Hinweis: `get_my_courses` existiert nicht — begonnene und abgeschlossene Kurse
liefert `get_my_progress` in `inProgressCourses` und `completedCourses`.

Ein Fortschritt pro Profil, persönliche Skill-Gaps, persönliche Ziele und
personalisiertes Ranking sind nicht verfügbar. `profileCompletionRates` enthält
nur eine site-weite Quote für `learn-skills`.

Ohne gültigen Bearer-Token antworten beide Tools mit HTTP 200 und einem
`error`-Feld — nicht mit 401. Das muss der Aufrufer prüfen, sonst sieht es aus
wie „keine gespeicherten Inhalte".

---

# UC12 – Bereichssuche

## Benutzerziel

Benutzer möchte Inhalte, Skills, Profile oder Lernmöglichkeiten zu einem bestimmten Bereich finden.

## Agentenlogik

1. Bereich erkennen
2. Bereichsstruktur laden
3. Zugehörige Skills, Profile oder Inhalte ermitteln
4. Ergebnisse gruppieren
5. Bereichsübersicht darstellen

## Antwort

- Bereich
- Zugehörige Profile
- Zugehörige Skills
- Anzahl verfügbarer Inhalte


## MCP-Tools

GetDivisions() — Bereiche mit Abteilungen (PascalCase, nicht `get_divisions`)
search_profiles(divisionId) — Profile eines Bereichs

Hinweis: `list_areas`, `get_area_profiles`, `get_area_skills` und
`get_area_courses` existieren nicht.

**Datenlücke:** Kurse lassen sich nicht nach Bereich filtern. `search_courses`
nimmt `divisionId` an, ignoriert es aber, weil das Feld an `LearnCourse` fehlt.
„Zeige Inhalte aus dem Bereich Produktion" ist nur über die Profile des Bereichs
beantwortbar. Skills sind ebenfalls nicht bereichsbezogen ablegt — sie hängen an
Kursen und Tags, nicht an Divisions.

---

# UC13 – Sammlungen / Collections

## Benutzerziel

Benutzer möchte kuratierte Lernsammlungen oder Themenpakete entdecken.

## Agentenlogik

1. Suchthema erkennen
2. Collections laden
3. Relevante Sammlungen filtern
4. Ergebnisse priorisieren
5. Sammlungen darstellen

## Antwort

- Sammlungsname
- Beschreibung
- Anzahl Inhalte
- Enthaltene Themen

## MCP-Tools

search_collections(limit, offset)

Hinweis: `get_collection` und `get_collection_items` existieren nicht, und
`search_collections` hat **keinen** `query`-Parameter. Die Filterung nach Thema
muss der Agent auf den geladenen Sammlungen selbst vornehmen.

**Datenlücke:** „Enthaltene Themen" ist nicht beantwortbar. Die Antwort liefert
nur `itemCount`, nicht die enthaltenen Inhalte.

---