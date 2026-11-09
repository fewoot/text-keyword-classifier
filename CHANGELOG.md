# Changelog

All notable changes to this project are documented in this file.

## [1.0.0] - 2026-11-09

First public release.

### Features

- Scan a folder (optionally recursive) for `.txt` and `.docx` files and match
  keywords grouped into user-defined categories.
- Count rules:
  - longest match wins inside one category, so a shorter keyword contained in a
    longer one is not counted twice;
  - different categories count independently, even when their matches overlap;
  - three selectable counting modes for the `N` in the output file name
    (matched snippets / keyword occurrences / distinct keywords).
- Insert a category marker before matched keywords in the text
  (`〔123〕keyword`), optionally only for the first occurrence per category.
- Rename output files to `Category1N次-Category2N次-original.txt`, with
  configurable category ordering and automatic `(1)`, `(2)` de-duplication.
- Never modify the original files: results are written to a separate folder,
  the sub-directory structure is preserved.
- Encoding detection for UTF-8 (with/without BOM), UTF-16 and GBK; `.docx` body
  text is read directly from the OOXML package without requiring Word.
- WinForms UI in Chinese: folder pickers, category/keyword editor, options,
  live progress, per-file result table, timestamped log, preview mode
  (dry run), pause/resume.
- High-DPI aware layout: every size and font is scaled by the monitor DPI, so
  125% / 150% displays do not clip labels or buttons.
- Settings persisted to `config.json` next to the executable; import/export of
  category configuration.
- Zero third-party dependencies: builds with the compiler that ships with
  .NET Framework 4.8, no NuGet packages, no installer, single ~127 KB exe.
- 167 automated checks: core algorithm self-test, end-to-end file processing,
  UI structure, config save/restore, layout regression at 5 window sizes and
  DPI scaling at 100% / 125% / 150%.
- Application icon rendered by code (`tools/IconMaker.cs`, 9 sizes, hand-written
  ICO container) and embedded into the executable.

### Documentation

- `README.md` - usage, build, testing, structure.
- `docs/notes.md` - 25 lessons learned while building this project (batch files,
  WinForms layout, high DPI, testing).
- `docs/customize.md` - how to reuse this project as a template.
