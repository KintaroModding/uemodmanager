# UE MM

A mod manager for Unreal Engine games: **Black Myth: Wukong** and **Phantom
Blade Zero**, and **Stellar Blade: Blood Rain** as a preview of its look until
the game is out. One app serves every game: the player picks the game, and the
interface takes that game's look. The executable keeps the file name
`UEModManager.exe`.

It keeps its own copy of every mod and records a SHA-256 checksum for every
file it places. A file it did not place is never overwritten or removed.

## Download

Get `uemm_<version>.zip`, one zip for every game, from the latest
[release](https://github.com/KintaroModding/uemodmanager/releases/latest). Unzip
it into a folder you can write to and start `UEModManager.exe`; it needs the
[.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0).
How to install and use it, for players: [openmods.md](openmods.md).

## Interface language

In **Settings → Language**, choose **English**, **简体中文**, **繁體中文**, **Tiếng Việt**, **日本語** or **한국어**. The interface
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

## Games

At the first start the app shows **Choose your game**: one card per game, with
where the game is on this PC (Steam, Epic, a folder chosen by hand, or Game not
found) and how many mods it has. A click saves the choice (`game` in
`settings.json`) and starts the app again with that game. **Switch game** in the
title bar opens the same picker at any time, and **Settings → Ask for the game
at every start** shows it at each start. Each game keeps its own mods and
backups in `Data/games/<id>/`, and has its own look: colours, art and card
(see `src/UEManager.UI/Theme/README.md`).

A game that is not out yet, such as Stellar Blade: Blood Rain, has a **Peek**
label on its card: it can be chosen to see its look, and Home says that mod
support comes with the game.

**Missing your game? Tell us on Discord** opens **Scan a game folder…**. The app
reads the folder's tree with file sizes and sums it up: the Unreal project
folder, the paks, the likely game exe with its version, signs of Steam or Epic,
UE4SS and anti-cheat. Nothing is sent: the report opens in its own view to copy,
save as `.txt` or share on Discord, so the game can be added. It hides the
Windows user name, Steam ids and other long ids in folder names.

## Tray icon

An icon next to the clock, in the game's colours (**Settings → Tray icon**, on
until the player turns it off): a click shows or minimizes the window, a
right-click opens a menu to switch the game, launch it, open the app or exit.
**Minimize to tray** hides the window there instead of on the taskbar, and
**Close button minimizes to the tray** does the same for the X; it needs both
settings on.

## Supported mod types

| Type | Recognised by | Deployed to |
| --- | --- | --- |
| PAK | `.pak`, with `.utoc` + `.ucas` (IoStore) | `<Project>/Content/Paks/~mods` |
| Blueprint | package files below a `LogicMods` folder | `<Project>/Content/Paks/LogicMods` |
| UE4SS Lua / DLL | a folder with `Scripts/main.lua` or `dlls/*.dll` | UE4SS's `Mods` folder |
| PBZModLoader (Phantom Blade Zero) | files below `<Project>/PBZModLoader/mods`; a top-level `PBZModLoader/mods` counts without that folder too | the same path in the game |
| Game files | a file below the download's `<Project>` folder, except in `Content/Paks`, `Binaries` and `PBZModLoader/mods`; a top-level `Content` counts without that folder too | the same path in the game |
| Exe files | a `.dll`, `.addon64` or `.asi` file, with the files in its folder | the game's exe folder |
| C# Loader (Black Myth: Wukong) | files in `CSharpLoader/Mods`, at the download's top or below `<Project>/Binaries/Win64` | `CSharpLoader/Mods` next to the game's exe |

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
  `~mods` or `LogicMods` with the pak. A PBZModLoader mod's `.json`, `.ini`,
  `.cfg`, `.toml`, `.yaml` or `.txt` files in its folder below
  `<Project>/PBZModLoader/mods` are settings too (such as Animation Cancel's
  `phantombladezero_animationcancel.txt` next to its `main.js`), as are the
  data files of a C# Loader mod. Files named like a readme, license or
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

The openmods page (the catalog: see Catalog switch) lists the game's mods from
[openmods](https://openmods.nexus): the game is found in openmods' games list
by its Steam app id, and its own list is read from there. The lists are read
when the page opens, at most every 10 minutes (openmods' own refresh interval),
and only format 1 is accepted; unknown fields are ignored, and only https links
are used.

- The box **About openmods.nexus ecosystem** next to the title opens a view over
  the whole window: openmods is free for players, open to every author with a
  GitHub account and an open-source mod, has no moderation of its listings, and
  has no premium downloads, advertisements or data collection. The box **Mod type
  info** in the row below explains the type boxes and each type.
- **Full view** (outlined in orange, after Mod type info) shows the page as a
  grid of cards in place of the menu and the page, at the window's size: cover,
  the name with the type below it, author, tags, version, date, license,
  description and the same actions, with the same type filter, search, sort
  and switch. It leaves the window's size alone. **Exit full view** or Esc goes
  back. It is off at every start.
- The **Mods | Tools** switch is two plain buttons: the app shows the open side on
  them, and a click runs a command. Radio buttons bound both ways to the two
  sides could write each other back without end (a stack overflow at the start
  on one PC).

- **Install** downloads the release's only zip from GitHub, checks its size
  and, when the list gives one, its SHA-256 (`files[].sha256`), and installs
  it. The installed mod records the catalog id (`owner/repo` of the source).
- A download that needs a look opens the install review instead: several
  parts, a warning (such as UE4SS missing), a mod installed by hand that it
  would replace, or settings the player changed.
- When the list names a newer version than the installed one, the card offers
  **Update to …**, and the mod's row on the Mods page offers **Install update**
  (see Mod pages and updates). An update replaces the mod with the same catalog
  id, even when the new version renamed its files.
- A catalog mod never takes over files of another enabled mod: the install
  stops and names that mod.
- The UE4SS entry opens the manager's own UE4SS install.
- A search field filters by name, description, author, tag and type. The list sorts
  by name or by the date of the latest release (newest first by default); the
  choice is kept in `settings.json`.
- A switch, **Mods (n) | Tools (n)**, shows either the mods or those tagged
  `tools` (such as UE4SS and the manager itself); the page opens on the mods at
  every start, and the switch shows only when the list has tools. When the search
  finds nothing on one side but something on the other, or the game lists only
  tools, the page says so and offers to switch.
- The type filter, **Type: All (n) | UE4SS Lua (n) | …**, after Full view (in
  the full view, below the title), shows the cards of one type at a time: All,
  then each type on the open side of the switch, in the order of Mod type info,
  with its number of cards. A mod with UE4SS scripts and a DLL is under both
  UE4SS Lua and UE4SS DLL; a card without a known type only under All. Every
  start and each side of the switch begin on All, and the filter shows only
  when a type would leave out a card. When the search finds nothing of the type
  but cards of other types, the page says so and offers **Show all types**
  before the other side. Plain buttons, as in the switch.
- Each card shows the mod's type in the words of the Mods page (UE4SS Lua,
  UE4SS DLL, PAK, Blueprint, UE4SS files, PBZModLoader, Game files,
  Exe files) or
  Other. The app's own scanner reads the paths in the release zip that openmods
  lists (`files[].entries`) as an install would, with the game's project and exe
  folders, and the type is that of the parts a one-click install takes, so the
  Mods page shows the same type after the install. Other: the install would
  place nothing, such as a tool's exe or a .usmap file. Only the single zip that
  **Install** uses counts; without its full list of paths (not read yet,
  `entriesTruncated`, several zips) the card shows no type, and UE4SS itself has
  none. The paths are only read, never used for a file; paths that would leave
  the download are left out.
  The type stands in front of the name; every type box on the page is as wide
  as the widest, so all names start at the same place.

`--catalog <url>` reads another games list, such as a test copy; the page then
says that it is not the real one.

Network accesses: the UE4SS manifest and the update check at start, the mod
update check at start (unless turned off) and on its button (see
[Mod pages and updates](#mod-pages-and-updates)), the openmods lists and cover
pictures when the openmods page opens (only while the catalog is switched on),
and the downloads the user starts. Pictures are kept in memory only.

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
the install folder, so the folder is never guessed. Without it, the **Backups**
page is greyed out and says that it comes in the next update. Phantom Blade
Zero keeps its saves in the install folder, in `ProjectPBZ/Saved/SaveGames`,
with a folder per store and player inside. The backup at launch is off by
default.

A broken mod can break a save. Save backups have a page of their own in the
menu, **Backups**, below UE4SS. When the player turns it on there, **Launch game** first zips
the save folder into `Data/games/<id>/save-backups/`; **Back up now** does the
same at any time, with a note when one is typed next to it. Without saves yet
(the folder is empty or not made yet) the game starts with a note; when the
backup fails, the app asks whether to start the game anyway. Starting the game
from Steam, Epic or a shortcut makes no backup.

The page shows the backups in two columns:

- **Normal** (left): the app keeps the newest, as many as the slider says: 2 to
  20 in steps of 2, 6 by default (one step above the 5 of versions up to 2.0.0,
  so an update removes none). A number in `settings.json` off the steps rounds
  up to the next one. Lowering the number removes older backups only after the
  next successful backup.
- **Personal** (right): backups the player keeps for good. The pin on a normal
  backup moves it here, into `save-backups/personal/`; the app never removes
  these, and they do not count towards the number. The other pin moves one back.

Each backup can carry a note of the player's, such as "Before the final boss",
saved on Enter or when the field loses the focus, in `save-backups/notes.json`
by the backup's path; it follows a backup that moves. **Delete** asks first and
leaves the current saves alone.

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

## Launch options

**Launch game** starts the game through Steam or Epic when the app found it
there, so DRM and overlays work as usual, and otherwise its exe. The gear on the
right of the button opens **Launch options**: the arguments the game starts
with. A profile can recommend some (`launchArguments`): Black Myth: Wukong loads
pak mods only with `-fileopenlog`, so it starts with it until the player changes
it. With arguments, the app starts the game's own exe, so launch options set in
Steam or Epic do not apply; without, it goes through the store. The window says
so. The player's arguments are saved per game (`launchArguments` in
`settings.json`); arguments equal to the recommendation are not saved, so a later
change of the recommendation reaches the player.

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

## Mod pages and updates

A mod whose download kept the mod site's file name, such as
`Travel Anywhere 61 1.0.1 2026-10-29T13-42Z LbtEo64mS.zip`, has the mod's number
in it, so its row and its details offer **Open mod page**. Any other mod shows
*No mod page*; the recommended mods on the card show neither.

For those mods the app checks for updates: at each start (Settings → Mod updates
turns this off) and with the button in the mod list's header, which then waits
30 seconds. One request to the mod site's public API asks for the version on the
page of all of them at once. It sends only the game's number on the site and the
mods' numbers, needs no account, and downloads nothing. The page's version is
compared with the mod's version in the list, which comes from the download's
name:

- **Update available** (blue): the page has a newer version. A click opens the
  page's files, where the player downloads it.
- **Up to date** (green): the page has the same version, or an older one.
- Nothing: not checked yet, the check failed (the log says why), or the versions
  cannot be compared, such as `1.2-beta` against `1.2`.

Text after a version's numbers is left out, so `0.5.2.withHP` on the page is
newer than `0.5.1` in the list, and `1.3` is newer than `1.2-beta`. With the same
numbers, a single letter right after them counts in the alphabet's order: `1.2b`
is newer than `1.2a`, and `1.2a` is newer than `1.2`. Other text with the same
numbers, or a version without a number at the start, shows **Up to date** only
when both are the exact same text.

The page's version is the mod's main version. A page with several files, such
as styles or optional files, has one version for all of them, so only the mod's
author keeps it right. After an update in the manager, the box follows at once
from the version already known, without asking the site again. A newer version
also shows **UPDATE** on the Mods entry.

A mod installed from the openmods page (see openmods page) links to its page on
openmods instead; the link reads **openmods.nexus**, in orange (in the row and
in the details), so it stands apart from a mod site's page. The app stores that page with the mod, as the openmods list
gives it, since a project listed for another author has a page of its own. Its
box compares the mod's version with the newest release in the openmods list, by
the openmods page's rule, so both pages agree. The same check reads that list
(at most every 10 minutes, as openmods asks); so does a visit of the openmods
page. A newer release shows **Install update** (blue): a click downloads and
installs it, as the openmods page's **Update to …** does. Not tracked works the
same for these mods.

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
    Games/Profiles/*.json Game profiles: store ids and folder layout. All built into the assembly.
    Catalog/              Reading the openmods lists (see Catalog switch).
  UEManager.UI/           WPF application (net8.0-windows). Builds UEModManager.exe.
    Catalog/              The openmods page (see Catalog switch).
    Theme/                Palette, control styles, and each game's colours (Games/) and art
                          (Art/<game id>/). Built into the exe (see Theme/README.md).
  UEManager.Cli/          The command line tool (net8.0), a library built into UEModManager.exe;
                          see Command line tool.
  UEManager.Core.Tests/   xUnit tests for Core. Covers every operation that touches the game folder.
  UEManager.UI.Tests/     xUnit tests for the UI: the language files against the source, and a
                          change of language in the open window.
  UEManager.Cli.Tests/    xUnit tests for the command line tool, against a fake install.
```

## Command line tool

`UEModManager.exe --cli <command>` (`src/UEManager.Cli`, by CooperZhuang) drives
the same Core without a window, for scripts, tests and AI agents. It is built
into the app's exe: the release stays one exe, as the self-update takes exactly
one, and normal starts of the app do not change.

```text
UEModManager.exe --cli status | list | verify
UEModManager.exe --cli plan <archive|folder>
UEModManager.exe --cli install <archive|folder> --yes
UEModManager.exe --cli enable <mod> | disable <mod>
UEModManager.exe --cli uninstall <mod> --yes [--remove-leftovers]
UEModManager.exe --cli backup list | create | restore | personal | note | delete
UEModManager.exe --cli ue4ss status [--check]
UEModManager.exe --cli ue4ss install [--package <file>] [--ue4ss-version <tag>]
UEModManager.exe --cli ue4ss update
UEModManager.exe --cli ue4ss remove --yes
```

- `--json` gives one JSON object with stable fields; `--data-dir`, `--game` and
  `--game-root` pick the data folder, the game and its install folder.
- `backup` is the Save backups page without a window: `create` zips the save
  folder now (`--note`, `--keep`), `restore`, `personal`, `note` and `delete`
  work on the backups the app shows, and `list` prints them. The save folder is
  the app's (settings.json, then the game profile); `--save-folder` overrides it
  for `create` only, as a restore empties the folder it fills. `--keep` takes
  the app's values, 2 to 20 in steps of 2. `restore` refuses while the game
  runs (`SaveBackups.CanRestoreNow`, which the app uses too).
- `install`, `uninstall` and `ue4ss remove` without `--yes` only show what they
  would do, with exit code 2; so do `backup restore` and `backup delete`. Exit
  code 1 is an error, on stderr.
- The data folder is `--data-dir`, else `UEMM_DATA_DIR`, else the `Data` folder
  beside `UEModManager.exe`, where the app keeps it.
- `plan`, `install`, `enable`, `disable`, `uninstall`, `ue4ss install`,
  `ue4ss update` and `ue4ss remove` refuse while the app is open with the same
  data folder: both take the data folder's lock (`DataFolderLock`), as two
  programs would overwrite each other's mod list. The `backup` commands that
  change the data folder take that lock too. `ue4ss status` reads only, so it
  works beside the open app.
- Which parts of a download it takes is the install review's first selection
  (`ModManager.FirstSelection`), so the tool and the app agree.
- `--part <index|name>` takes only the named parts of a download: by the plan's
  index from 1, by `folder/name`, or by name or variant (such as `x2`) when only
  one part has it. It can be repeated; `--all-parts` takes every part instead,
  and the two cannot be combined. Two parts of one choice (two variants, or a
  script mod's copies for both loaders) are a problem in the plan, as only one
  can be in the game.
- When an installed mod has the download's file names or mod page, the tool
  takes the install review's first choice (`ModManager.PlacesFor`): the update,
  or for a download that only comes from the same mod page, keeping both.
  `--keep-both` keeps the installed mod in any case: the download becomes
  another mod, in a folder of its own below `~mods`. The plan shows this as
  `keepBoth` and the folder as `ownFolder`, with no `replaces`. When the
  download must still take the mod's place (another choice of the same mod, or
  files outside `~mods`), `--keep-both` stops with an error.
- `--staging <folder>` unpacks the download there instead of the data folder's
  `staging` folder, for example on a drive with more room. It must be outside
  the game folder.
- `ue4ss status` reads the data folder's `ue4ss.json` record and UE4SS's own
  files; `--check` also reads the manifest, online, to say whether a newer
  release exists.
- `ue4ss install` takes the release the game profile's manifest names, from
  the first address that answers, else the profile's fixed one
  (`Ue4ssManifest.FixedRelease`), and downloads, checks and installs it with
  the Core's `ModManager.DownloadAndInstallUe4ssAsync`, as the app's one-click
  install does, below the data folder's staging folder (or `--staging
  <folder>`, outside the game). `--package` takes a local zip, 7z file or
  folder instead, which also works offline, and `--ue4ss-version` names the
  version recorded for it. Without `--package`, `--ue4ss-version` must be the
  release the manifest names. An install over an installed UE4SS is an update.
  With C++ mods installed it warns as the app does: each is made for one UE4SS
  build.
- `ue4ss update` installs the manifest's release when it differs from the
  installed one, but never an older one (`Ue4ssManifest.IsOlder`): after a
  newer build from `--package`, a script's update must not take it away. The
  manifest may go back on purpose, so `ue4ss install` still takes its release.
  Otherwise it says so and exits 0 without downloading.
- `ue4ss remove` without `--yes` lists what it would remove
  (`ModManager.PreviewUe4ssRemoval`, the Core's own list): dwmapi.dll and the
  whole ue4ss folder, with UE4SS's settings and every mod inside it, also mods
  installed by hand, and the enabled mods it would disable, which stay in the
  library. With `--yes` it removes UE4SS as the app does. A failed manifest
  read, download or checksum is an error with exit code 1, and no temporary
  download is left behind.
- `--ue4ss-manifest-url <url>` reads a test copy of the UE4SS manifest, as the
  app's own option of that name does.
- `uninstall` keeps the files a mod wrote itself, such as its logs, and lists them: they are not in the
  mod's record, so its record cannot remove them with it. `--remove-leftovers` moves them into the
  manager's backups first, below `leftovers`, so the player can take them out again.
- `UEModManager.exe` is a window program, so a terminal does not wait for it.
  Pipe the output (`... --cli list | more`, in PowerShell `| Out-String`), use
  `start /wait` in cmd or `Start-Process -Wait` in PowerShell. Scripts that wait
  for the process and read its output get the output and the exit code.

## Build

Requires the .NET 10 SDK (pinned in `global.json`). The output targets .NET 8.

```powershell
dotnet build src/UEManager.slnx
dotnet test src/UEManager.slnx
```

## Catalog switch

The catalog (the openmods page) is always built in. `Catalog` in
`src/Directory.Build.props` switches it on; it is `true`. With `false` the app
shows no openmods page, reads no openmods list and takes no `--catalog` option.
The rest of the app reaches the catalog only through partial methods
(`CreateCatalog`, `OnModsChanged`, `ReadCatalogArgument`,
`AddCatalogResources`), which do nothing while it is off; through the catalog's
part of the main view model (`MainViewModel.Catalog.cs`), which gives the Mods
page the page and update box of mods installed from openmods and knows no list
while it is off; and through five resource keys (`CatalogNavItem`,
`CatalogPage`, `CatalogFullView`, `CatalogAbout`, `CatalogTypesInfo`) that only
the catalog fills.

Every publish checks the exe it made: it must hold every page of the app (each
XAML file, compiled). A build can, rarely, go wrong without an error; such an
exe is deleted and the publish fails with "came out broken", naming the missing
pages. Publish it again.

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
dist/release/uemm_<version>.zip
dist/release/uemm_phantom-blade-zero_<version>.zip
```

The exe is a single file, framework dependent (needs the .NET 8 Desktop
Runtime), and passed the publish check (see Catalog switch). The same exe serves
every game. The second zip is a byte copy of the first under the name that apps
up to 1.7.2 look for on GitHub. Attach both to each release until GitHub's
download count of the old-named copy stays near zero; then `-NoOldName` leaves
it out.

Game profiles and theme art are built in, and so are the license texts of the
bundled libraries (`src/UEManager.UI/Licenses`), shown on the About page. Update
those texts when a library is added or upgraded.

To publish a release on GitHub: tag it `v<version>` (as in
`src/Directory.Build.props`) and attach the zips from `dist/release`. The update
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

The app takes `uemm_<version>.zip` from the release, by its whole name. The
copy on Cloudflare lists that file twice: by its own name, and as
`uemm_phantom-blade-zero_<version>.zip`, the name apps up to 1.7.2 look for; both
entries point to the same file, with the same SHA-256. The update window offers:

1. **Update now**: downloads that zip, checks its size and SHA-256 (from
   GitHub's digest, which the copy keeps), checks that the exe inside carries the announced version,
   then renames the running exe to `<name>.old`, puts the new one in its place
   and starts it with the same options. Windows refuses to overwrite a running
   exe but lets it be renamed. The next start deletes the `.old` file. If any
   step fails, the old exe stays in place.
2. **Open release page**: to download the zip and replace the exe by hand.

## Game profiles

A game profile, `src/UEManager.Core/Games/Profiles/<game id>.json`, tells the
manager where a game keeps its files. Every profile is built into the exe. Paths
are relative to the install folder, and `{Project}` stands for the Unreal project
folder. Phantom Blade Zero's:

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

A profile also holds the game's name and `motto` (the line above it on Home,
"Ready to mod" by default), `launchArguments` (see Launch options), the UE4SS
download and manifest addresses, the mod site's addresses, and three flags:
`comingSoon` (a game not out yet: a Peek card and a note on Home),
`experimental` (a tag on its card and on Home while its layout is not confirmed)
and `hidden` (no card in the picker or the tray menu, as Mortal Shell II for
now). `--game <id>` starts the app with any profile, a hidden one too, for test
runs; it does not change the saved choice.

## Data

The app is portable. Everything it owns lives in a `Data` folder next to
`UEModManager.exe`, created on the first start:

```text
UEModManager.exe
Data/
  settings.json                    the chosen game, folders chosen by hand, launch arguments, last seen game
                                   build, last announced app version, tray settings, window place and size
  instance.lock                    held while the app runs
  logs/app-YYYYMMDD.log
  staging/                         unpacked downloads, emptied at every start
  games/<id>/mods.json             installed mods, files, checksums, mod sets
  games/<id>/library/<mod id>/     the manager's copy of each mod (.defaults: settings files as shipped)
  games/<id>/backups/              settings the player changed, saved before an update, uninstall or reset
  games/<id>/save-backups/         zips of the save folder, the newest 6 (2 to 20, see Save backups),
                                   personal/ with the ones the player keeps, notes.json with the notes
```

Outside it, the app writes only into the game folder: the files of enabled
mods (`~mods`, `LogicMods`, UE4SS's `Mods`) and UE4SS itself next to the
game's exe. If the app folder is not writable (for example under
`C:\Program Files`), the app shows an error and asks to move the folder.
`publish.ps1` keeps an existing `Data` folder in `dist/uemm`.

Command line:

```text
UEModManager.exe [files...] [--data-dir <folder>] [--game <id>] [--catalog <url>] [--update-url <url>]
                 [--ue4ss-manifest-url <url>]
```

Files given on the command line (as by "Open with") open the install review.
`--data-dir` uses another data folder, which keeps test runs away from real data.
`--game` starts with that game (see Game profiles).
`--catalog` reads another openmods games list (see openmods page); only while
the catalog is switched on. `--update-url` reads another release list for the update check,
in GitHub's format; it may use http, for a test server. `--ue4ss-manifest-url`
reads another UE4SS manifest, also over http, for a test server. A restart (after a
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