# Application icon

- `caero-icon-source.png`: original transparent artwork generated with the built-in imagegen tool.
- `caero-icon.png`: 256px image embedded for the runtime window/taskbar icon.
- `caero.ico`: 16, 24, 32, 48, 64, 128 and 256px images embedded in the Windows executable.
- Rebuild the derived files with `powershell -File tools/BuildIcon.ps1` from the repository root.

Generation prompt:

> Use case: stylized-concept. Asset type: final Windows executable application icon for CAERO, a quiet mysterious first-person exploration game about wandering stone citadels surrounded by fog. Generate ONE square icon, transparent background, no lettering, no typography, no watermark, no surrounding mockup, no icon grid. Subject: a single monumental pale weathered limestone tower on a small floating angular stone island, an elegant tall rectangular open doorway and a simple ascending outside staircase visible in the silhouette, a thin soft wisp of blue-gray mist around the base. Restrained crafted isometric game emblem, simplified bold geometric masses and very subtle stone texture, crisp edges, ivory lit stone against deep charcoal-blue shadow faces with a tiny muted cyan light deep in doorway. Centered composition fills about 85% of the square, generous transparent margin. Strong distinctive silhouette and contrast for a 32px desktop icon; keep architectural details few and large. Atmospheric and solemn, mature and minimal, no vegetation, no characters, no sky rectangle. Output 1024x1024 transparent PNG.
