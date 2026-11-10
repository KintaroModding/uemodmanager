# UE MM

A mod manager for Unreal Engine games. The first and, for now, only supported
game is **Phantom Blade Zero**; the interface is themed for it. The executable
keeps the file name `UEModManager.exe`.

It keeps its own copy of every mod and records a SHA-256 checksum for every
file it places. A file it did not place is never overwritten or removed.

## Download

Get `uemm_<game>_<version>.zip` from the latest
[release](https://github.com/KintaroModding/uemodmanager/releases/latest). Unzip
it into a folder you can write to and start `UEModManager.exe`; it needs the
[.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0).
How to install and use it, for players: [openmods.md](openmods.md).

## Interface language

In **Settings → Language**, choose **English**, **简体中文**, **繁體中文** or **Tiếng Việt**. The interface
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

The changelog has one file per language too, `Localization/changelog.<code>.md`,
with the same `## <version> (<date>)` headings as `CHANGELOG.md` and the lines
translated. A release it does not have yet shows in English, so a translation
may come later; a test checks that each heading names a real release. The
**Changelog** link itself stays English in every language.

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
| Game files | a file below the download's `<Project>` folder, except in `Content/Paks` and `Binaries`; `PBZModLoader/mods` and a top-level `Content` count without that folder too | the same path in the game |
| Exe files | a `.dll`, `.addon64` or `.asi` file, with the files in its folder | the game's exe folder |

A mod of type Game files or Exe files has a blue shade in the list.

A game file is one the game keeps and reads itself, such as the keybinding
preset `ProjectPBZ/Saved/EnhancedInputUserSettings.sav` that some Phantom Blade
Zero controller mods bring. Such a download is installed as one more part, with
the path it has below the install folder. The game may have that file already:
the install review says so, the manager saves the game's own copy into the mod's
folder, and puts it back when the mod is disabled, updated or removed. The game
writes these files while it runs, so their content is never compared, and their
folders are never removed. When the game changed such a file while the mod was
on, as with new key bindings or a ReShade.ini, that version is copied into
`Data/games/<id>/backups/` before the mod's file leaves. The player's saves,
logs and crash reports (`Saved/SaveGames`, `Saved/Logs`, `Saved/Crashes`) and
`PBZModLoader/mods.ini` are never installed: a mod's copy would bring back an old
save when the mod leaves.
If the game's own copy cannot be put back, as when the game holds the file,
nothing is removed.

Exe files are the other way round: a proxy DLL such as `dxgi.dll`, a ReShade
add-on or an `.asi` loader has to be next to the game's executable when it
starts, so content packages cannot stand in for them. A download that keeps such
a loader loose (ReShade itself, RenoDX, OptiScaler, `version.dll` + `.asi`
loaders) is installed as its whole folder next to the exe, or from the paths it
carries when they name the exe folder (`platform/steam/dxgi.dll`). A file of the
same name that the game or another tool had there is saved into the mod's folder
and put back when the mod is disabled or removed, and the install review says so
first. A file that belongs to UE4SS's own installation is refused: overwriting it
would break UE4SS and every mod on it. Windows may load any DLL next to the
game's exe when the game starts, so the review ticks such a part only when it is
clearly a loader: a known proxy name (`dxgi`, `d3d9`/`11`/`12`, `dinput8`,
`version`, `winmm`, `winhttp`, `dsound`, `xinput1_3`/`1_4`, `opengl32`,
`nvngx`), a ReShade add-on or an `.asi`. Any other DLL, such as a tool in an
`extras` folder, shows unticked with a note, and the player decides.

UE4SS sits next to the game's exe, which most Unreal games keep in
`<Project>/Binaries/Win64` and Phantom Blade Zero in `platform/<store>` (see
[Game profiles](#game-profiles)). Its `Mods` folder there is `ue4ss/Mods` for
UE4SS 3.x and `Mods` for 2.x, detected from where `UE4SS.dll` is. A UE4SS mod
is deployed as its whole folder; when it has no `enabled.txt`, the manager adds
one, so UE4SS starts it without `mods.txt` being edited. Disabling removes the
folder again. Lua, DLL and Blueprint mods can be installed before UE4SS is,
with a warning.

A UE4SS mod may bring Lua libraries for UE4SS's `shared` folder, in a `shared`
folder next to its own (such as `Mods/shared/json/json.lua`); they go with the
mod. One copy serves every mod that brings the same file, and it goes when the
last of them is disabled. A library that was there before, such as one of
UE4SS's own, is used but never removed. Another version of a library that an
enabled mod uses is refused, since only one can be in the game.

A download that places a folder without code in UE4SS's Mods folder
(`ue4ss/Mods/<Name>/...`) installs it as it is, as a part of its own (type
"UE4SS files"), without an `enabled.txt`, since UE4SS has nothing to start
there. A Blueprint mod such as AutoSwitchWeapon ships such a folder with only a
`modinfo.ini`: the mod writes its `<Name>.config.json` there, which ModConfigUI
shows in the game's settings. Files the mod writes there stay when it is
disabled or uninstalled. Files loose in the Mods folder, such as `mods.txt`,
stay out: they are UE4SS's own.

A document stays out by its name and type: `license.txt` does, `license.json`
does not. A `.sig` goes with the package of the same name. The review warns about
any other file that stays out, as the mod may need it. Text, pictures, links and
install helpers beside a mod's files, outside the game's own folders, stay out
without a warning.

The wider rules for game files and for files beside a package take only what the
other rules left, so they add to what a download installs and never move a file: a
folder with a loader still goes next to the exe as a whole.

A download with several folders that hold the same files (options such as
`x1.5/`, `x2/`, `x10/`) offers them as one choice, sorted by number; the chosen
variant is recorded with the mod.

Below the parts, the install review shows where the ticked parts' files go, in
the same tree of folders as the mod details, and follows each tick. A settings
file says so, and a file that takes the place of one the game has says that the
game's file is kept. The download's other files are named below the tree ("Not
installed: …"), so a file that matters cannot pass as a readme.

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
  `Scripts/config` or `Scripts/settings` file (`.lua`, `.json`, `.ini`, `.cfg`,
  `.toml`, `.yaml` or `.txt`, such as `Scripts/config.json`), or a `.json`,
  `.ini`, `.cfg`, `.toml`, `.yaml` or
  `.txt` file that a download keeps in the same folder as its pak, whatever
  the folder and the file are called (such as a mod that reads its color from
  `~mods`, or an asset mod's `.txt` loose at the top of the zip). It goes into
  `~mods` or `LogicMods` with the pak. Files named like a readme, license or
  change log are no settings, and neither are other files beside the packages:
  they are not installed. Disabling keeps the player's
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

At every start the app reads `uemodmanager/manifest.json` of the
`KintaroModding/metadata` repo from the profile's `ue4ssManifestUrls`, in order:
`metadata.openmods.nexus` and `uemm-metadata.pages.dev` (Cloudflare Pages, which
deploys every push to the repo) and then GitHub itself. Each address gets 5
seconds; the first that has the game's entry counts, so a host that is blocked
or slow in a region, as GitHub in parts of China, does not stop the check:

```json
{ "ue4ss": { "<game id>": { "version": "v…", "url": "https://…/….zip", "sha256": "…",
  "mirror": "ue4ss/….zip",
  "files": { "ue4ss/UE4SS.dll": ["<sha256>"], "dwmapi.dll": ["<sha256>"], … } } } }
```

`url` is the release zip on GitHub. The optional `mirror` is a copy of the same
zip in the metadata repo, relative to the manifest, so it comes from the same
Cloudflare address; **Download and install** takes it first and `url` when it
fails, and checks the SHA-256 either way. Versions up to 1.5.1 ignore `mirror`,
so `url` must stay a full GitHub link.

When the entry's version differs from the recorded one (or none is recorded,
as for a UE4SS installed by hand), Home and the UE4SS page offer **Update to
v…**. A new UE4SS build therefore ships by editing the manifest, without a new
app build. Offline or failing, the check stays silent.

This is a maintainer-selected build for the game, not a search for the newest
upstream UE4SS release; there is no stable/nightly channel selector. The app
checks at startup but installs only after a user action. Close the game and
choose **Update to v…** on Home or the UE4SS page (or install a downloaded
archive). The existing update condition is a version-string difference, not
an ordering comparison: a manually installed or numerically newer build can
also be offered the manifest's version, potentially a downgrade. Review the
target version first. The download verifies SHA-256 only when the manifest
supplies it. This mod-page feature does not change that UE4SS behavior.

The optional `files` lists the DLLs of the official builds (`UE4SS.dll` and the
proxy, such as `dwmapi.dll`), by their place next to the game's exe, with the
SHA-256s each has in any official release. The DLLs make the build; scripts and
settings are the player's and the mods' to change, so the app compares only
DLLs, also when the list names other files. When an installed DLL that the list
names has none of them, Home shows a small orange **Unofficial UE4SS build** box; a click lists
those files and offers **Install the official build**, which replaces UE4SS's
files and keeps its settings and the mods. A DLL the player removed does not
count. The help report says whether the build is official. Older app versions
ignore `files`. `--ue4ss-manifest-url <url>` reads a test copy of the manifest.

### Recommended mod

The manifest's optional top-level `recommended` names one mod per game, so it can
come and go without a new app version, such as a fix that a later game patch
makes needless:

```json
{ "recommended": { "<game id>": { "repo": "owner/name", "name": "…", "folder": "…",
  "experimental": true, "text": { "en": "…", "zh-CN": "…" },
  "notes": { "en": "…", "zh-CN": "…" } } } }
```

The Mods page shows it in a card above the search box, apart from the player's
own mods: its text (English when the language has none), the label
**Recommended**, or **Recommended - Experimental** with `experimental`, and
**Download and install**, or **Update to …**. With `notes` (paragraphs apart by an
empty line) a **TECHNICAL NOTES** box next to the label opens them, with a link to
`repo` as the source code. Once installed, the card holds the mod's normal
row. Its name and version come from the newest release, read at every start:
first from `uemodmanager/mods/<repo name>/` in the metadata repo (a copy, in the
shape of the app's own copy, see Updates), on the host that answered the manifest;
then from `repo` on GitHub. A new release of the mod is on offer once its copy is
published, and a small blue **UPDATE** on the Mods entry of the sidebar shows it to a
player who has the mod installed. A missing UE4SS, or one
other than the manifest names, is installed first, from the same click: such a mod
may work only with that build. `folder` finds a copy installed by hand, so
it moves to the card too.

The optional top-level `recommended2` has the same shape and comes first. Apps
before 1.5.6 do not read it, so `recommended` stays as they know it. It adds
`installedName` (a shorter name for the mod's row once it is installed), `fileSuffix`
(the end of the zip's name, in a release with several zips), `ue4ss`
(`false` for a mod that needs no UE4SS, so its button leaves UE4SS alone) and `extra`
(a second mod the card offers below it, with `retireText` and `retireButton` for a
line and a button that uninstall it while the main mod is installed too). The two
mods do not block each other. `thanks` (`box`, `text`, and `links`, https only) adds a
green box that opens a thank-you, for the people a mod builds on.

A download with an `alternative-entries` folder holds other entry DLLs of one loader,
each under its own name. Only one may be in the game: the review shows them as one
choice and leaves them unticked, and a click on the card does not offer them. A player
who took one keeps it through an update.

A download with both `<Project>/Content/JavaScript/PBZModsMenu` and
`<Project>/PBZModLoader/mods` holds one script mod twice, a copy for each loader. It is
one choice too, and nothing is ticked until the player makes it: with both copies in the
game, one is not used, or the mod loads twice and most likely crashes the game. A later
install keeps the copy that is in the game, and the other copy takes its place. With
one of the two loaders in the game, the copy for it is ticked at first.

Mods Menu 0.9.0 and PBZModLoader 2.1.0 or newer run together, so both install like any
other mod. An older version of either causes problems with the other: the review warns
when it knows such a version, from the mod list or the download's name, and so does the
Mods page while both are in the game. Mods Menu uses `hid.dll`, so the review stops
PBZModLoader's hid entry beside it, and Mods Menu while PBZModLoader is on that entry. With
both loaders in the game, a zip with a copy for each ticks the copy for PBZModLoader.
Either loader counts when it is in the mod list, even turned off, and when it was put in
by hand.

A script mod below one of those two loaders' folders waits for its loader: the review
stops it while that loader is missing. For a zip with a copy for each loader and neither
loader in the game, the copy for PBZModLoader is ticked, so the review says what to do. A
copy of PBZModLoader put in by hand counts by its DLL next to the exe, under one of its entry
names and with the text `[PBZMOD]` in it. Its log and its mods folder do not count, as they
stay when it is removed. A loader before 2.0 counts by its start file
`<Project>/Content/JavaScript/PBZModLoader/bootstrap.js` or by a DLL that names that path.

A PBZModLoader installed by hand gives way to the manager's copy, which is one DLL: after
the player agrees, its entry DLL and the old layout that the loader's manifest lists
(`Content/JavaScript/PBZModLoader`, and `compiler`, `evidence` and `TOOLCHAIN-LICENSE.txt` in
`PBZModLoader`) move to a backup folder, all or none. `PBZModLoader/mods` and `mods.ini` stay.
A mod with only a `main.ts` no longer runs, as the compiler was the old loader's, and the
question names such mods. These are the only files the manager moves that it did not place.
The review of a new mod with only a `main.ts` warns too.

Mod sets leave it as it is: a set does not hold it, and a set saved before it
came does not turn it off. **Test without mods** turns it off like any other mod.
When the manifest no longer names it, it becomes one of the player's own mods.
Offline, the installed mod stays in the card.

### UE4SS page

The UE4SS page holds everything about UE4SS: its status and folders, how many
installed mods need it, install, update and removal, and the **log**. The log
is the end of `UE4SS.log` (the last 2000 lines), which UE4SS writes anew at
every game start. Lines that speak of an error are red, warnings orange, and
the mod folders an error names are listed. The page reads the log each time it
opens; **Refresh**, **Copy** and **Open log file** are next to it. **Clear**
deletes `UE4SS.log`, so the old lines are gone; UE4SS writes a new one at the
next game start. Like every change in the game folder, it waits until the game
is closed. The error marks in the Mods list go with the log, and until the next
game start a help report holds no log and the page shows no UE4SS version from it. The search box
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

A click on a mod's name, or on the list button of its row, on the Mods page
shows its details: type, version, variant, when and from which download it was
installed, its size, and every file it placed, in the tree of its folders in the
game folder (a folder that holds only one folder shares its line, as
`platform\steam\ue4ss\Mods`). Each file shows its size and state: in the game, a
settings file the player may change, changed outside the manager, or missing;
the full path is its tooltip. The state is checked by size, as the list does,
not by checksum. A disabled mod lists its files as not in the game.

The blue **Edit** box next to the name changes the mod's name and version, as the install
review sets them (Enter saves, Esc cancels). Only the mod list changes: the
library keeps each mod under its id, and the game's files keep their names, so
it also works while the game runs.

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

## Tags

A mod's tags are words of the player's own, apart by commas, such as "Combat, UI"
(`tags` in `mods.json`). The install review has a field for them, and **Add tag** is
a button in a mod's row and in its details; **Edit** in the details takes them too.
Under the box, tags to pick: the player's own, the most used first, then common
ones in the app's language. They narrow as the player types, **Tab** takes the
first one, and a click adds one; a tag already in the box is not offered again.
Once a mod has a tag, buttons above the list show **All tags** and each tag with
its count, and a click shows only the mods with that tag, one tag at a time. A row shows
its tags, and the search finds them. An update keeps a mod's tags. They change no
file of the mod. The mods on the card stand apart from the list and have no tags.

## Installed mod update checks

Opening **Mods**, installing/updating a mod while on that page, or pressing
**Refresh / F5** checks the public Nexus Mods pages of rows with an **Open mod
page** link. Checks run in the background (at most three at once); each page's
result, including an unavailable page, is cached in memory for 10 minutes.
Checks work even when the openmods catalog is switched off.

A newer, comparable page version shows **Update available: &lt;version&gt;** below
the mod name. Use **Open mod page** to review the release, download the right
file/variant, and install it through the existing review dialog. Nothing is
downloaded or replaced automatically. This compares the mod page's version,
not each optional file or variant; it cannot guarantee that your chosen file
has an update. Equal or older page versions show **No newer page version**.

This is a best-effort, read-only check without a Nexus API key or browser
cookies. Login-only pages, Cloudflare challenges, network failures, changed
page markup, missing local versions, non-numeric versions, and unsupported
sites show **Could not check for updates**, never a claim that the mod is up to
date. Only dotted numeric versions (with an optional v prefix) are compared.
Requests go directly to Nexus Mods, revealing the game/mod page IDs to that
site; no local files, paths or credentials are uploaded. Redirects are not
followed. Rows without a link, and the recommended mods on their separate
card, do not use this checker.

## Sorting and showing the list

A click on a column header of the Mods page sorts the list by it, and a second click
reverses the order: **Mod** A to Z, **Installed** newest first, **Status** enabled
mods first, **Type** by its name, and **Integrity** with problems first (files
changed, then missing, then in place, then off). Mods that are equal there go by
their name. Left of the tags, **All**, **Enabled** and **Disabled** with their counts
choose which mods the list shows; **Enabled** hides the disabled ones. The state,
a tag and the search work together. The sort and the state stay after a restart.

Below the list, a box counts the files in `~mods` that no mod in the list placed,
such as a mod put in by hand. **Show files** lists them in the tree of their
folders, with their sizes and **Open folder**; the manager never changes or removes
them.

## Same file names

Some authors give different mods the same file names, such as `Skin_P.pak` in two
styles of one outfit. A new version of a mod has the same names too, and only the
player can tell the two apart. So when a download by hand has a file of the same name
as a mod's in `~mods`, the install review asks. **Update** that mod is ticked at first,
as it is its usual meaning. **Keep both** installs the download as another mod, with
its files in a folder of its own below `~mods`, named after the mod, as the game also
loads paks from there. Each mod then has its on/off switch, and an update of either
keeps its place. The review warns that two mods may still change the same things in
the game: only one style or version shows then, and only the author can make both
work together, with one combined mod.

## Donating

**Donate** above the Discord button opens a thank-you window with the PayPal link. A
donation is fully optional, and every feature works the same without one. The small X on the
button asks first, then removes it from the sidebar for good (`hideDonateButton` in
`settings.json`). The About page keeps the button at its top.

## Keyboard

| Key | Does |
| --- | --- |
| Ctrl+F | goes to the search box: the log's on the UE4SS page, the mod search from every other page |
| F5 | checks the mods' files in the game again; on the UE4SS page it also reads the log again |
| Ctrl+S | saves in the settings editor |
| Esc | clears a search box; closes a dialog, or answers its question with the safe choice |

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

Before a release, add its section at the top of `CHANGELOG.md`
(`## <version> (<yyyy-mm-dd>)`, then one short `- ` line per change): a test
checks that the newest section is the app's version, so the publish stops
without it.

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

At every start the app reads the newest release of
`KintaroModding/uemodmanager` and shows the result below the version, at the
bottom of the sidebar. It reads a copy first: the release steps put GitHub's
`releases/latest` answer and the release zip into the `KintaroModding/metadata`
repo (`uemodmanager/app/`), which Cloudflare Pages serves, as for the UE4SS
manifest. GitHub's API is the last of the addresses, 5 seconds each. The
copy's zip link is relative to it, and the copy also names the same zip on
GitHub, which **Update now** downloads when the copy fails:

- **Up to date** (green): the newest release is not newer than this version.
  Its **Changelog** link shows what changed in each version, newest first, one
  short line per change, from `CHANGELOG.md` built into the exe (so it works
  offline too).
- **Update available** (orange): a click opens the update window.
- **Update check failed** (grey): offline, no release yet, or GitHub's limit
  of 60 requests per hour; the tooltip says which. Nothing else happens.

A new version is announced once, without a sound: a banner in the middle of
the window for 3 seconds, then the orange box flashes once. While a dialog or
the busy screen is open, the banner waits. The announced version is kept in
`settings.json` (`announcedUpdate`), so the next start only shows the orange box.

The update window shows the release notes as GitHub does, for what release
notes use: lists, **bold**, `code` and links. A link opens in the browser only
when it is an http or https address.

The app takes `uemm_<game>_…zip` from the release. The update window offers:

1. **Update now**: downloads that zip, checks its size and SHA-256 (from
   GitHub's digest, which the copy keeps), checks that the exe inside carries the announced version,
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
