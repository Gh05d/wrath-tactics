# /release — Wrath Tactics Release Orchestrator

## Konfiguration (hardcoded)

- Remote: `origin`
- Repo: `Gh05d/wrath-tactics`
- Mod-Name: `Wrath Tactics`
- Nexus-URL: `https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005` (nach Nexus-Page-Creation echten Wert einsetzen)
- csproj: `WrathTactics/WrathTactics.csproj`
- Info.json: `WrathTactics/Info.json`
- Repository.json: `Repository.json`
- Build: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/ --nologo`
- Release-Zip: `WrathTactics/bin/WrathTactics-X.Y.Z.zip`

---

## Schritt 1: Release-Typ bestimmen

Lies `$ARGUMENTS`.

- Wenn `$ARGUMENTS` eines von `patch`, `minor`, `major` ist — verwende es direkt.
- Sonst: Frage den User: „Welcher Release-Typ? (patch / minor / major)"

---

## Schritt 2: Pre-flight-Checks

Führe alle Checks aus, bevor du irgendetwas änderst.

1. **Working Tree sauber?**
   ```
   git diff --quiet && git diff --cached --quiet
   ```
   Schlägt fehl: Abbruch mit „Fehler: Working Tree ist dirty. Bitte erst committen oder stashen."

2. **Auf master?**
   ```
   git rev-parse --abbrev-ref HEAD
   ```
   Nicht `master`: Abbruch mit „Fehler: Nicht auf master-Branch."

3. **Aktuelle Version lesen** aus `WrathTactics/WrathTactics.csproj`:
   ```
   grep -oP '<Version>\K[^<]+' WrathTactics/WrathTactics.csproj
   ```
   Gültige Form: `X.Y.Z` (drei Zahlen). Ungültig: Abbruch mit „Fehler: Keine gültige Semver-Version in csproj gefunden."

4. **Neue Version berechnen** anhand des Release-Typs:
   - `patch`: Z+1
   - `minor`: Y+1, Z=0
   - `major`: X+1, Y=0, Z=0

5. **Tag noch nicht vorhanden?**
   ```
   git rev-parse "vX.Y.Z" 2>/dev/null
   ```
   Existiert bereits: Abbruch mit „Fehler: Tag vX.Y.Z existiert bereits. Version prüfen."

---

## Schritt 3: Release Notes generieren

1. **Letzten Tag finden:**
   ```
   git describe --tags --abbrev=0 --match 'v*'
   ```
   Existiert kein Tag (erstes Release): alle Commits auf master als Basis nehmen.

2. **Commits seit letztem Tag lesen:**
   ```
   git log <letzter-tag>..HEAD --oneline
   ```

3. **`chore:`-Commits herausfiltern** (Versions-Bumps, Repository.json-Updates — kein Mehrwert für User).

4. **Release Notes erstellen (GitHub Markdown):**

   ```markdown
   ## What's New

   - <Bullet-Liste aus gefilterten Commits, als lesbarer Satz formuliert>

   ## Installation

   1. Download `WrathTactics-X.Y.Z.zip`
   2. Drag onto the Unity Mod Manager window, or extract into `{GameDir}/Mods/WrathTactics/`
   3. Enable in Unity Mod Manager

   ## Requirements

   - [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) 0.23.0+
   - Pathfinder: Wrath of the Righteous 1.4+
   ```

   Nexus-Upload wird automatisch von der GitHub Action übernommen (`.github/workflows/nexus-upload.yml`).

5. **Dem User die Release Notes zeigen** und fragen: „Release Notes so in Ordnung, oder soll ich etwas anpassen?"

   Warte auf Freigabe oder Änderungswünsche. Überarbeite bis der User zustimmt.

---

## Schritt 4: Versions-Bump

Aktualisiere die Version in exakt diesen drei Dateien:

1. **`WrathTactics/WrathTactics.csproj`** — `<Version>X.Y.Z</Version>` ersetzen
2. **`WrathTactics/Info.json`** — `"Version": "X.Y.Z"` ersetzen
3. **`Repository.json`** — `"Version": "X.Y.Z"` und `"DownloadUrl"` auf:
   ```
   https://github.com/Gh05d/wrath-tactics/releases/download/vX.Y.Z/WrathTactics-X.Y.Z.zip
   ```

Commit erstellen:
```
git add WrathTactics/WrathTactics.csproj WrathTactics/Info.json Repository.json
git commit -m "chore: bump version to X.Y.Z"
```

---

## Schritt 5: Build

```
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/ --nologo
```

Danach prüfen ob das ZIP existiert:
```
ls WrathTactics/bin/WrathTactics-X.Y.Z.zip
```

**Build schlägt fehl oder ZIP nicht vorhanden:**
```
git reset --soft HEAD~1
git restore --staged WrathTactics/WrathTactics.csproj WrathTactics/Info.json Repository.json
```
Abbruch mit „Fehler: Build fehlgeschlagen. Versions-Bump wurde rückgängig gemacht."

---

## Schritt 6: Bestätigungs-Gate (Point of No Return)

Zeige dem User eine Zusammenfassung:

```
=== Release bereit ===
Version:  vX.Y.Z
ZIP:      WrathTactics/bin/WrathTactics-X.Y.Z.zip

Was jetzt passiert:
  1. git push origin master
  2. git tag -a vX.Y.Z
  3. git push origin vX.Y.Z
  4. GitHub Release erstellen mit ZIP-Upload
  5. GitHub Action lädt automatisch zu Nexus Mods hoch

GitHub Release Notes (Vorschau):
<GitHub Markdown Notes>

Fortfahren? (ja/nein)
```

**User sagt nein oder bricht ab:**
```
git reset --soft HEAD~1
git restore --staged WrathTactics/WrathTactics.csproj WrathTactics/Info.json Repository.json
```
Meldung: „Release abgebrochen. Versions-Bump rückgängig gemacht."

---

## Schritt 7: Push, Tag, GitHub Release

Reihenfolge ist wichtig — Code erst pushen, dann taggen:

1. **Push master:**
   ```
   git push origin master
   ```
   Schlägt fehl:
   ```
   git reset --soft HEAD~1
   git restore --staged WrathTactics/WrathTactics.csproj WrathTactics/Info.json Repository.json
   ```
   Abbruch mit „Fehler: Push fehlgeschlagen. Versions-Bump rückgängig gemacht."

2. **Tag erstellen:**
   ```
   git tag -a vX.Y.Z -m "Release vX.Y.Z"
   ```

3. **Tag pushen:**
   ```
   git push origin vX.Y.Z
   ```
   Schlägt fehl: Lokalen Tag löschen:
   ```
   git tag -d vX.Y.Z
   ```
   Meldung: „Tag-Push fehlgeschlagen. Manuell ausführen: `git push origin vX.Y.Z`"

4. **GitHub Release erstellen:**
   ```
   gh release create vX.Y.Z "WrathTactics/bin/WrathTactics-X.Y.Z.zip" \
     --repo Gh05d/wrath-tactics \
     --title "Wrath Tactics vX.Y.Z" \
     --notes "<GitHub Markdown Notes>"
   ```
   Schlägt fehl: Manuellen Befehl anzeigen und weitermachen.

---

## Schritt 8: Abschluss

Prüfe ob die GitHub Action für den Nexus-Upload erfolgreich war:
```
gh run list --repo Gh05d/wrath-tactics --limit 1
```

Zeige dem User die Zusammenfassung:

```
=== Release vX.Y.Z abgeschlossen! ===

GitHub: https://github.com/Gh05d/wrath-tactics/releases/tag/vX.Y.Z
Nexus:  Automatisch hochgeladen via GitHub Action (Status: <success/failure>)
```

Falls die GitHub Action fehlgeschlagen ist, zeige den manuellen Nexus-Upload-Link:
```
Nexus Upload (manuell): https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005?tab=files
ZIP: WrathTactics/bin/WrathTactics-X.Y.Z.zip
```

---

## Schritt 9: Discord Post

Generiere die fertige Discord-Nachricht zum Copy-Pasten in Owlcat's `#mod-updates`-Kanal. Basis sind die „What's New"-Bullets aus Schritt 3, **aber stark gekürzt** — Discord bevorzugt kurze Scan-baren Text, nicht die ausführlichen Erklärungen der GitHub-Release-Notes.

Kürzungsregel: Jeder Bullet max. eine Zeile, nur das WAS (nicht das WARUM/WIE). Entferne Erklärungen in Klammern, technische Details, Root-Cause-Begründungen.

Beispiel:
- GitHub: „CastSpell rules can now fall back across Scroll / Potion / quickslot Wand sources via a new Sources dropdown; metamagic + variant rules stay Spellbook-only by design."
- Discord: „CastSpell: new Sources dropdown (Spell / Scroll / Potion / Wand fallback)"

Falls die Bullet-Liste leer wäre (z.B. nur `chore:`-Commits seit letztem Tag), verwende stattdessen den Platzhalter-Bullet `- Maintenance release`.

Ausgabeformat im Terminal (exakt so, inkl. Delimiter-Zeilen):

```
=== Discord Post (alles zwischen den Zeilen kopieren) ===
**Wrath Tactics vX.Y.Z**

- <bullet 1>
- <bullet 2>
- ...

GitHub: https://github.com/Gh05d/wrath-tactics/releases/tag/vX.Y.Z
Nexus: https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005
=== End Discord Post ===
```

Regeln:

- Mod-Name + Version in einer Zeile fett (`**...**`), danach eine Leerzeile.
- Bullets mit `- ` prefix, genau wie in den GitHub-Release-Notes aus Schritt 3.
- Beide Links als reine URLs (kein Markdown-Link-Syntax) — Discord erzeugt automatisch Previews.
- Keine „Installation" oder „Requirements" Sektionen — Discord-Post bleibt kurz, Details sind auf Nexus.
- Schritt 9 ist rein informativ: schlägt er fehl, ist das Release bereits durch (Tag gepushed, GitHub Release live, Nexus Action läuft). Nicht abbrechen, nicht rückgängig machen.

---

## Fehlerbehandlung — Übersicht

| Fehler | Verhalten |
|--------|-----------|
| Dirty working tree | Abbruch vor jeder Änderung |
| Nicht auf master | Abbruch vor jeder Änderung |
| Keine gültige Semver | Abbruch vor jeder Änderung |
| Tag existiert bereits | Abbruch vor jeder Änderung |
| Build schlägt fehl | Bump-Commit rückgängig machen, Abbruch |
| User bricht am Gate ab | Bump-Commit rückgängig machen |
| Push master schlägt fehl | Bump-Commit rückgängig machen, Abbruch |
| Push tag schlägt fehl | Lokalen Tag löschen, manuellen Befehl zeigen |
| GitHub Release schlägt fehl | Manuellen `gh release create`-Befehl zeigen |
