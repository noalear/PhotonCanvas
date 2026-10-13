# F2 Ultra (`GS004-CLASS-4`) device protocol

Source: `reference/exts/GS004-CLASS-4/index.js` (ext 1.5.31, versionId 54).

Supporting sources:
- Transport: `app/.vite/renderer/packages/shared-cdn/core/atomm-sharedworker.js`
- Discovery: `app/.vite/build/discover-worker.*.cjs` (unminified)
- File channel: `app/.vite/renderer/editor/static/js/src.DzJfpp3_.js`

**Status:** the section "Verified on hardware" below was tested on the user's F2 Ultra (firmware 40.54.021). Everything else was read from code. Items marked (?) are inferred.

## Verified on hardware (2026-10-12, F2 Ultra, fw 40.54.021.00.ht6)

Implemented in `src/ytool/`.

- **Discovery:** V2 encrypted multicast works. The device answers with `deviceCode: GS004-CLASS-4`, `platformVersion: 1.5`, `netType: wlan`.
- **Framing:** framing and CRC16 work as documented below. Device JSON responses are pretty-printed with tabs.
- **Pairing:** `/v1/user/parity` with `userID: "mk-guest"` returns `code 0, "user auth success"` and **no** sessionToken. No button press is needed, and every read endpoint works afterwards.
  - **A random UUID as userID makes the device drop the connection (1006).** The stock app's logged-in userUuid must have some other format.
- **All channels share one id:** the instruction, file and media sockets must all use the **same** `?id=`, because the device pairs them by it. With different ids, file requests are silently ignored.
- **`machineInfo`:**
  - `laserType: ["RED","BLUE"]`, `laserPower: [60,40]`: RED = 60 W fiber, BLUE = 40 W diode.
  - `workSize 231.98 × 231.98` mm.
  - `gcodeFormat: ["txt","bin"]`, so **plain-text G-code is accepted**.
  - `packageFormat: ["xf"]`.
- **Snapshot:** `GET /v1/camera/snap` needs `params: {name: "main"|"deep", stream: "0"|"1"}`. The params must go in `params`, not `data`; otherwise the device returns `-1 "name is not string"`. It returns `{filename: "/tmp/main_<ts>.jpg"}`, which is then downloaded over the file channel.
  - Images are 4000×3000 JPEGs: about 630 KB for the overview, about 1 MB for the close-up.
  - The file request packet is sent with no body CRC (type 33 | 0x80), the same as the stock app.
- **Snapshot polling rate:** **3.9 fps** for the overview and 2.8 fps for the close-up, in a sequential loop on WiFi.
- **Live stream: not available on this firmware.**
  - `POST /v1/platform/camera/live {name, stream, action:"start"}` returns success.
  - The media_stream socket answers pings (`0x02` → `0x82`) but sends no frames.
  - `/v1/platform/camera/list` fails with "getCameraList failed".
  - `/v1/camera/live` returns 404.

  The live view is therefore built on snapshot polling (`ytool view`).
- **Not found (404):** `/v1/camera`, `/v1/camera/resolution`, `/v1/platform/camera/resolution`.

## Two protocol generations

| | V1 (legacy firmware) | V2 (current firmware) |
|---|---|---|
| Commands | HTTP `http://<ip>:8080` | WebSocket `wss://<ip>:28900/websocket?id=<uuid>&function=instruction` |
| Events | WebSocket `ws://<ip>:8081`, JSON pushes, `"ping"`→`"pong"` | same instruction socket |
| Files | HTTP POST | `...&function=file_stream` socket |
| Media | camera HTTP on `:8329` | `...&function=media_stream` socket |
| Auth | none | pairing → `sessionToken` |

**USB is a network link, not a serial port.** The host gets these addresses:
- V1: `201.234.3.1` / `201.234.4.1`
- V2: `172.25.3.1` / `172.25.4.1`
- Ethernet: `169.254.2.1`

## Discovery

### V1

1. Send UDP JSON `{"ip":<hostIP>,"port":<localPort>,"requestId":<ms>}` to multicast `224.0.1.77:20000`, 3 times, 3 s apart.
2. The device replies with `{ip, name, version, sncode, requestId}`.
3. Fallback: TCP-probe `:8080` across the subnet, then call the `/system` probes (below).

### V2

1. Build the payload:
   ```json
   {"type":"deviceFind","method":"request","data":{"version":"1.0","clientType":"atomnClient","requestId":<uint32>,"key":"<discovery-reply-key>"}}
   ```
2. Encrypt it with AES-256-CBC (PKCS7 padding) using the **primary** key `<discovery-request-key>`. The wire format is the 16-byte IV followed by the ciphertext.
3. Send it to all of these:
   - `224.0.0.251:5353` (ttl 1)
   - `224.0.0.252:5354` (ttl 1)
   - `239.0.1.251:25353` (ttl 4)
   - `239.0.1.252:25354` (ttl 4)

   Over USB, send it unicast to the same ports instead.
4. The reply is decrypted with the **common** key `<discovery-reply-key>` (the same key is sent inside the request). It contains `deviceIp, deviceName, deviceCode, deviceId, deviceSn, firmwareVersion, ...`.

### HTTP probes (V1)

- `/system?action=version`
- `/system?action=version_v2` (returns `machine_type`, `package_version`)
- `/system?action=get_dev_name`

## V2 framing (WebSocket binary messages)

```
0-1  0xBA 0xBE                magic
2-4  payload length           big-endian, 3 bytes
5    protocol type            bit7 = no body CRC
6-7  CRC16(payload)
8-9  CRC16(bytes 0..7)
10.. payload
```

- **CRC:** CRC-16/ARC (poly 0xA001 reflected, init 0). This matches the npm `crc` package's `crc16`.
- **Types:** `F0F7=1 F3F4=2 F8F9=3 JSON=4 BUFFER=5 NETWORK_CONFIG=32 FILE_TRANSFER=33 MEDIA_STREAM=34`.

**Request (type 4):**

```json
{"type":"request","method":"GET|PUT|POST","url":"/v1/...","params":{},"data":{},"timestamp":0,"transactionId":1,"sessionToken":"..."}
```

- **IDs:** transaction IDs run 1..65500.
- **Response:** `{"type":"response","transactionId","code":0,"data","message"}`. Any code other than 0 is an error, and 403 means the session expired.
- **Heartbeat:** `GET /v1/user/ping` with transactionId 65510, every 3 s. The socket is considered dead after 11 s without a reply. On the file and media sockets, send a type 33 or 34 frame with payload `0x02`; the device answers `0x82`.
- **Push events:** `{info, level, module, type}`:
  - Work events: `WORK_PREPARED`, `WORK_STARTED`, `WORK_PAUSED`, `WORK_STOPED`, `WORK_FINISHED`.
  - Mode and state: `MODE_CHANGE`, `P_IDLE`, `P_AUTOFOCUS`, `P_MEASURE`, `P_SLEEP`.

### Pairing (V2)

1. Send `GET /v1/user/parity` with `data: {userID: "<uuid>"|"mk-guest", userKey: "<pairing-key>", timezone, token: "<cached or empty>"}`.
2. Handle the response code:
   - `0`: success. It returns `{sessionToken, token}`. Cache `token` per serial number.
   - `406`: the user must press the machine's button. Poll `GET /v1/user/auth-status` every 1 s until it returns 0. A `410` means the wait timed out.
   - `409` or `503`: blocked.

The stock app also uploads the token to the xTool cloud. We don't have to.

## V2 endpoints (instruction socket)

| Purpose | Request |
|---|---|
| Machine info | `GET /v1/device/machineInfo` (sn, firmware, laserPower, laserType, `gcodeFormat`, ips) |
| Runtime state | `GET /v1/device/runtime-infos` (`curMode{mode,desc,subMode}`) |
| Configs | `GET/PUT /v1/device/configs` |
| Start / pause / stop | `PUT /v1/processing/state?action=start\|pause\|stop` |
| Progress | `GET /v1/processing/progress` |
| Job config after upload | `PUT /v1/processing/upload/config {fileType:"xf", autoStart:0\|1, taskId}` |
| Framing loop | `PUT /v1/processing/frame/replace {loopPrint:1}` |
| Power-loss resume | `GET /v1/processing/powerResume?action=query`, `PUT ...` |
| Autofocus | `POST /v1/laser-head/focus/control {action:"auto_start"\|"auto_stop"}` |
| Focus params | `GET/PUT /v1/laser-head/focus/parameter` |
| Laser head | `PUT /v1/laser-head/parameter`, `GET ?action=laser_ready` |
| Peripherals | `GET/PUT /v1/peripheral/param?type=gap\|machine_lock\|smoking_fan\|fill_light\|ir_led\|conveyor\|emergency_stop` |
| Motion delays | `GET/PUT /v1/motion_control/paramter` (the misspelling is the firmware's) |
| Raw command | `POST /v1/cnc/cmd` |
| Camera exposure | `POST /v1/camera {exp, stream, exp_time}` |
| Camera snapshot | `GET /v1/camera/snap` → `{filename}` → file download (fileType 5) |
| Live camera (?) | `POST /v1/platform/camera/live` + media_stream socket. It is in the shared API but **the F2U ext never calls it**. Test this first. |
| Alarms / stats / log | `GET /v1/device/alarms`, `/v1/device/statistics`, `/v1/log` |
| File transfer | `PUT /v1/filetransfer/upload\|download\|finish` |

### File transfer (V2)

1. Send `PUT /v1/filetransfer/upload {filetype, filename, filesize, digesttype:1, digestdata:<md5 hex>, channel, packetsize:1048576}`. The device answers with its own `packetsize`.
2. The device then requests data on the file socket with type 33 frames: `[0x01][channel][offset: 5 bytes][window: 3 bytes]`.
3. The client answers with `[0x81][channel][offset: 5 bytes][data...]`.
4. The transfer ends with `/v1/filetransfer/finish`.

Downloads mirror this with a 5 MB window.

File types: `1` = job, `2` = firmware, `3` = calibration/config, `5` = image or log.

## V1 endpoints (HTTP :8080)

| Purpose | Request |
|---|---|
| Info / status | `GET /device/machineInfo`, `/device/runningStatus` |
| Upload job | `POST /processing/upload?gcodeType=processing&fileType=xf` (octet-stream) |
| Framing | `POST /processing/upload?gcodeType=frame&fileType=txt&autoStart=1&loopPrint=1`, then `/processing/stop` |
| Control | `/processing/pause`, `/processing/stop`, `/processing/progress` |
| Config | `POST /config/get`, `/config/set` |
| Focus / mode | `/focus/control`, `/device/modeSwitch` |
| Peripherals | `/peripheral/{gap,machine_lock,smoking_fan,fill_light,ir_led,laser_head,emergency_stop,conveyor}` |
| Camera (port 8329) | `GET /snap?name=main\|deep&stream=0\|1` → JPEG, `/camera` (exposure) |
| Files | `/file?action=download` |

Firmware updates use port 8087: `/upgrade_version`, `/package?action=burn`.

## Job file: `xtool.xf` (tar archive)

| Member | Content |
|---|---|
| `preview.jpg` | 200×200 thumbnail |
| `border.gcode` | framing path |
| `motion.gcode` or `motion.bgcode` | the job; use `.bgcode` if `machineInfo.gcodeFormat` contains `"bin"` (format unknown) |
| `description.json` | `{mode, light: "blue"\|"red"\|"mixed", create_time, axis, time?}` |

The upload uses V2 fileType 1 with name `tmp.xf`, followed by the upload/config call.

### G-code dialect (from the JS templates; the motion body comes from the native lib)

**Head:**
```
# GS004-4 HEAD
G0 F<laserOffJumpSpeed>
M4 S0
G1 F180000
G0 X0 Y0
```

Optional head lines:
- `M114 S1`: framing only.
- `G100 F..`: U axis.
- `M535U..` / `M536U..`: conveyor or rotary.
- `G102 / G91 / G0Z<dz>F600 / G90 / G103`: Z move.

**Per-element block:**
- Source select: `G21` = blue (diode) or `G22` = red (fiber 1064 nm) (?)
- Fiber only: `G0H<pulseWidth>` (default 200) and `G0Q<mopaFrequency>` (default 65)
- `G4P1`
- Mode: `M523P10` (dot/bitmap) or `M523P40` (vector) (?)
- A `# blockConfig={powerFactor, overDist, accel, density, power:[min,max], bitmapMode, crossDot, startX, isVector}` comment

**Passes:** `# MUST` / `G104 P<repeat>` … `G105` makes the device loop the block.

**Tail:**
```
G90
G0 S0
G1 F180000
M536 U0
M6 P1
```

**Units sent:** power = percent × 10 (0–1000), speed = mm/s × 60 (mm/min).

There is no signing or encryption. Integrity checks are the MD5 on file transfers and CRC16 per frame.

## Camera

- **Two cameras:** stream 0 is the "main" overview camera, and stream 1 is the "deep" close-up camera. The close-up is used with the IR LED for height measurement.
- **Stills only in the stock app.** The stock app grabs snapshots, which is why there is no live view. Getting video means either (a) testing `/v1/platform/camera/live` plus the media_stream socket, or (b) polling snapshots fast enough to approximate it.
- **Calibration files:** downloaded on connect as `global_calib.json`, `global_ir.json` and `local_ir.txt` (fileType 3).
- **Correction:** done by the native `GS004_GS007_CORRECT_IMAGE {src_jpg, dst_jpg, calib_json}`. The calibration schema is unknown; capture real files to work it out.

## Pause between passes

The stock app can't do this. Passes are a device-side loop in one file, and resuming requires the physical button. Our options:

1. **Split passes into separate jobs.** Generate one `.xf` per pass (with the same origin) and upload the next one when the user is ready. This is fully under our control.
2. **Pause on progress.** Watch `/v1/processing/progress` and send `action=pause` at pass boundaries. This depends on how precisely progress is reported.
3. **Test resume.** Test whether `action=start` after a pause resumes the job. The stock code has the call but never uses it.

## Unknowns to capture from a real machine or export

- The G-code motion body produced by `lib_path_planning.dll`: galvo specifics, S scaling, how fill and bitmap are encoded. → Export an `.xf` from the stock app for a few simple test shapes and diff them.
- The `.bgcode` binary format, if this firmware reports `gcodeFormat: bin`.
- The calibration JSON schema.
- Whether live camera works.
