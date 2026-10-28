# Theme

Everything here is compiled into `UEModManager.exe`. Nothing ships as a loose file.

| File | Purpose |
| --- | --- |
| `Palette.xaml` | Colors, brushes and font families. |
| `Controls.xaml` | Styles for buttons, inputs, navigation, scroll bars and text. |
| `Art/logo.png` | Square emblem with a transparent background, no text. Shown at 60 x 60 at the top of the sidebar, above the game name (which the UI draws as text). 256 x 256 is enough. |
| `Art/background.png` or `.jpg` | Cover art, wide (about 3:1), no text. Anchored to the top right of the content area and faded out toward the lower left. Keep the subject in the upper right and the left side dark. |
| `Fonts/*.ttf`, `Fonts/*.otf` | Optional. Built in as resources; reference one from `Palette.xaml`. |

Both images are optional. Without them the UI falls back to the drawn ink-circle
mark and a plain ink background with a faint red glow.

Keep the files small: they are embedded in the exe. A JPEG at quality 90 or so is
fine for the cover; the logo needs a PNG for its transparency.

To use a bundled font for headings, set `Font.Heading` in `Palette.xaml` to the
font's family name, for example:

```xml
<FontFamily x:Key="Font.Heading">pack://application:,,,/Theme/Fonts/#Cinzel</FontFamily>
```

Check that a font's license allows redistribution before adding it.

The exe icon is `../app.ico`.
