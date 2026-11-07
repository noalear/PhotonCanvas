# F2 Ultra (`GS004-CLASS-4`) device protocol

What the machine accepts on the network and the formats it takes, as far as PhotonCanvas needs
them. This was worked out for interoperability with the user's own machine and software.

**Status:** the sections headed "Verified on hardware" were tested on the user's F2 Ultra
(firmware 40.54.021). The rest is not yet confirmed on the machine. Items marked (?) are inferred.

## Verified on hardware (2026-10-12, F2 Ultra, fw 40.54.021.00.ht6)

Implemented in `src/pcprobe/`.

- **Discovery:** V2 encrypted multicast works. The device answers with `deviceCode: GS004-CLASS-4`, `platformVersion: 1.5`, `netType: wlan`.
- **Framing:** framing and CRC16 work as documented below. Device JSON responses are pretty-printed with tabs.
- **Pairing:** `/v1/user/parity` with `userID: "mk-guest"` returns `code 0, "user auth success"` and **no** sessionToken. No button press is needed, and every read endpoint works afterwards.
  - **A random UUID as userID makes the device drop the connection (1006).** Other user IDs must have some other format.
- **All channels share one id:** the instruction, file and media sockets must all use the **same** `?id=`, because the device pairs them by it. With different ids, file requests are silently ignored.
- **`machineInfo`:**
  - `laserType: ["RED","BLUE"]`, `laserPower: [60,40]`: RED = 60 W fiber, BLUE = 40 W diode.
  - `workSize 231.98 × 231.98` mm.
  - `gcodeFormat: ["txt","bin"]`, so **plain-text G-code is accepted**.
  - `packageFormat: ["xf"]`.
- **Snapshot:** `GET /v1/camera/snap` needs `params: {name: "main"|"deep", stream: "0"|"1"}`. The params must go in `params`, not `data`; otherwise the device returns `-1 "name is not string"`. It returns `{filename: "/tmp/main_<ts>.jpg"}`, which is then downloaded over the file channel.
  - Images are 4000×3000 JPEGs: about 630 KB for the overview, about 1 MB for the close-up.
  - The file request packet is sent with no body CRC (type 33 | 0x80).
- **Snapshot polling rate:** **3.9 fps** for the overview and 2.8 fps for the close-up, in a sequential loop on WiFi.
- **Live stream: not available on this firmware.**
  - `POST /v1/platform/camera/live {name, stream, action:"start"}` returns success.
  - The media_stream socket answers pings (`0x02` → `0x82`) but sends no frames.
  - `/v1/platform/camera/list` fails with "getCameraList failed".
  - `/v1/camera/live` returns 404.

  The live view is therefore built on snapshot polling (`pcprobe view`).
- **Not found (404):** `/v1/camera`, `/v1/camera/resolution`, `/v1/platform/camera/resolution`.
- **Close-up camera field (Camera → Close-up position, 2026-10-20, 21.1 mm focus):** found from 10 burned dots to within 0.03 mm.
  - It sees a turned, slightly keystoned area about 155 × 118 mm, centred near the middle of the bed. Its corners on the bed are (147, 201), (12, 125), (77, 26) and (199, 96).
  - About 27 px/mm in the middle of its 4000 × 3000 picture. That shows 0.4 mm patterns, but not single 0.05 mm lines.
  - The picture is turned −151.8° to look straight. The largest straight rectangle inside it is 83 × 74 mm: (67, 75) to (150, 149).
- **After a job (seen in logs, 2026-10-18/19):** the mode goes `P_WORKING` → `P_WORK_DONE` and stays there until the machine's button is pressed (then `P_IDLE`). PhotonCanvas now sends `PUT /v1/device/mode {mode:"P_IDLE"}` before an upload when it sees `P_WORK_DONE`. The same request leaves sleep and focusing. Not yet confirmed that this clears `P_WORK_DONE` on the machine.
- **Several connections at once (2026-10-23, `tools/RemoteProbe`, during a running job):** three clients were connected together (the desktop app plus two probe connections, each with its own `?id=` and its own `mk-guest` pairing). All three could read status, all three got every push, and none was dropped. The job was not disturbed. A remote-control server can therefore sit beside the desktop app.
- **Camera during a job (same run):** `/v1/camera/snap` still answers while `P_WORKING`. Each overview or close-up picture took 0.15 to 0.3 s, and 12 pictures 5 s apart all arrived. Back to back it managed only 1.1 pictures a second, and 1 of 8 failed with `-1 "get camera snap failed"`.
  - **But the pictures are black.** They are full 4000×3000 frames, but they average a brightness of 1 out of 255 (about 185 KB, against about 630 KB idle). Only the laser dot shows, plus a faint trail next to it when brightened.
  - `POST /v1/camera` (params `{exp, stream, exp_time?}`) is accepted during a job but changes nothing. That covers `exp 0` (the normal setting) and `exp 2` with `exp_time` 2, 20, 100, 500 and 2000. The overview was left at `exp 0` afterwards.
  - `GET /v1/peripheral/param?type=fill_light` reports `{front:165, back:79}` (on) during the job. `type=ir_led` returns `-3 "error action type !"`.
  - **The machine blanks both cameras for the whole job.** Five seconds after the job ended (`P_WORK_DONE`), with the same lighting, the overview averaged 82 and the close-up 113, both clear. During the job, every picture over a full minute was black, so the blanking isn't just while the beam is on. The user confirms the machine always does this. A time-lapse during a job therefore needs pauses, separate passes, or an outside camera.

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
   {"type":"deviceFind","method":"request","data":{"version":"1.0","clientType":"atomnClient","requestId":<uint32>,"key":"<discovery reply key>"}}
   ```
2. Encrypt it with AES-256-CBC (PKCS7 padding) using the **discovery request key** (32 characters; see *Machine keys* below). The wire format is the 16-byte IV followed by the ciphertext.
3. Send it to all of these:
   - `224.0.0.251:5353` (ttl 1)
   - `224.0.0.252:5354` (ttl 1)
   - `239.0.1.251:25353` (ttl 4)
   - `239.0.1.252:25354` (ttl 4)

   Over USB, send it unicast to the same ports instead.
4. The reply is decrypted with the **discovery reply key** (32 characters; the same key is sent inside the request). It contains `deviceIp, deviceName, deviceCode, deviceId, deviceSn, firmwareVersion, ...`.

### Machine keys

Three fixed strings are needed: the discovery request key, the discovery reply key and the pairing key. They are not published here or built into the program. The desktop app's **Settings → Connection → Connection keys** reads them from the user's own installed xTool Studio, or the user can type them in; they are saved to `keys.json` in the data folder.

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

1. Send `GET /v1/user/parity` with `data: {userID: "<uuid>"|"mk-guest", userKey: "<pairing key>", timezone, token: "<cached or empty>"}`.
2. Handle the response code:
   - `0`: success. It returns `{sessionToken, token}`. Cache `token` per serial number.
   - `406`: the user must press the machine's button. Poll `GET /v1/user/auth-status` every 1 s until it returns 0. A `410` means the wait timed out.
   - `409` or `503`: blocked.

Nothing has to be sent anywhere else: pairing is between the program and the machine only.

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
| Live camera (?) | `POST /v1/platform/camera/live` + media_stream socket. Accepted, but sends no frames on this firmware (see above). |
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

### G-code dialect (outline; see [gcode-f2-ultra.md](gcode-f2-ultra.md) for verified samples)

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

## Raw commands and stored settings (not yet tried)

- `POST /v1/cnc/cmd` with `{cmd: "<one G-code line>"}` as the **query parameters** (not the body) runs one command at once, outside a job, for example pointing the galvo (`G0X…Y…F60000`) or stopping the U axis (`M108U0`). PhotonCanvas's console uses it, only for what the user types or runs.
- `GET /v1/device/configs` returns the machine's stored settings, e.g. `ConveyorURate` (0.7459 × 100 ÷ calibration %) and `ConveyorAngleCompensate`.
- Conveyor belt moves outside a job: `PUT /v1/peripheral/param?type=conveyor` `{u: mm, action: "goto" | "stop" | "lock" | "free", speed: 9600}` (not used by PhotonCanvas yet).
  - Tried on firmware 40.54.021 with the rotary on the "snake 1" port (2026-11-06): `lock`, `goto`, `stop` and `free` all answer code 0 with `type=conveyor`, and -1 `"invalid type"` with `type=rotary`. `GET` with either type answers -3 `"error action type !"`. Code 0 only means accepted: whether the motor turns is not confirmed yet. `python -m pcprobe.motor_test` (or `tools/motor-test.cmd`) is a window for checking this by eye; it saves commands, answers and what was seen to `%USERPROFILE%\PhotonCanvas\motor-test`.
  - The machine can report `P_SLEEP` on the first connection and `P_IDLE` on the next, so check again before refusing to run.

## Camera

- **Two cameras:** stream 0 is the "main" overview camera, and stream 1 is the "deep" close-up camera. The close-up is used with the IR LED for height measurement.
- **Stills only.** The machine gives snapshots; the live view polls them (see above).
- **Calibration files:** the machine holds `global_calib.json`, `global_ir.json` and `local_ir.txt` (fileType 3). Their schema is unknown; PhotonCanvas calibrates the overview camera itself.

## Pause between passes

Passes are a device-side loop in one file, and resuming requires the physical button. Our options:

1. **Split passes into separate jobs.** Generate one `.xf` per pass (with the same origin) and upload the next one when the user is ready. This is fully under our control.
2. **Pause on progress.** Watch `/v1/processing/progress` and send `action=pause` at pass boundaries. This depends on how precisely progress is reported.
3. **Test resume.** Test whether `action=start` after a pause resumes the job.

## Unknowns to capture from a real machine or export

- The motion body for every kind of layer: galvo specifics, S scaling, how fill and bitmap are encoded. → Export G-code from xTool Studio for a few simple test shapes and compare (see [gcode-f2-ultra.md](gcode-f2-ultra.md)).
- The `.bgcode` binary format, if this firmware reports `gcodeFormat: bin`.
- The calibration JSON schema.
- Whether live camera works.

## Verified on hardware: first job (2026-10-12, smiley, 30 mm, blue 70% 40 mm/s, 8 whole-shape passes, 1 s pause)

- **Upload** works as documented:
  1. Send `PUT /v1/filetransfer/upload` with the MD5.
  2. The device sends `0x01` window requests on file_stream.
  3. We reply with `0x81` data packets (no body CRC).
  4. The device sends `/v1/filetransfer/finish` with code 0.
  5. Send `PUT /v1/processing/upload/config {fileType:"xf", autoStart:0, taskId}`.
- **Text G-code inside `.xf`:** a `motion.gcode` member is accepted in place of `motion.bgcode`. The tar is ustar, mode 0666. `description.json` was minimal (mode, light, create_time, axis, processingParams), and the device accepted it.
- **Whole-shape pass mode works**, with `G4P1000` between passes. The user judged the cut "looks great"; the eyes and mouth dropped cleanly out of 3 mm basswood.
- **Events** arrive as requests: `{"type":"request","method":"REPORT|NOTIFY","url":...,"data":{"module","type","info","level"}}`.
  - After upload: `MODE_CHANGE P_WORK` then `WORK_PREPARED`. The machine is armed and waiting for the button.
  - `BUTTON SHOERT_PRESS info "1"` is the start/pause button. `"2"` is the other button; it did not start the job.
  - `GAP OPEN/CLOSE` is the lid. The job only started after the lid was closed.
  - Start: `MODE_CHANGE P_WORKING` + `WORK_STARTED`.
  - Pause (short press during the job): `P_PAUSE` + `WORK_PAUSED`.
  - Cancel (long press while paused): `WORK_STOPED`, then `WORK_RESULT WORK_FINISHED {result:"failed", timeUse}`, then `P_IDLE`.
- **Head Z** reads within about 0.06 mm of the commanded value (27.058 for 27). Distance = z − 6.
- **Cameras** are black while a job runs, because the fill lights are off.
- **`G4 P` is milliseconds (verified).** A 2-pass job at 0% power with `G4P10000` between passes reported `timeUse: 10`, and the job finished 11.3 s after `WORK_STARTED`.
- **Lid-open processing:** `PUT /v1/device/configs {alias:"config", type:"user", kv:{gapCheckWithKey:false}}` turns on xTool's "Enclosure-opened processing", which lets the button start jobs with the lid open. The setting is stored on the machine. Read it back with `GET` and `kv:["gapCheckWithKey"]`. It was turned ON for the user on 2026-10-12, at their request (`pcprobe lid-open <ip> on|off`).
- **Loaded job state (2026-10-13):** `GET /v1/device/runtime-infos` reports a job waiting for the button as `curMode {mode:"Work", subMode:"workReady", taskId}`; running is `subMode:"working"`, paused `"workPause"`, framing has `desc:"frame"`. MODE_CHANGE events call the same states `P_WORK`/`P_WORKING`/`P_PAUSE`. Idle is `P_IDLE` in both. `taskId` is the one sent in `upload/config`. A loaded job survives reconnecting; `PUT /v1/processing/state?action=stop` clears it (no motion), after which a new upload is accepted. The MODE_CHANGE `P_WORK` event for an upload arrives a few ms before the `upload/config` reply.
- **Framing (?, not yet run on hardware):** upload the border G-code as `tmpFrame.gcode` (file type 1), then `PUT /v1/processing/upload/config {fileType:"txt", autoStart:1, loopPrint:1, gcodeType:"frame"}`; it starts at once and loops. Moving the outline: upload `tmpFrameNew.gcode`, then `PUT /v1/processing/frame/replace {loopPrint:1}`. Stop: `PUT /v1/processing/state?action=stop`. runtime-infos reports `mode:"Work"` with `desc:"frame"` (or `frameExt`) while framing. The frame G-code is the job's `border.gcode`.
- **Pointer lights out of photos (?, not yet confirmed on hardware):** before a photo, send `PUT /v1/peripheral/param?type=ir_led {action:"off", index:0}` (auto-focus IR light) and `PUT /v1/laser-head/parameter {action:"power", ctrl:"set", value:0}` (centre pointer), waits 100 ms, takes the photo, then sends `action:"on"` and `value:<focusPower>` (read with `GET /v1/device/configs kv:["focusPower"]`; 21 on the user's machine).
