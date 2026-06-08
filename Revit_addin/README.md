# Charis Revit Connector

A Revit 2027 add-in that streams data from **Cloud Firestore** into Revit using
the Firebase Admin credential and `Google.Cloud.Firestore` realtime listeners.

## Status: M5 — single layered document, all four families

**Connect** runs a live two-way sync against **one document**,
`rhinoReviewV2/test-model`, holding `floors[] / walls[] / beams[] / columns[]`
arrays plus origin metadata. Each snapshot is the **full authoritative state**.
Types are shared and named by size with the universal `Test` prefix.

- **Floor**: closed `polyline` outline, `thickness`, `material`.
- **Wall**: centerline `polyline` (one Revit wall per segment), `thickness`,
  `height`, `material`.
- **Beam** / **Column**: `line` [start,end] + `xandy{b,h}` on a structural
  framing/column family (auto-detected from what's loaded).

Firestore → Revit creates/updates and **deletes anything absent** from the
document; Revit → Firestore pushes geometry + size (and deletes) via
read-modify-write on the relevant array. A thread-local `SyncGuard` plus the
`writerInstanceId` origin marker (ours = `revit-charis`) suppress echoes.

| Milestone | Scope | State |
|-----------|-------|-------|
| M0–M3 | Skeleton → auth → realtime stream → bi-directional sync (floors) | ✅ done |
| M4 | Family framework + Floor/Wall/Beam/Column (four collections) | ✅ done |
| **M5** (this) | Single layered doc `rhinoReviewV2/test-model`, full-state reconcile + writer protocol | ✅ done |
| Harden | Debounce, reconnect, write-conflict handling, beam/column family selection, security | |

### Dev loop helper

Revit locks the add-in DLLs while running. `deploy.ps1` closes Revit, builds +
deploys, and (optionally) relaunches:

```powershell
./deploy.ps1            # close Revit, build + deploy
./deploy.ps1 -Launch    # ...then relaunch Revit 2027
```

### Firestore layout

A single document `rhinoReviewV2/test-model`:

```
{
  floors:  [ { id, type:"floor",  polyline:Vec3[] (closed), thickness, material } ],
  walls:   [ { id, type:"wall",   polyline:Vec3[] (centerline), thickness, height, material } ],
  beams:   [ { id, type:"beam",   line:[Vec3,Vec3], xandy:{b,h} } ],
  columns: [ { id, type:"column", line:[Vec3,Vec3], xandy:{b,h} } ],
  writerInstanceId, writerOperationId, updatedAtUtc, schemaVersion
}
```

All numeric values are in **feet** (Revit's internal length unit); numbers may
be int64 or double. Each element's **`id` field** maps to one Revit element
(stored in the element's Comments). Our writes stamp `writerInstanceId =
revit-charis` so the listener ignores them (no echo) and the Rhino side can tell
our edits apart.

### How it maps to Revit

- A floor's thickness/material come from its **type's compound structure**, not
  an instance parameter. Types are **shared and named by thickness (+ material)**
  with a universal `Test` prefix — e.g. `Test - Floor - 8` or
  `Test - Floor - 8 - concrete` — created on demand. Changing thickness/material
  **switches** the floor to the matching type (creating it if absent) rather than
  mutating a shared type, so each type name always reflects its real geometry.
- The Firestore document key is written to the floor's **Comments** parameter so
  updates/deletes find the right floor, even after reopening the model.
- The `Listen` callback runs on a background thread: it only enqueues changes
  and raises an `ExternalEvent`; all model edits happen on Revit's thread in a
  single transaction.

### Loop prevention (M3)

`SyncGuard` is a thread-local flag raised while the forward sync applies edits
inside its transaction. `DocumentChanged` fires during that commit (same thread),
sees the guard, and skips writing those edits back to Firestore. Tolerance checks
(`Tol`) make the single convergence round-trip a no-op (no move/thickness change
within ~0.001").

### Limitations (revisit in hardening)

- The Revit→Firestore registry is seeded from the **active document on Connect**;
  switching documents mid-session isn't re-seeded yet.
- Creating a brand-new floor *in Revit* (no document key) is not pushed up — only
  managed floors (those with a `Test - Floor - …` type **and** a document key in
  Comments) sync back.
- Floor outline changes **recreate** the slab (delete + re-sketch), so the floor
  gets a new ElementId on each shape change (id mapping is preserved via Comments).
- No debounce yet; rapid bursts raise the event repeatedly.
- Floors are hosted on the **lowest level**; `z` sets their elevation (top of slab).

## Requirements

- **Revit 2027** (runs on .NET 10)
- **.NET 10 SDK** to build
- Visual Studio 2022 17.x (or `dotnet build`)

## Build & deploy

```powershell
dotnet build CharisRevitConnector.sln -c Debug
```

The project's `DeployAddin` MSBuild target runs automatically after build and
copies `CharisRevitConnector.dll` + `CharisRevitConnector.addin` into the
**per-user** add-in folder:

```
%AppData%\Autodesk\Revit\Addins\2027\
```

> **Revit 2027 note:** the all-users add-in folder moved to
> `%ProgramFiles%\Autodesk\Revit\Addins\2027` (admin-only). `%ProgramData%`
> is no longer scanned for 2027, so we deploy to the per-user `%AppData%`
> folder, which needs no admin rights.

## Firebase credential setup (required for M1)

The add-in **never** embeds the service-account key. At connect time it looks
for the JSON in this order (first existing file wins):

1. `CHARIS_FIREBASE_CREDENTIALS` environment variable (full path to the `.json`)
2. `GOOGLE_APPLICATION_CREDENTIALS` environment variable
3. `%AppData%\Charis\serviceAccount.json`

Get the key from the Firebase console → **Project settings → Service accounts →
Generate new private key**. The `project_id` is read from the JSON itself.

```powershell
# simplest: drop the key at the default per-user location
New-Item -ItemType Directory -Force "$env:APPDATA\Charis" | Out-Null
Copy-Item .\your-service-account.json "$env:APPDATA\Charis\serviceAccount.json"
```

Activity is logged to `%AppData%\Charis\charis.log`.

## Verify (M4a done-when)

Forward (Firestore → Revit):
1. Launch Revit 2027 with a model **open** (≥1 Level and ≥1 Floor type) →
   **Charis → Connect**.
2. Add a `floors/<key>` doc with a closed polyline + thickness + material, e.g.
   ```json
   { "type":"floor",
     "polyline":[{"x":0,"y":0,"z":0},{"x":40,"y":0,"z":0},
                 {"x":40,"y":30,"z":0},{"x":0,"y":30,"z":0},{"x":0,"y":0,"z":0}],
     "thickness":1, "material":"concrete" }
   ```
   → that exact 40×30 slab appears.
3. Edit `thickness`/`material` → updates in place. Edit `polyline` → re-shapes.
4. Delete the doc → floor removed.

Reverse (Revit → Firestore):
5. **Move** the floor in Revit → its `floors/<key>` doc's `polyline` updates.
6. **Delete** it in Revit → its doc is deleted.
7. Confirm **no oscillation** (settles after one round-trip).

Activity is logged to `%AppData%\Charis\charis.log`.

To debug, attach Visual Studio to `Revit.exe` and break in
`FloorHandler.CreateOrUpdate` (forward) or `RevitChangeWatcher.OnDocumentChanged`
(reverse).

## Notes

- The Revit API is referenced via the `Revit_All_Main_Versions_API_x64`
  (2027.0.2) NuGet package as **reference-only** (`ExcludeAssets=runtime`),
  so the Revit DLLs are not copied to output.
- On .NET 10, Firestore uses the **managed `Grpc.Net.Client`** — no native
  `Grpc.Core` and far lower assembly-conflict risk inside Revit's process.
- The add-in DLL and all dependencies deploy to an isolated
  `Addins\2027\CharisRevitConnector\` subfolder; `EnableDynamicLoading` +
  the generated `.deps.json` drive per-add-in assembly resolution.
- **Never commit the Firebase service-account JSON.** `.gitignore` blocks the
  common filename patterns.
