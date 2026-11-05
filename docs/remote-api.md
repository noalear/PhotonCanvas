# Remote server API

`PhotonCanvas.Server` lets a phone watch and run the machine through the computer. This is its HTTP
API, for the Android app (and the phone page the server itself serves at `/`, which uses the same
calls). The user's guide is `docs/help/remote.md`.

## How it fits

```
phone ──HTTP──▶ PhotonCanvas.Server (on the PC) ──WebSocket──▶ F2 Ultra
                        │ reads (never writes) app.json, keys.json, designs
                        └ writes data\remote\ (remote.json, jobs.jsonl, sent.json, jobs\<id>\*.jpg, state.json)
```

- The server keeps **its own** connection to the machine, next to the desktop app's (verified: the
  machine takes several, each gets every push; see `docs/protocol-f2-ultra.md`).
- It builds jobs exactly as the desktop's **Ship It** (`JobSettings.From(app.json)` + `JobBuilder` +
  `JobChecks`), and uploads them with `autoStart: 0`.
- It never writes `app.json`: the desktop app keeps that file in memory and would overwrite it.
- Port **8740** (`remote.json` → `Port`), plain HTTP, all interfaces: the phone page and the app
  download. Port **8741** (`HttpsPort`): the same API over HTTPS with the server's own certificate
  (ECDSA P-256, made on the first start, kept in `remote\tls.pfx`; port and fingerprint in
  `remote\tls.json`). `--local` binds both to localhost only (testing).
- The QR code: `http://<address>:8740/#code=<code>&tls=8741&fp=<fingerprint>&alt=<other addresses>`.
  The fingerprint is SHA-256 of the certificate, base64url; the app accepts that certificate and no
  other (no certificate authority is involved). `alt` lists the computer's other addresses (home
  network, Tailscale): the app tries all of them and uses whichever answers.
- `GET /tls` (no access code) → `{port, fingerprint}`: for a phone given only the address, which then
  talks HTTPS to that certificate from then on (trust on first use).

## Access

Every `/api/...` request needs the access code (`remote.json` → `AccessCode`, 16 characters as
`XXXX-XXXX-XXXX-XXXX`; dashes, spaces and case don't matter):

- `Authorization: Bearer <code>`, or
- `?access_token=<code>` where a header can't be set (`<img>`, `EventSource`).

`401` = wrong or missing code; `429` = ten wrong codes from that address within five minutes (locked
out for the rest of the five minutes). The desktop app's QR code holds `http://<pc>:<port>/#code=<code>`.
The code follows `#`, so it never reaches the server (or its log); the page keeps it and clears the address.

Plain HTTP: on the home network, or over Tailscale (encrypted). Never expose the port to the internet.

## Errors

Any error is `{"error": "<plain words for the user>"}` with a status: `403` starting from the phone
is turned off on the computer, `404` unknown thing, `409`
can't be done now (machine busy, the start safeguard, a refused job), `422` the design can't be
built, `502` the machine refused or didn't do it, `503` the server isn't connected to the machine.
Show the text as it is.

## Status

`GET /api/info` → `{product, version, machine, cameraNote, api, androidApp, addresses}`. Use it to
check the address and code. `api` is the API's version (1); `androidApp` is where the server offers
the Android app for download, or null; `addresses` are the computer's addresses (the app keeps them).

`GET /app/PhotonCanvasRemote.apk` (no access code): the Android app, when the server has it (beside
the server in `app\`, or the newest build in the source tree).

The records for all of this are in `src/PhotonCanvas.Remote/Contracts.cs`, shared by the server and
the Android app.

`GET /api/status` →

```json
{
  "connected": true, "ip": "192.168.1.60", "name": "xTool_F2 Ultra_v2_000000",
  "firmware": "40.54.021.00.ht6", "mode": "P_WORKING", "state": "Running a job",
  "focusDistanceMm": 21.3, "problem": null, "cameraBlanked": true, "at": "2026-10-23T02:02:31-07:00",
  "job": {
    "taskId": "…", "design": "freddie", "source": "phone|computer|elsewhere", "stage": "loaded|running|paused",
    "loadedAt": "…", "startedAt": "…", "elapsedS": 878.4, "fraction": null, "remainingS": 3330, "estimateS": 4560,
    "startedFromPhone": false
  }
}
```

- `mode` is the machine's (`P_IDLE`, `P_WORK` loaded, `P_WORKING`, `P_PAUSE`, `P_WORK_DONE`, …) and
  `state` the same in words. `problem` says why it isn't connected.
- `job` is null unless a job is loaded, running or paused. `estimateS` and `remainingS` are known for
  jobs the phone loaded (the machine reports elapsed time only; firmware 40.54 gives no fraction).
- `cameraBlanked`: the machine blanks its cameras for the whole job (pictures come out black).

`GET /api/events`: server-sent events (`text/event-stream`). First the status, then:

| event | data |
|---|---|
| `status` | the status above, on every change and every 2 s while a job runs |
| `job` | `{kind: "started", id, design, source, startedFromPhone}`, `{kind: "finished" \| "stopped", id, design, tookS, text}`, `{kind: "picture", id, design}` (the after picture is saved) |
| `alert` | `{kind: "flame" \| "tilt" \| "machine", text, at}`: the machine's alarms (`FIRE_RECOGNITION`, `GYRO_SENSOR`, error-level events). Show these loudly. |

A comment line (`: ping`) every 15 s keeps the connection open.

## Camera

`GET /api/camera/overview` or `/api/camera/closeup`, optional `?width=` (160–4000, default 1280):
takes a picture now → `image/jpeg`, with headers `X-Picture-Id`, `X-Taken-At` (ISO 8601) and
`X-Camera-Blanked`. The full picture (4000 × 3000) is kept by id for a while (the last 16).

`GET /api/pictures/{id}?width=`: one of those again.

## Designs

`GET /api/projects` → `[{id, name, folder, modified, recent}]`: the desktop app's recent files first,
then the project folders' designs (`remote.json` → `ProjectFolders`, else Documents; two levels deep),
`.pcanvas` and `.ytool`. Ids are stable (a hash of the path).

`POST /api/projects/{id}/check` → builds the job (can take seconds; a build is reused until the file
or app.json changes):

```json
{
  "id": "…", "design": "freddie", "estimateS": 4560, "estimate": "1 h 16 min", "widthMm": 144.9, "heightMm": 70.7,
  "refusal": null, "notInJob": [], "lines": ["• Relief …: Image, 30.2% at 80 mm/s × 3 (diode)", "…"], "warnings": [],
  "loadedJob": null, "phoneNote": null,
  "text": "the whole message as Ship It shows it",
  "preview": "/api/projects/{id}/preview.png"
}
```

`refusal` set = it can't be loaded (say why). `loadedJob` = a job is loaded and not started; loading
replaces it. `phoneNote` = something the phone does differently (serial numbers don't move on).

`GET /api/projects/{id}/preview.png?width=` (100–2000, default 800): what will be marked, on white.

`POST /api/projects/{id}/load` → uploads the job (the machine then waits) and takes a picture of the bed:
`{taskId, design, estimateS, picture: {id, takenAt, blanked, url} | null, text}`.

## The job

`POST /api/job/start` with `{"pictureId": "<id>"}` → `{mode, state}`. Starts the loaded job: **the
laser fires**. Starting from the phone is **off by default**: until the user turns on *Allow starting
jobs from the phone* in the desktop app's Remote access window (after a full warning; saved in
`remote\phone-start.json`, read at every start), the server answers `403` with what to do and nothing
starts. When it's on, only on a picture the server took **less than 2 minutes ago, while this very job
was loaded** (`StartGuard`); otherwise `409` with what to do. If the machine doesn't start, `502`.

`POST /api/job/pause`, `/api/job/resume`, `/api/job/stop` → `{mode, state}`. Stop also cancels a
loaded job that hasn't started.

## History

`GET /api/history?max=` (1–300, default 50), newest first → `[{id, design, source, sourceText,
loadedAt, startedAt, endedAt, finished, startedFromPhone, estimateS, tookS, before, after, projectId}]`.
`projectId` is set for jobs loaded from the phone whose design file is still there ("Run again").
`before` and `after` are picture URLs or null: a picture when the job was loaded, and one taken 3 s
after it ended (the camera works again then). Every job the machine runs while the server is on is
here, whoever started it.

`GET /api/history/{id}/before.jpg` or `after.jpg`, optional `?width=`.

## Still to verify on the machine

- That `PUT /v1/processing/state?action=start` starts a job waiting for the button (`P_WORK`). If it
  doesn't, the server will re-upload the job with `autoStart: 1`.
