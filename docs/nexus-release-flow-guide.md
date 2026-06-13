# Nexus Mods Release Flow — Setup Guide

A drop-in, GitHub-Actions-based release pipeline for a .NET / Unity Mod Manager (UMM)
mod for **Pathfinder: Wrath of the Righteous**, with **automatic upload to Nexus Mods**.

This guide is self-contained: an LLM can implement it directly. Anything in
`<ANGLE_BRACKETS>` is a per-project placeholder — replace every occurrence.

---

## What this flow does

Publishing a GitHub Release triggers a workflow that grabs the release's `.zip`
asset and uploads it to your Nexus mod page. You never touch the Nexus upload form
for routine releases. There are two pieces:

1. **GitHub Action** (`.github/workflows/nexus-upload.yml`) — the automation.
   Fires on `release: published`, downloads the `.zip` asset, pushes it to Nexus
   via the official `Nexus-Mods/upload-action`. This part is **game-agnostic**:
   routing is entirely via the API key + `file_group_id`, so it works for any
   Nexus mod regardless of game.

2. **Release orchestration** — the steps that *produce* the GitHub Release
   (version bump → build zip → `gh release create`). This part is mod-specific
   (.NET build, csproj/Info.json). A ready-to-adapt checklist is in
   [§5](#5-release-orchestration-checklist).

```
bump version (3 files) → Release build (produces <Mod>-X.Y.Z.zip)
   → git push + tag → gh release create (attach zip)
      → GitHub Release published
         → nexus-upload.yml fires → zip uploaded to Nexus   ✅ (no manual step)
```

---

## Placeholder reference

Fill these in once, then substitute throughout:

| Placeholder | Meaning | Example (Wrath Tactics) |
|---|---|---|
| `<MOD_ID>` | UMM mod id / assembly base name | `WrathTactics` |
| `<MOD_DISPLAY_NAME>` | Human-facing name | `Wrath Tactics` |
| `<GH_OWNER>/<GH_REPO>` | GitHub repo slug | `Gh05d/wrath-tactics` |
| `<CSPROJ_PATH>` | Path to the project file | `WrathTactics/WrathTactics.csproj` |
| `<NEXUS_GAME_DOMAIN>` | Nexus game domain | `pathfinderwrathoftherighteous` |
| `<NEXUS_MOD_PAGE_ID>` | Number in the Nexus mod URL | `1005` |
| `<NEXUS_FILE_GROUP_ID>` | 7-digit file-group id (see §2) | `4191` |

---

## 1. One-time prerequisites

### 1a. A Nexus mod page must already exist

The upload API adds **files to an existing mod page** — it cannot create the page.
Create the mod page manually on Nexus once (with header image, description, category).
After that, all file uploads are automated.

> The GitHub Action uploads **only the file zip**. Mod-page images (header,
> thumbnail), the long description, and the "version display" field are separate
> fields on the Nexus *Edit* page and stay manual.

### 1b. Get a Nexus Personal API Key

Nexus account → **Site Preferences → API Keys** → "Personal API Key". Copy it.
You need it (a) to discover the `file_group_id` below, and (b) as the GitHub secret.

### 1c. Get a GitHub token for `gh`

The orchestration uses the `gh` CLI (`gh auth login` once). The workflow itself uses
the auto-provided `${{ github.token }}` — no setup needed for that part.

---

## 2. Find your `file_group_id` (the tricky bit)

**The `file_id=X` in a Nexus URL is NOT the `file_group_id`.** The upload action
needs the 7-digit *file-group* id, obtained via a two-step v3-API dance (needs the
Personal API Key from 1b):

```bash
KEY="<your-personal-api-key>"

# Step 1: resolve the long numeric internal mod id from the page id
curl -sH "apikey: $KEY" \
  "https://api.nexusmods.com/v3/games/<NEXUS_GAME_DOMAIN>/mods/<NEXUS_MOD_PAGE_ID>"
#   → take data.id  (a long numeric value)

# Step 2: list the file-update groups for that internal id
curl -sH "apikey: $KEY" \
  "https://api.nexusmods.com/v3/mods/<data.id-from-step-1>/file-update-groups"
#   → data.groups[].id  is the 7-digit <NEXUS_FILE_GROUP_ID> the action needs
```

If a brand-new page has no groups yet, do one manual file upload first to create
a group, then re-run step 2.

---

## 3. Configure GitHub repo settings

In **Settings → Secrets and variables → Actions**:

- **Secret** `NEXUSMODS_API_KEY` = the Personal API Key from 1b.
- **Variable** `NEXUSMODS_FILE_GROUP_ID` = the 7-digit id from §2.

(Secret vs. variable matters: the action reads `secrets.NEXUSMODS_API_KEY` and
`vars.NEXUSMODS_FILE_GROUP_ID`.)

---

## 4. The workflow file

Create `.github/workflows/nexus-upload.yml` verbatim — it is already generic
(no per-mod values are hardcoded; everything comes from secrets/variables and the
release event):

```yaml
name: Upload to Nexus Mods

on:
  release:
    types: [published]

jobs:
  upload-to-nexus:
    runs-on: ubuntu-latest
    steps:
      - name: Download release asset
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          gh release download ${{ github.event.release.tag_name }} --pattern "*.zip" --repo ${{ github.repository }} --clobber
          ls *.zip || { echo "No zip asset found in release"; exit 1; }

      - name: Get zip filename and version
        run: |
          echo "ZIP_FILE=$(ls *.zip)" >> "$GITHUB_ENV"
          echo "VERSION=${GITHUB_REF_NAME#v}" >> "$GITHUB_ENV"

      - name: Upload to Nexus Mods
        uses: Nexus-Mods/upload-action@main
        with:
          api_key: ${{ secrets.NEXUSMODS_API_KEY }}
          file_group_id: ${{ vars.NEXUSMODS_FILE_GROUP_ID }}
          filename: ${{ env.ZIP_FILE }}
          version: ${{ env.VERSION }}
          description: ${{ github.event.release.body }}
          file_category: main
```

Notes:
- The workflow depends on the GitHub Release carrying a `*.zip` asset named with the
  version (e.g. `<MOD_ID>-X.Y.Z.zip`). The build target in §4a produces exactly that.
- `version` is derived by stripping the leading `v` from the tag (`v1.2.3` → `1.2.3`),
  so **tag releases as `vX.Y.Z`**.
- `Nexus-Mods/upload-action@main` pins to a moving branch. If you want
  reproducibility, pin to a commit SHA or a released tag instead of `@main`.

### 4a. Make the Release build produce the zip

The workflow uploads whatever `.zip` is attached to the release. Have the project
emit `<MOD_ID>-<Version>.zip` on a Release build. In `<CSPROJ_PATH>`, an MSBuild
target that zips the build output (this is the Wrath Tactics setup):

```xml
<Target Name="CreateZip" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
  <ZipDirectory SourceDirectory="$(MSBuildProjectDirectory)\$(OutputPath)"
                DestinationFile="$(MSBuildProjectDirectory)\$(OutputPath)\..\$(AssemblyName)-$(Version).zip"
                Overwrite="true" />
</Target>
```

With `<Version>X.Y.Z</Version>` and `<AssemblyName><MOD_ID></AssemblyName>` in the
csproj, a Release build drops `bin/<MOD_ID>-X.Y.Z.zip`.

---

## 5. Release orchestration checklist

This is the human/LLM-driven sequence that creates the GitHub Release (which then
triggers §4). Adapt freely — it's a checklist, not code.

### 5a. Pre-flight (abort before changing anything if any fails)
- Working tree clean (`git diff --quiet && git diff --cached --quiet`).
- On the default branch (`master`/`main`).
- Read current version from the csproj (`grep -oP '<Version>\K[^<]+' <CSPROJ_PATH>`);
  must be valid `X.Y.Z`.
- Compute the next version (patch = Z+1, minor = Y+1 Z=0, major = X+1 Y=0 Z=0).
- Tag `vX.Y.Z` does not already exist (`git rev-parse "vX.Y.Z"`).

> **Pre-condition trap:** the csproj must hold the **pre-bump** version when you
> start. If a manual `chore: bump version` commit already ran, the script bumps
> *again*. Drop it first (`git reset --hard HEAD~1`).

### 5b. Bump the version in **three** files (UMM mods need all three)
1. `<CSPROJ_PATH>` — `<Version>X.Y.Z</Version>`
2. `<MOD_ID>/Info.json` — `"Version": "X.Y.Z"` (UMM reads this; bumping only the
   csproj ships a zip whose filename version disagrees with what UMM shows)
3. `Repository.json` — `"Version": "X.Y.Z"` **and** the `DownloadUrl`:
   `https://github.com/<GH_OWNER>/<GH_REPO>/releases/download/vX.Y.Z/<MOD_ID>-X.Y.Z.zip`
   (this is UMM's in-manager auto-update feed; `Info.json`'s `Repository` field
   points at the raw `Repository.json` on the default branch — see §6)

Commit: `git commit -am "chore: bump version to X.Y.Z"`.

### 5c. Build the release zip
```bash
~/.dotnet/dotnet build <CSPROJ_PATH> -c Release -p:SolutionDir=$(pwd)/ --nologo
ls <MOD_ID>/bin/<MOD_ID>-X.Y.Z.zip   # verify it exists
```
On failure: `git reset --soft HEAD~1` (undo the bump commit) and abort.

### 5d. Confirmation gate (point of no return)
Show the user: version, zip path, the exact steps about to run, and the release
notes preview. Proceed only on explicit "yes". On "no": undo the bump commit.

### 5e. Push, tag, create the GitHub Release (order matters: push code, then tag)
```bash
git push origin master
git tag -a vX.Y.Z -m "Release vX.Y.Z"
git push origin vX.Y.Z
gh release create vX.Y.Z "<MOD_ID>/bin/<MOD_ID>-X.Y.Z.zip" \
  --repo <GH_OWNER>/<GH_REPO> \
  --title "<MOD_DISPLAY_NAME> vX.Y.Z" \
  --notes "<github-markdown-release-notes>"
```
`gh release create` publishes the release → that fires `nexus-upload.yml`.

### 5f. Verify the upload
```bash
gh run list --repo <GH_OWNER>/<GH_REPO> --limit 1
```
If the action failed, fall back to a manual upload at
`https://www.nexusmods.com/<NEXUS_GAME_DOMAIN>/mods/<NEXUS_MOD_PAGE_ID>?tab=files`.
Don't roll back — the tag is pushed and the GitHub Release is live; only the Nexus
hand-off failed.

---

## 6. `Repository.json` (UMM in-manager auto-update — optional but recommended)

Separate from Nexus: this lets UMM show "Update available" inside the game's mod
manager. Keep it at the repo root and point `Info.json`'s `Repository` field at its
raw URL on the default branch:

```json
{
  "Releases": [
    {
      "Id": "<MOD_ID>",
      "Version": "X.Y.Z",
      "DownloadUrl": "https://github.com/<GH_OWNER>/<GH_REPO>/releases/download/vX.Y.Z/<MOD_ID>-X.Y.Z.zip"
    }
  ]
}
```

`Info.json`:
```json
"Repository": "https://raw.githubusercontent.com/<GH_OWNER>/<GH_REPO>/master/Repository.json"
```

It's bumped in step 5b so the feed stays in sync with the Nexus/GitHub release.

---

## 7. Gotchas (learned the hard way)

- **`file_id` in the Nexus URL ≠ `file_group_id`.** Do the §2 v3-API dance.
- **The action uploads only the file zip.** Mod-page header/thumbnail images,
  description, and version-display text are separate manual fields on Nexus.
- **Bump all three version files** (csproj, Info.json, Repository.json). Bumping
  only one ships a zip whose name/UMM-version/auto-update feed disagree.
- **csproj must hold the pre-bump version** before the orchestration runs, or it
  double-bumps.
- **Tag with the `v` prefix** (`vX.Y.Z`); the workflow strips it for the Nexus
  `version` field.
- **`@main` is a moving target.** Pin `Nexus-Mods/upload-action` to a SHA/tag for
  reproducible releases.
- **The release must carry a `*.zip` asset**, or the workflow fails at the
  download step (`No zip asset found in release`).
