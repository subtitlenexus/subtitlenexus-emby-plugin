# Subtitle Nexus (Emby plugin)

Generate AI subtitles for your Emby library via [Subtitle Nexus](https://subtitlenexus.com).

This plugin hashes each video, looks up cached subtitles on Nexus, and — on a
miss — extracts the audio with `ffmpeg`, uploads it to Nexus, submits a
transcription request, and streams the resulting SRT next to your video as
`{filename}.{lang}.srt`. Emby picks up the sidecar on the next library
refresh, which the plugin triggers for you.

> **NOTE: Under construction — not guaranteed to work.** v0.1.0 code is
> complete but has not been compiled or installed on a real Emby server
> yet. See the [TODO](#todo) section below for the punch list.

## TODO

### Before first install

- [ ] `dotnet build -c Release` against `MediaBrowser.Server.Core` 4.9.1.90 —
      Emby docs are notoriously thin, so expect 1–2 namespace / API
      shape fixes on first compile.
- [ ] Drop the resulting `SubtitleNexus.dll` into Emby's plugin
      directory, restart, confirm the plugin appears in **Settings →
      Plugins**.
- [ ] Confirm the embedded `configPage.html` renders, loads existing
      settings via `ApiClient.getPluginConfiguration(pluginId)`, and
      saves back via `ApiClient.updatePluginConfiguration`.
- [ ] Hit `GET /SubtitleNexus/Validate` with an admin token and confirm
      it returns user info from Nexus.

### Before first generation job

- [ ] End-to-end test: pick one short video missing a sidecar SRT, hit
      `POST /SubtitleNexus/Generate/<InternalId>`, watch the worker logs.
- [ ] Verify `async Task<object>` works in Emby's `IService` dispatcher.
      If async dispatch misbehaves, swap to synchronous `object` returns
      with `.GetAwaiter().GetResult()` shims.
- [ ] Verify `[Authenticated(Roles = "Admin")]` attribute import from
      `MediaBrowser.Controller.Net` is correct on the targeted SDK
      version.
- [ ] Verify `ILibraryManager.QueueLibraryScan()` actually picks up the
      new sidecar SRT. If a `RefreshItem` overload exists on the
      `netstandard2.0` surface, swap to it for a surgical refresh
      instead of a full library scan.
- [ ] Stress-test the hand-rolled `Json.cs` parser against real Nexus
      response shapes — particularly the upload-start, submit, and
      poll-status payloads. Watch for nested objects or unexpected types.
- [ ] Verify the explicit little-endian byte assembly in `NexusHasher.OsHash`
      produces the expected OpenSubtitles hash (cross-check against a
      reference Python `oshash` implementation on an identical .mkv).
- [ ] Run the scheduled task against a small library and confirm
      bounded parallelism (`MaxConcurrentJobs`) works.

### Before publishing

- [ ] Replace the placeholder GUID `4b8a2c5f-7e3d-4f9a-8c1b-9d6e2a3b4c5d`
      (used in `Plugin.cs` and the config HTML's `pluginId` constant)
      if your plugin distribution channel issues GUIDs.
- [ ] Add a CI workflow (GitHub Actions) to build the DLL on push.
- [ ] Add a real `LICENSE` file matching the MIT declaration in this
      README.
- [ ] Real semver and version sync across `Plugin.cs` and the csproj.

### Deferred (post-v1)

- [ ] In-page "Generate Subtitles" button on Emby's web UI item page.
      Emby exposes some frontend extension points but the cleanest path
      is unclear — track community plugin patterns and revisit.

## Requirements

- Emby Server 4.8 or newer (plugin is built against `MediaBrowser.Server.Core` 4.9.1.90)
- `ffmpeg` on `$PATH`, **or** an absolute path configured in the plugin
- A Subtitle Nexus account and API key from
  <https://subtitlenexus.com/account>

## Install

1. Build the plugin (or grab the release DLL):

   ```sh
   dotnet build -c Release
   ```

2. Copy `bin/Release/netstandard2.0/SubtitleNexus.dll` into Emby's plugin
   directory:

   - Linux:   `/var/lib/emby/plugins/`
   - Docker:  `/config/plugins/` (inside the container)
   - Windows: `%AppData%\Emby-Server\plugins\`
   - macOS:   `~/Library/Application Support/emby-server/plugins/`

3. Restart Emby Server.

4. Open **Settings → Plugins → Subtitle Nexus** and fill in:

   - **API Key** — required, from <https://subtitlenexus.com/account>
   - **Subtitle Language / Audio Language** — defaults `en` / `ja`
   - **Model Version** — defaults `lulu-2605`
   - **Visibility** — `PUBLIC` (shared with Nexus community) or `UNLISTED`
   - **ffmpeg Path** — leave blank if `ffmpeg` is on `$PATH`
   - **Max Concurrent Jobs** — 1 (serial) by default; bump to 2–4 for faster
     library scans if your network and quota allow

5. Click **Save**.

## Use

### Library-wide scheduled task

**Settings → Scheduled Tasks → Generate missing Subtitle Nexus subtitles**

The task scans every Movie / Episode / Video item, skips those that already
have a `{filename}.{lang}.srt` sidecar, and submits the rest. Default
schedule is weekly (Sunday 03:00) — you can run it on demand from the
Scheduled Tasks page or change the trigger to nightly / interval.

### Per-item REST endpoint

To process a single item, POST to the plugin's endpoint with the Emby
`InternalId` of the video:

```sh
curl -X POST \
     -H "X-Emby-Token: $EMBY_API_KEY" \
     "http://emby.local:8096/SubtitleNexus/Generate/12345"
```

Returns JSON like:

```json
{
  "item_id": 12345,
  "name": "My Movie",
  "result": "Generated"
}
```

`result` is one of: `Cached`, `Generated`, `AlreadyPresent`, `Skipped`,
`Error`.

> Finding the `InternalId`: Emby's REST API exposes items at
> `GET /Items?Ids=...` and surfaces it as the integer `Id` field on each item
> when called from a server admin context.

### Validate your API key

```sh
curl -H "X-Emby-Token: $EMBY_API_KEY" \
     "http://emby.local:8096/SubtitleNexus/Validate"
```

Returns the username, plan, remaining subtitle credits and tokens — handy
to confirm the key works before kicking off a big scan.

## How it works

The pipeline:

1. **Hash** the file with the OpenSubtitles 64-bit hash and a SHA256 over
   the head+tail+size endpoints. Both are sent to Nexus so subsequent runs
   on the same file dedupe to the same cache entry.
2. **Cache lookup** via `/v1/subtitle/search/`. On a hit, the SRT is
   downloaded and written directly. `IgnoreCommunitySubs = true` restricts
   the lookup to your own previously generated subtitles.
3. **Extract audio** with `ffmpeg -vn -sn -ac 1 -ar 16000 -codec:a libmp3lame -b:a 64k`.
   Mono, 16 kHz, ~64 kbps mp3 — small enough to upload quickly and lossy
   enough not to affect transcription quality.
4. **Upload** the mp3 to a Nexus-issued presigned S3 URL, then call
   `/v1/async-upload/av/finish/`.
5. **Submit** the transcription request, then **poll** every 20 s. Once
   Nexus reports `has_file`, the plugin streams the partial SRT to disk so
   you can read along while transcription finishes. Each new partial
   triggers an Emby library refresh.
6. **Final download** when the request reaches `COMPLETED`.

## Settings reference

| Setting | Default | Description |
| --- | --- | --- |
| `ApiKey` | (empty) | Required. From subtitlenexus.com/account. |
| `Domain` | `api.subtitlenexus.com` | Nexus API host. |
| `Model` | `lulu-2605` | Nexus model slug. |
| `SubtitleLanguage` | `en` | Output language code. |
| `AudioLanguage` | `ja` | Source audio language code. |
| `Visibility` | `PUBLIC` | `PUBLIC` shares with the Nexus community, `UNLISTED` keeps it private. |
| `IgnoreCommunitySubs` | false | Only reuse your own cached subtitles. |
| `DisableSubtitleSearch` | false | Always submit a new request (skip cache lookup). |
| `AutoPurchasePastDailyLimit` | false | Spend tokens automatically when the daily free-download limit is hit. |
| `FfmpegPath` | (empty) | Absolute path to ffmpeg, or blank to use `ffmpeg` from `$PATH`. |
| `MaxConcurrentJobs` | 1 | Parallelism for the library-scan task. |

## Troubleshooting

- **"ffmpeg failed (exit ...)"** — Emby couldn't launch ffmpeg. Set
  `FfmpegPath` to an absolute path (find it with `which ffmpeg`).
- **"Health check failed"** — DNS or firewall is blocking
  `api.subtitlenexus.com`, or your API key is wrong. Hit the
  `/SubtitleNexus/Validate` endpoint to see the underlying error.
- **SRT is written but Emby doesn't show the caption** — the plugin queues
  a library scan after each successful generation; if Emby's task queue is
  busy, the caption may take a minute to attach. You can manually re-run
  **Settings → Library → Scan All Libraries**.
- **Subtitle Nexus rate-limit** — set `AutoPurchasePastDailyLimit = true`
  if you want jobs to keep going past the free-download cap. Otherwise the
  download step will surface an HTTP 402 error.

## Development

```sh
git clone https://github.com/subtitlenexus/subtitlenexus-emby-plugin.git
cd subtitlenexus-emby-plugin
dotnet restore
dotnet build -c Release
cp bin/Release/netstandard2.0/SubtitleNexus.dll /path/to/emby/plugins/
```

The plugin targets `netstandard2.0` and references `MediaBrowser.Server.Core`
4.9.1.90, the published Emby plugin SDK.

## License

MIT.
