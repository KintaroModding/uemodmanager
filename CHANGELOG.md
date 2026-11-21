# Changelog

What changed in each release of UE MM, newest first: one short line per change.
The app shows this list (the **Changelog** link in the sidebar's update box), so
every release adds its section at the top, as `## <version> (<yyyy-mm-dd>)`.

## 2.5.0 (2026-11-21)
- Mods of one author that bring the same base files can be installed together: the install review offers Keep both, and share the files that are the same in both
- The install review: CHOICES EXPLAINED says what Update and the two ways to keep both are for, and a choice that cannot work for the download is no longer shown

## 2.4.1 (2026-11-20)
- The open window no longer keeps the graphics card busy while nothing happens: a hidden busy spinner kept it redrawing
- Settings ▸ Interface language: Smooth scrolling can be turned off

## 2.4.0 (2026-11-19)
- Settings ▸ Interface language: choose the font and the interface scale (85 to 140 %); titles keep the game's own font (thanks, HumanMus1c)

## 2.3.2 (2026-11-16)
- Reset modding opens over the whole window with a live log of every file and folder it touches; a file in use that stops it is named
- Wider scroll bars and smooth wheel scrolling; a page's scroll bar floats over its margin, so the page no longer shifts
- The openmods lists load faster: the app asks for compressed answers
- openmods: the picture viewer shows every picture in the same 16:9 frame, with black bars where a picture's shape differs

## 2.3.1 (2026-11-15)
- Stellar Blade, experimental: its own look, UE4SS with one click (our build for the game) and save backups
- Mortal Shell II and WUCHANG: Fallen Feathers are no longer experimental

## 2.3.0 (2026-11-15)
- openmods: click a card's cover to see the mod's cover and screenshots; a box on the cover shows how many images there are
- openmods: the pictures are kept in the Data folder, so the page no longer downloads every cover at each start; Settings ▸ Clear picture cache empties it
- openmods: a Changelog button on each card shows what changed in each version, from the mod's releases on GitHub
- openmods: each card shows how often the mod was downloaded, and the list sorts by downloads too
- openmods: tidier cards, and the full view's cards use the whole width

## 2.2.1 (2026-11-15)
- WUCHANG: Fallen Feathers, experimental: its own look, UE4SS with one click, save backups, and on the Mods page bitfix, which its pak mods need
- Choose your game: See all games shows every game as cards over the whole window, Mortal Shell II (experimental) among them
- The update check of mods with a mod page compares each mod with its own file, so an optional file no longer shows a false update (thanks, CooperZhuang, and HumanMus1c for the idea)
- Uninstalling a UE4SS mod names the files it wrote into its own folder, such as logs, and can move them into the backups (thanks, CooperZhuang)
- The mod details and Show files keep Close in view, also for a mod with many files

## 2.2.0 (2026-11-14)
- The openmods page: the open-source mods listed for your game on openmods.nexus, with search, sort and a one-click install or update
- openmods: a Mods | Tools switch, the type on every card with a type filter, and Hide installed to leave out what you already have
- openmods: Full view shows the page over the whole window as a grid of cards, and MOD TYPE INFO explains each type
- On the Mods page, a mod installed from openmods links to its page there, in orange, and its update installs with one click
- Japanese and Korean
- Pak mods with .utoc and .ucas files are PAK too, and mods in PBZModLoader's mods folder have their own type, PBZModLoader
- A PBZModLoader mod's data files are its settings: you can edit them, an update keeps them, and an uninstall backs them up
- Settings ▸ Tray icon: Tray icon only, without a taskbar button
- No flash at start: the window shows once it is drawn
- Window and popup shadows, and a dimmer border while the window is not in front (thanks, HumanMus1c)
- Show files no longer lists a settings file that a mod writes beside its own (thanks, CooperZhuang)
- UEModManager.exe --cli: list, install, enable, disable and uninstall mods, back up saves and manage UE4SS from scripts (thanks, CooperZhuang)
- The update check of mods with a mod page reads versions such as 0.5.2.withHP and 1.2a correctly (thanks, HumanMus1c, for the report)

## 2.1.0 (2026-11-12)
- Backups has a page of its own in the menu: the save folder, the backup at launch and Back up now, with the backups in two columns
- Normal backups: a slider sets how many the app keeps, 2 to 20 (6 by default); the oldest go only after the next backup (thanks, EstusFlask)
- Personal backups: the green arrow moves a backup there, where the app never removes it and it does not count; the red arrow moves it back
- A note on every backup, such as "Before the final boss", and a note field next to Back up now
- A backup you do not need can be deleted, after a question
- A tighter menu, no subtexts under the page titles, and the version as About's title

## 2.0.0 (2026-11-11)
- One app for several games: Black Myth: Wukong and Phantom Blade Zero, and Stellar Blade: Blood Rain to peek at its look before the game is out
- Choose your game at the first start or with Switch game in the title bar; Settings can ask for the game at every start, and the picker has a message from the developer
- Each game has its own look, art and card; the app's icon is now the mod stack, and Windows shows it right after an update
- Black Myth: Wukong: pak, UE4SS and C# Loader mods, the C# Loader and UE4SS with one click, and save backups
- Launch options on the right of Launch game: the arguments the game starts with, with the game's recommended ones, such as Wukong's -fileopenlog for pak mods
- A tray icon next to the clock, with a menu to switch or launch the game; Settings can minimize and close the app to the tray
- Missing your game? Scan a game folder in the game picker makes a report to share on Discord, with your user name and ids hidden
- The game picker's cards show where each game is on this PC and how many mods it has
- At the first start the language question comes before the game picker, which also has a language selector
- The install review names the game the mods go into
- A UE4SS put in by hand is no longer offered as an update when it is already the newest build
- One download for every game, uemm_<version>.zip; older versions of the app still find their update

## 1.7.2 (2026-11-10)
- A mod's details can turn off its update check, to keep that version: the box then says Not tracked, and the check leaves the mod out
- The update check button comes back after its 30 second wait; before, it could stay grey for good
- The button is ready on the first visit of the Mods page: only a press starts the wait, not the check at start
- The button spins in green while a check runs, at least a second, so a click always shows something

## 1.7.1 (2026-11-10)
- An update keeps the name you gave a mod with Edit, like its tags; a name the app made from the file name still follows the new download (thanks, qhq)

## 1.7.0 (2026-11-10)
- Update check for mods with a mod page: one request to the mod site's public API asks for the page version of all of them, at each start and with the new button in the mod list's header, which then waits 30 seconds (thanks, msylgj)
- Each of these rows and the mod details show Update available, which opens the page's files, or Up to date; the Mods entry shows UPDATE when a mod has a newer version
- UPDATE CHECK NOTES next to the button explains what is compared and where the check cannot tell, such as a page version the author did not update
- The app sends only the mod numbers and downloads nothing; Settings ▸ Mod updates turns off the check at start

## 1.6.4 (2026-11-10)
- A program with the game's exe name in another folder, such as one started as administrator, no longer counts as the running game (thanks, High-cla)
- The log names the process that counted as the running game, with its path, or why its path could not be read

## 1.6.3 (2026-11-09)
- A settings file that a script mod makes in ~mods, such as Auto Collect Loot's PBZAutoCollect.txt, is now part of the mod: kept on an update, backed up on an uninstall, and no longer listed by Show files (thanks, CooperZhuang)
- The manager takes such a file at its start and when the game closes
- A settings file in ~mods now installs with its script mod also when the download has a folder of its own on top

## 1.6.2 (2026-11-08)
- A download from the same mod page as an installed mod, with other file names (such as a pak that became a script), now asks: Keep both comes first, or Update (thanks, CooperZhuang)
- A settings file that a script mod keeps in ~mods installs with that mod (thanks, CooperZhuang)
- A download with parts for several loaders installs whole, as its author says; a missing loader is now a warning, not a stop (thanks, CooperZhuang)
- No "Files left out" warning for data files such as MANIFEST.json beside the game's folders of a download

## 1.6.1 (2026-11-07)
- Open mod page in each mod's row and details, for a mod whose download kept the mod site's file name; the app reads the mod's number from that name, with no network
- Mods installed with an older version get the link too; other mods show "No mod page", and the mods on the card show neither
- A second download of the same file, whose name ends in (1), now also gets its name and version
- Not possible yet: a real update check. It needs access to the mod site's API, which still waits for approval, so Open mod page is the most the app can do for now

## 1.6.0 (2026-11-07)
- Mods Menu and PBZModLoader can be installed together: Mods Menu 0.9.0 and PBZModLoader 2.1.0 or newer run side by side
- A warning when a known older version of either would cause problems, and PBZModLoader's hid entry stays out beside Mods Menu
- Show files lists the files in the mods folder that the manager did not install
- The UE4SS window shows its note on which release to take only for a download by hand

## 1.5.9 (2026-11-07)
- Filter the mod list by All, Enabled or Disabled; Enabled hides the disabled mods
- Sort the list by Status, Type and Integrity too, not only by name and date
- Tag suggestions under the Tags field: your own tags and common ones, narrowed as you type; Tab completes

## 1.5.8 (2026-11-07)
- Same file names as an installed mod: the install window asks whether to update it or keep both
- Keep both puts the new mod in a folder of its own in ~mods, with a note that only one style may show
- Donate button above the Discord button: fully optional, and its X removes it for good (the About page keeps it)

## 1.5.7 (2026-11-06)
- Tags for your mods: add them at install or with the Add tag button, then filter the list with one click
- An X in the search boxes clears the text, and so does Esc
- Mods with loose files below the game's project folder install whole, such as the images of Quest Navigation
- Mods for PBZModLoader also install from a zip without the ProjectPBZ folder on top
- A .sig file goes with its pak, and a file such as license.json or notice.ini is no longer taken for a document
- The install window warns when a file stays out that the mod may need
- The install window warns about a mod with only a main.ts: PBZModLoader 2.0 cannot use it
- A clearer message when a download has no mod files

## 1.5.6 (2026-11-05)
- PBZModLoader on the recommended card: its own fix for pak mods, no UE4SS needed (thanks, LKF)
- UE4SS C++ BYPASS stays on the card as an extra download
- Upgrade from a PBZModLoader installed by hand: old files go to a backup, mods and settings stay
- Mods for PBZModLoader install, and a zip with a copy for two loaders installs only one copy
- A script mod waits until its loader is installed
- Mods Menu and PBZModLoader: only one of them at a time
- A mod that only shares a file with another mod no longer replaces it
- Scripts named like install.js or notice.js are no longer left out
- No more note before installing Game files or Exe files mods
- Install problems always stay in view; long file notes wrap

## 1.5.5 (2026-11-04)
- Script mods: files below Content/JavaScript now install, such as Mods Menu
- Refresh button on the Mods page, to check the mods folder again without a restart
- Game files and Exe files mods have a blue row, and a new one shows a short note before it installs
- The window no longer goes below its start size

## 1.5.4 (2026-11-04)
- Blue UPDATE on the Mods entry in the sidebar when the recommended mod has a new version

## 1.5.3 (2026-11-04)
- Recommended mod in its own card at the top of the Mods page, with an update check at every start
- One click installs it, and installs or updates UE4SS when the mod needs it
- First mod: UE4SS C++ BYPASS brings script-based pak mods back after the game's new script check (experimental)
- Technical notes on the card: how the mod works and where its source code is

## 1.5.2 (2026-11-03)
- Data, app updates and UE4SS downloads from Cloudflare first, GitHub as backup
- Remote box in the sidebar: where the data came from, with a short explanation
- "Try again" in that window when no data came at start
- Help reports name where the data and the update check came from
- Traditional Chinese: "Open exe folder" names the right folder (thanks, CooperZhuang)

## 1.5.1 (2026-11-02)
- Open folder links in the mod details: each folder of a mod opens in the game

## 1.5.0 (2026-11-02)
- Traditional Chinese (thanks, CooperZhuang)
- More natural Simplified Chinese texts (thanks, CooperZhuang)
- Traditional Chinese in a Taiwan font
- UE4SS log in tabs: UE4SS, Mods and Unnamed
- Known harmless UE4SS lines no longer count as errors
- Keep my version: a mod file changed outside the manager no longer blocks the mod
- Same title size on every page; long contributor lines wrap

## 1.4.1 (2026-11-01)
- Changelog link in the update box: what changed in each version
- A file another program holds gets a clear message, not an error (thanks, CooperZhuang)

## 1.4.0 (2026-11-01)
- Key binding and config files in the game's own folders (thanks, CooperZhuang)
- ReShade, RenoDX and other loader mods next to the game's exe
- The game's own files kept and put back; saves never touched
- A settings .txt next to a pak installs in any folder
- Install window shows where files go and names what it skips

## 1.3.2 (2026-11-01)
- Rename installed mods: blue Edit box in the mod details
- "Optional" hint translated

## 1.3.1 (2026-10-31)
- UE4SS mods with Scripts/config.json get the settings editor

## 1.3.0 (2026-10-31)
- Vietnamese
- Warning for an unofficial UE4SS build
- Mod names stay visible in a narrow window
- More natural Chinese texts

## 1.2.2 (2026-10-30)
- UE4SS folders without code install as they are, for mod settings pages

## 1.2.1 (2026-10-30)
- UE4SS mods with shared Lua libraries install completely
- File tree button for each mod
- Clear deletes the UE4SS log
- Update notes shown formatted

## 1.2.0 (2026-10-30)
- Mod details with every file a mod placed
- Mod sets: turn groups of mods on with one click
- Test the game without mods
- Restore a save backup
- Own settings editor with colours; a .txt next to a pak counts as settings
- UE4SS errors: note when the game closes, red mark in the list, log search
- Simplified Chinese (thanks, Null993)
- Window opens where it was; Ctrl+F and F5

## 1.1.1 (2026-10-29)
- Settings files next to a pak (.json, .ini and more) install with it
- Cleaner names for downloaded mods

## 1.1.0 (2026-10-29)
- First full release
- UE4SS for Phantom Blade Zero with one click
- UE4SS updates keep the settings you changed
- Finds the game's exe with another name, as on other stores

## 1.0.1 (2026-10-29)
- Folders of the released game (Steam)
- Save backups
- Locate game folder accepts only the game's folder

## 1.0.0 (2026-10-28)
- First pre-release
