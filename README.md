# UE MM

A mod manager for Unreal Engine games. The first and, for now, only supported
game is **Phantom Blade Zero**; the interface is themed for it. The executable
keeps the file name `UEModManager.exe`.

Its catalog, the openmods page, puts the mods of [openmods](https://openmods.nexus)
a click away. It is built in, but switched off for now (see Catalog switch).

It keeps its own copy of every mod and records a SHA-256 checksum for every
file it places. A file it did not place is never overwritten or removed.

## Download

Get `uemm_<game>_<version>.zip` from the latest
[release](https://github.com/KintaroModding/uemodmanager/releases/latest). Unzip
it into a folder you can write to and start `UEModManager.exe`; it needs the
[.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0).
How to install and use it, for players: [openmods.md](openmods.md).

## Interface language

In **Settings → Language**, choose **English** or **简体中文**. The interface
changes at once, on the page you are on, and the choice is saved in
`Data/settings.json`. Until you choose, the app is in English. When Windows
uses a language the app has (its display language, format settings or install
language), the app asks once at start whether to switch to it: in that language
and in English, with **Yes** and **No** in English. Either answer is saved, so
the question does not come again. Mod names, paths, downloaded
descriptions and license texts stay as they are. Logs and help reports are
always English, so that a helper can read them.

### Translations

The English text in the code is the key. Every other language is one file,
`src/UEManager.UI/Localization/<code>.json` (such as `zh-CN.json`), that maps
each English text to its translation; `"$name"` is the language's own name, and
`"$englishName"` its name in English, for the question that offers it. A text
without a translation shows in English.

To add a language, copy `zh-CN.json` to `<code>.json`, set `$name` and
`$englishName`, and translate every value. Keep the values in braces, such as `{0}` and
`{1:N0}`; their order may change. The build finds the new files, and the app
lists the language.

In code, a text the player sees goes through `L.T("English text")`,
`L.F($"English text {value}")` or `L.Plural(count, "{0} mod", "{0} mods")`;
`L.Join` puts sentences one after another. Core writes its messages in English,
as `new ModException($"…")` and `problems.Add($"…")`: the UI translates them
when it shows them (`L.Message`), and the log keeps the English. XAML uses
`{ui:Loc 'English text'}`. View models that compute texts inherit
`LocalizedObservableObject`, so their texts follow a change of language.

`TranslationTests` reads these texts from the source, and the names, taglines
and UE4SS notes from the game profiles. It fails when a language file lacks a
text, holds one the app no longer uses, or loses a value in a translation. So
after a change to an English text, update every language file too.

## Supported mod types

| Type | Recognised by | Deployed to |
| --- | --- | --- |
| PAK / IoStore | `.pak`, `.utoc` + `.ucas` | `<Project>/Content/Paks/~mods` |
| Blueprint | package files below a `LogicMods` folder | `<Project>/Content/Paks/LogicMods` |
| UE4SS Lua / DLL | a folder with `Scripts/main.lua` or `dlls/*.dll` | UE4SS's `Mods` folder |

UE4SS sits next to the game's exe, which most Unreal games keep in
`<Project>/Binaries/Win64` and Phantom Blade Zero in `platform/<store>` (see
[Game profiles](#game-profiles)). Its `Mods` folder there is `ue4ss/Mods` for
UE4SS 3.x and `Mods` for 2.x, detected from where `UE4SS.dll` is. A UE4SS mod
is deployed as its whole folder; when it has no `enabled.txt`, the manager adds
one, so UE4SS starts it without `mods.txt` being edited. Disabling removes the
folder again. Lua, DLL and Blueprint mods can be installed before UE4SS is,
with a warning.

A download with several folders that hold the same files (options such as
`x1.5/`, `x2/`, `x10/`) offers them as one choice, sorted by number; the chosen
variant is recorded with the mod.

### Safety

- Nothing in the game folder changes while the game runs: every install,
  enable, disable, uninstall and UE4SS change first looks for the game's
  executables among the running processes.
- A removal changes all of a mod's files or none: every file is moved aside
  before any is deleted, so a file in use puts everything back.
- Saving the mod list (`mods.json`) is the last step of every change, and
  moved files are deleted only after it. A virus scanner or a sync tool may
  hold the list: the manager tries three times, and if it still cannot save,
  it undoes the change, so the game and the list always agree.
- A deployed file that was changed outside the manager stops its removal,
  except a **settings file**, which the player is meant to edit: a UE4SS mod's
  `Scripts/config.lua`, or a `.json`, `.ini`, `.cfg`, `.toml`, `.yaml` or
  `.txt` file that a download keeps next to its pak in a `~mods` or
  `LogicMods` folder (such as a mod that reads its color from `~mods`). Files
  named like a readme, license or change log are no settings, and neither are
  other files beside the packages: they are not installed. Disabling keeps the player's
  version for the next enable. An update asks whether to keep it (the default) or take the new
  defaults. Before an update or an uninstall replaces it, a copy goes to
  `Data/games/<id>/backups/`. On the Mods page, **Edit settings** opens it in
  the app's own editor (AvalonEdit): the copy in the game, with simple colours
  for Lua, JSON and Name=Value lines, **Save** (Ctrl+S) and **Close**, and a
  question before unsaved changes are lost, also when the app closes. A save
  keeps the file's encoding and line breaks. **Reset settings** puts back the
  file the mod shipped with (kept in the library under `.defaults`) after the
  same backup.
- A download that receives no data for 60 seconds stops, and a failed download
  leaves no file behind.
- One app runs per data folder (`Data/instance.lock`). A second start, as by
  "Open with" in Explorer, hands its downloads to the running app and ends.
- The mod list belongs to the game, not to one install of it. When enabled
  mods are still in another install (a second copy of the game), the app asks
  first: **Move mods** removes them there, all or none, and installs them here;
  **Keep the other folder** goes on using that install. A game folder that was
  moved, with the old place gone, is followed without asking.

## Installing UE4SS

When UE4SS is missing, Home, the UE4SS page and the install review offer
**Install UE4SS…**. The popup installs a UE4SS 3.x release package
(`dwmapi.dll` plus the `ue4ss` folder) next to the game's exe. Without that
exe, it installs nothing:

- **Download and install** fetches the release named by the online manifest
  (below), or else the fixed `ue4ssPackageUrl` of the game profile, and refuses
  a file that does not match the given SHA-256.
- **Install from file…** takes a release zip the user downloaded from
  `ue4ssDownloadUrl`. It always works, also offline. Dropping that zip on the
  window does the same after a confirmation.

The installed version, and each file of the release with its SHA-256, is
recorded in `Data/games/<id>/ue4ss.json`. An update compares the game's files
with that record:

- `UE4SS-settings.ini`, `mods.txt`, `mods.json` and every `load_order.txt` are
  replaced only when the user has not changed them; changed ones are kept.
- Files of the previous release that the new one no longer has, such as an old
  signature file, are removed, unless the user changed them.
- Files of managed mods are never touched.

Without a file list (UE4SS installed by hand, or by version 1.0.x of the app),
the update keeps those four settings files and removes nothing. A `dwmapi.dll`
that is already there while UE4SS is not stops the install, and a failed step
puts every file back.

### UE4SS manifest

At every start the app reads the profile's `ue4ssManifestUrl`
(`KintaroModding/metadata`, `uemodmanager/manifest.json`):

```json
{ "ue4ss": { "<game id>": { "version": "v…", "url": "https://…/….zip", "sha256": "…" } } }
```

When the entry's version differs from the recorded one (or none is recorded,
as for a UE4SS installed by hand), Home and the UE4SS page offer **Update to
v…**. A new UE4SS build therefore ships by editing the manifest, without a new
app build. Offline or failing, the check stays silent.

### UE4SS page

The UE4SS page holds everything about UE4SS: its status and folders, how many
installed mods need it, install, update and removal, and the **log**. The log
is the end of `UE4SS.log` (the last 2000 lines), which UE4SS writes anew at
every game start. Lines that speak of an error are red, warnings orange, and
the mod folders an error names are listed. The page reads the log each time it
opens; **Refresh**, **Copy** and **Open log file** are next to it. **Clear**
hides the lines so far, so **Refresh** then shows only what UE4SS writes after
that, until the next game start; **Show all** brings them back. The file itself
is never changed, so a help report still holds the whole log. The search box
shows only the lines that contain its text, in any case, such as a mod's name,
together with **Only errors and warnings**, and counts them.

The app looks every 5 seconds whether the game runs. When a game it saw running
closes, however it was started, the app reads the log of that game start. If
errors name a UE4SS mod the manager installed, a note says so, with **Show
log**. Errors of UE4SS's own mods, such as `BPModLoaderMod`, never cause one.
On the Mods page, such a mod, while enabled, carries a red line: **UE4SS
reported errors at the last game start**. A click on it opens the UE4SS page
with the log searched for the mod's folder. The app reads the log for this at
start too, so the line stays until a game start without those errors, or until
the mod is updated.

## openmods page

The openmods page (the catalog, switched off for now: see Catalog switch) lists the game's mods from
[openmods](https://openmods.nexus): the game is found in openmods' games list
by its Steam app id, and its own list is read from there. The lists are read
when the page opens, at most every 10 minutes (openmods' own refresh interval),
and only format 1 is accepted; unknown fields are ignored, and only https links
are used.

- **Install** downloads the release's only zip from GitHub, checks its size
  and, when the list gives one, its SHA-256 (`files[].sha256`), and installs
  it. The installed mod records the catalog id (`owner/repo` of the source).
- A download that needs a look opens the install review instead: several
  parts, a warning (such as UE4SS missing), a mod installed by hand that it
  would replace, or settings the player changed.
- When the list names a newer version than the installed one, the card offers
  **Update to …**. An update replaces the mod with the same catalog id, even
  when the new version renamed its files.
- A catalog mod never takes over files of another enabled mod: the install
  stops and names that mod.
- The UE4SS entry opens the manager's own UE4SS install.
- A search field filters by name, description, author and tag. The list sorts
  by name or by the date of the latest release (newest first by default); the
  choice is kept in `settings.json`.

`--catalog <url>` reads another games list, such as a test copy; the page then
says that it is not the real one.

Network accesses: the UE4SS manifest and the update check at start, the
openmods lists and cover pictures when the openmods page opens (only while the
catalog is switched on), and the downloads the user starts. Pictures are kept in memory only.

### Removing UE4SS

**UE4SS → Remove UE4SS…** clears UE4SS completely after a confirmation,
whoever installed it: `dwmapi.dll` and the whole `ue4ss` folder (for UE4SS
2.x, its files and `Mods` folder in `Win64`), including UE4SS's settings and
every mod inside. Managed mods that lived there are disabled; their library
copies stay, so they can be enabled again once UE4SS is back. Everything is
moved aside first, also the pak of a mod that has a Lua part, so a file in use
stops the removal without leaving UE4SS or a mod half removed.

## Save backups

Save backups are offered only when the game profile names the save folder
(`saveFolder`). Games keep their saves in different places, such as
`%LOCALAPPDATA%`, `Documents\My Games`, a folder with a name of their own or
the install folder, so the folder is never guessed. Without it, **Settings →
Save backups** is greyed out, and its tooltip says that it comes in the next
update. Phantom Blade Zero keeps its saves in the install folder, in
`ProjectPBZ/Saved/SaveGames`, with a folder per store and player inside. The
backup at launch is off by default.

A broken mod can break a save. When the player turns it on, **Launch game**
first zips the save folder into `Data/games/<id>/save-backups/` and keeps the
newest 5. Without saves yet (the folder is empty or not made yet) the game
starts with a note; when the backup fails, the app asks whether to start the
game anyway. Starting the game from Steam, Epic or a shortcut makes no backup.
**Settings → Save backups** shows the folder and the backups, turns the backup
at launch on or off, and offers **Back up now**.

**Restore…** next to a backup puts it back, after a question and with the game
closed: the save folder then holds exactly the backup's files. The saves of now
are backed up first, so a restore can be undone. The backup is unpacked next to
the save folder before anything changes; the old folder moves aside until the
new one is in place, and moves back when a step fails (a file in use). Steam
may then report a conflict with its cloud saves; the question says to choose
the files on the PC.

The save folder is the profile's `saveFolder` (relative to the install folder,
or a full path in which environment variables such as `%LOCALAPPDATA%` are
expanded), or the folder the player chose, which wins.

## Game updates

The app remembers the store's build of the game (Steam build id, Epic version)
in `settings.json`. When it changes while mods are enabled, Home says that the
game was updated and that mods may need updates, until the player clicks
**Got it**.

## Report a problem

**Settings → Report a problem…** (also on the UE4SS page) opens a window where
the player says in a few words what happened. The app adds the facts a helper
asks for first: app and Windows version, game, store and build, UE4SS, every
mod with its state, files it did not install, the UE4SS log's errors and
warnings, and the last warnings and errors of its own log. The Windows user
folder becomes `%USERPROFILE%`. Nothing is sent: **Copy report** puts the text,
in a code block, on the clipboard for the help channel on Discord.

## Mod details

A click on a mod's name on the Mods page shows its details: type, version,
variant, when and from which download it was installed, its size, and every
file it placed with its path in the game folder (the full path as a tooltip),
size and state: in the game, a settings file the player may change, changed
outside the manager, or missing. The state is checked by size, as the list does,
not by checksum. A disabled mod lists its files as not in the game.

## Test without mods

**Test without mods** on the Mods page turns every enabled mod off, all or none,
and remembers which ones (`OffForTest` in `mods.json`, so it survives a
restart). The player starts the game and sees whether the problem stays.
**Turn them back on** turns exactly those mods on again, all or none; a mod the
player turned on meanwhile stays on, and an uninstalled one is skipped. UE4SS
itself and files the manager did not install stay in the game.

## Mod sets

**Mod sets…** on the Mods page saves the mods that are on now under a name, such
as "Co-op" or "Hard mode" (`sets` in `mods.json`); a set with the same name is
replaced. While a test without mods is on, the mods it turned off count as on.
**Use** turns exactly the mods of a set on and all others off, all or none, with
the same checks as a single enable or disable, and ends a test without mods.
The set whose mods are on now is marked **In use**. An update keeps a mod in its
sets; an uninstall takes it out of them. Deleting a set asks first, in the
window itself (Esc keeps the set), and changes no mods.

## Keyboard

| Key | Does |
| --- | --- |
| Ctrl+F | goes to the search box: the log's on the UE4SS page, the mod search from every other page |
| F5 | checks the mods' files in the game again; on the UE4SS page it also reads the log again |
| Ctrl+S | saves in the settings editor |
| Esc | closes a dialog, or answers its question with the safe choice |

Ctrl+F and F5 wait while a dialog is open.

## Reset modding

**Settings → Reset modding…** takes the game back to its original files, so the
player can start clean. A window first shows what changes:

- every enabled mod is turned off, all or none;
- UE4SS is removed completely, with its settings and every mod in its folder;
- files in `~mods` and `LogicMods` that the manager did not install move to a
  backup folder (`Data/games/<id>/backups/<date> reset/`), and the emptied
  folders go.

Two choices go further: **delete the mods from the manager** (as when each is
uninstalled: changed settings are backed up first), and **reset the app's
settings** (`settings.json`), after which the app starts again. Without them,
the mods stay in the Mods list, turned off. Backups of saves and settings stay.
Each step is saved before the next, so a step that fails (a file in use) leaves
a consistent state, and a second reset finishes the work. Mods put straight
into the game's `Paks` folder by hand are not found; the store's file check
(Steam: Verify integrity of game files) catches those.

## Solution layout

```text
src/
  UEManager.Core/         Game discovery, archive staging, mod install/enable/disable/uninstall.
                          No UI code. Targets net8.0.
    Games/Profiles/*.json Game profiles: store ids and folder layout. Built into the assembly.
    Catalog/              Reading the openmods lists (see Catalog switch).
  UEManager.UI/           WPF application (net8.0-windows). Builds UEModManager.exe.
    Catalog/              The openmods page (see Catalog switch).
    Theme/                Palette, control styles and optional art. Built into the exe
                          (see Theme/README.md).
  UEManager.Core.Tests/   xUnit tests for Core. Covers every operation that touches the game folder.
  UEManager.UI.Tests/     xUnit tests for the UI: the language files against the source, and a
                          change of language in the open window.
```

## Build

Requires the .NET 10 SDK (pinned in `global.json`). The output targets .NET 8.

```powershell
dotnet build src/UEManager.slnx
dotnet test src/UEManager.slnx
```

## Catalog switch

The catalog (the openmods page) is always built in. `Catalog` in
`src/Directory.Build.props` switches it on; it is `false` for now, so the app
shows no openmods page, reads no openmods list and takes no `--catalog` option.
The rest of the app reaches the catalog only through partial methods
(`CreateCatalog`, `OnModsChanged`, `ReadCatalogArgument`,
`AddCatalogResources`), which do nothing while it is off, and through two
resource keys (`CatalogNavItem`, `CatalogPage`) that only the catalog fills.

Every publish checks the exe it made: it must hold the main window and the
openmods page. A build can, rarely, go wrong without an error; such an exe is
deleted and the publish fails with "came out broken". Publish it again.

## Release

```powershell
./publish.ps1
```

Publishes the app for one GitHub release, after running the tests:

```text
dist/uemm/UEModManager.exe
dist/release/uemm_<game>_<version>.zip
```

The exe is a single file, framework dependent (needs the .NET 8 Desktop
Runtime), and passed the publish check (see Catalog switch).
`-GameProfile <id>` publishes for another game.

Game profiles and theme art are built in, and so are the license texts of the
bundled libraries (`src/UEManager.UI/Licenses`), shown on the About page. Update
those texts when a library is added or upgraded.

To publish a release on GitHub: tag it `v<version>` (as in
`src/Directory.Build.props`) and attach the zip from `dist/release`. The update
check reads that release.

`PreRelease` in `src/Directory.Build.props` marks a pre-release: while it is
`true`, a yellow **Pre-release** box below About opens a short note that the
app is early in its development and changes often. Releases set it to
`false`.

## Updates

At every start the app asks GitHub for the newest release of
`KintaroModding/uemodmanager` (`releases/latest`) and shows the result below the
version, at the bottom of the sidebar:

- **Up to date** (green): the newest release is not newer than this version.
- **Update available** (orange): a click opens the update window.
- **Update check failed** (grey): offline, no release yet, or GitHub's limit
  of 60 requests per hour; the tooltip says which. Nothing else happens.

A new version is announced once, without a sound: a banner in the middle of
the window for 3 seconds, then the orange box flashes once. While a dialog or
the busy screen is open, the banner waits. The announced version is kept in
`settings.json` (`announcedUpdate`), so the next start only shows the orange box.

The app takes `uemm_<game>_…zip` from the release. The update window offers:

1. **Update now**: downloads that zip, checks its size and SHA-256 (from
   GitHub's digest), checks that the exe inside carries the announced version,
   then renames the running exe to `<name>.old`, puts the new one in its place
   and starts it with the same options. Windows refuses to overwrite a running
   exe but lets it be renamed. The next start deletes the `.old` file. If any
   step fails, the old exe stays in place.
2. **Open release page**: to download the zip and replace the exe by hand.

## Game profiles

`src/UEManager.Core/Games/Profiles/phantom-blade-zero.json` tells the manager where
the game keeps its files. Paths are relative to the install folder, and
`{Project}` stands for the Unreal project folder:

```json
"projectFolder": "ProjectPBZ",
"paksPath": "{Project}/Content/Paks",
"modsPath": "{Project}/Content/Paks/~mods",
"binariesPath": "platform/*",
"shippingExecutable": "PBZ.exe",
"saveFolder": "{Project}/Saved/SaveGames",
"executable": "steam/PBZ.exe"
```

The Phantom Blade Zero layout is the one of the released game's Steam install.
Its real exe is not in `ProjectPBZ/Binaries/Win64`, as in most Unreal games,
but in a folder per store: `platform/steam/PBZ.exe`, started by
`steam/PBZ.exe`. `binariesPath` names the folder of the real exe, where UE4SS
goes (default `{Project}/Binaries/Win64`); a `*` in it matches any folder, and
the one that holds `shippingExecutable` wins, the one named after the store
first. When no folder has a file of that name, as may be the case in another
store's build, the largest exe of at least 50 MB counts as the game's exe: the
game's own is hundreds of MB, its helpers a few. Without `shippingExecutable`,
the exe is the usual `<Project>-Win64-Shipping.exe`. To correct the profile,
edit the file and rebuild. `ProfileTests` checks that every built-in profile parses, and
`GameLocatorTests` reads a copy of the Steam layout with it.

The game is found through Steam (`steamAppId`, all library folders) or the Epic
Games Launcher (`epicDisplayName`), or chosen by hand. In an install a store
names, a project folder other than the profile's is accepted too: any folder
(except `Engine`) that holds `Content/Paks` and an exe in `Binaries/Win64`. A
folder chosen by hand must be the game's main folder: it must hold `Engine`, the
profile's own project folder and the real exe, so the folder of another Unreal
game is refused. A pick inside the main folder, such as `Binaries/Win64`, is
walked up from.

A folder chosen by hand inside a Steam library or an Epic install still starts
through that store; otherwise **Launch game** starts the profile's `executable`
directly. An Epic install is matched by its folder here, so it works even when
`epicDisplayName` is wrong; the log then names the store's real name for the
profile. `saveFolder` names the save folder for save backups. Set it only when
the folder is known for sure: without it, the app offers no save backups.

Each build contains exactly one profile, chosen with the `GameProfile` build
property (default `phantom-blade-zero`). `mortal-shell-2` exists to test the
manager against a released Unreal Engine 5 game:

```powershell
dotnet publish src/UEManager.UI/UEManager.UI.csproj -c Release -p:GameProfile=mortal-shell-2 -o dist-ms2
```

## Data

The app is portable. Everything it owns lives in a `Data` folder next to
`UEModManager.exe`, created on the first start:

```text
UEModManager.exe
Data/
  settings.json                    folders chosen by hand, last seen game build, last announced app version,
                                   window place and size
  instance.lock                    held while the app runs
  logs/app-YYYYMMDD.log
  staging/                         unpacked downloads, emptied at every start
  games/<id>/mods.json             installed mods, files, checksums, mod sets
  games/<id>/library/<mod id>/     the manager's copy of each mod (.defaults: settings files as shipped)
  games/<id>/backups/              settings the player changed, saved before an update, uninstall or reset
  games/<id>/save-backups/         zips of the save folder, the newest 5
```

Outside it, the app writes only into the game folder: the files of enabled
mods (`~mods`, `LogicMods`, UE4SS's `Mods`) and UE4SS itself next to the
game's exe. If the app folder is not writable (for example under
`C:\Program Files`), the app shows an error and asks to move the folder.
`publish.ps1` keeps an existing `Data` folder in `dist/uemm`.

Command line:

```text
UEModManager.exe [files...] [--data-dir <folder>] [--catalog <url>] [--update-url <url>]
```

Files given on the command line (as by "Open with") open the install review.
`--data-dir` uses another data folder, which keeps test runs away from real data.
`--catalog` reads another openmods games list (see openmods page); only while
the catalog is switched on. `--update-url` reads another release list for the update check,
in GitHub's format; it may use http, for a test server. A restart (after a
self-update, or after Reset modding reset the settings) starts the app with
`--after-update`, so it waits up to 30 seconds for the old one to end instead of
handing over to it.

## Not yet supported

- Installing a UE4SS 2.x package (removing one works)
- Moving managed mods along when UE4SS 3.x is installed over a 2.x that was
  put in by hand: they stay in the old 2.x `Mods` folder, where 3.x does not
  load them, and still show as enabled. Remove UE4SS first, install 3.x, then
  enable the mods again.
- Load order: neither pak priority nor UE4SS's `mods.txt` order
- Taking over mods that were installed by hand

## Contributing

Contributions are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md). Everyone who
contributes is named in [CONTRIBUTORS.md](CONTRIBUTORS.md) and on the app's
About page.

## License

GNU General Public License, version 3 ([LICENSE](LICENSE)), with additional
terms under its section 7 ([NOTICE.md](NOTICE.md)): a work based on this program
keeps the credit to the original and its contributors, is marked as a modified
version with a name of its own, and gets no rights to the names "UE MM" and
"KintaroModding". The About page shows the license, the additional terms and
the contributors; all three are built into the exe. Both release files are
built from this source with the commands above.
