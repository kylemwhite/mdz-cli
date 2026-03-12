# The .mdz Format

`.mdz` is a ZIP archive containing Markdown documents, assets, and an optional `manifest.json` metadata file. The extension signals intent: this archive is a Markdown document bundle, not a generic ZIP.

## This archive's structure

The demo you're reading right now is an `.mdz` file. Here are its actual contents — the links below are internal navigation within the archive:

- [index.md](index.md) — entry point, overview page
- [format.md](format.md) — this page
- [faq.md](faq.md) — frequently asked questions
- [tools.md](tools.md) — tools and ecosystem
- [assets/overview.svg](assets/overview.svg) — archive diagram (an SVG image)
- `manifest.json` — metadata (click it in the sidebar)

A real-world archive might look like this:

```
my-document.mdz
├── manifest.json        ← optional, but recommended
├── index.md             ← entry point
├── chapter-2.md
├── appendix.md
└── assets/
    ├── overview.svg
    ├── diagram.png
    └── screenshot.jpg
```

## Entry point discovery

When a viewer opens an `.mdz`, it finds the primary document using this algorithm:

| Priority | Condition | Result |
|----------|-----------|--------|
| 1 | `manifest.json` present with `entryPoint` field | Use that file |
| 2 | `index.md` exists at the archive root | Use it |
| 3 | Exactly one `.md` or `.markdown` file at the root | Use it |
| 4 | None of the above | Error: ambiguous entry point |

## The manifest

`manifest.json` is optional but enables richer tooling. All fields except `mdz` and `title` are optional.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `mdz` | string | ✅ | Spec version, e.g. `"1.0.0-draft"` |
| `title` | string | ✅ | Human-readable document title |
| `description` | string | — | Short summary |
| `authors` | string[] | — | Author names |
| `keywords` | string[] | — | Tags for indexing |
| `license` | string | — | SPDX license identifier |
| `entryPoint` | string | — | Path to the primary Markdown file |
| `created` | string | — | ISO 8601 date |
| `modified` | string | — | ISO 8601 date |

## Path rules

All file paths inside an `.mdz` must:

- Use forward slashes as separators (`assets/photo.png`, not `assets\photo.png`)
- Not start with `/` or contain `..` components that escape the archive root
- Be encoded in UTF-8
- Be unique when compared case-insensitively on case-insensitive filesystems

## Error codes

Implementations should use these standardised error identifiers:

| Code | Meaning |
|------|---------|
| `ERR_ZIP_INVALID` | File is not a valid ZIP archive |
| `ERR_ZIP_ENCRYPTED` | Archive contains encrypted entries |
| `ERR_PATH_INVALID` | An entry path violates path rules |
| `ERR_MANIFEST_INVALID` | `manifest.json` is malformed |
| `ERR_ENTRYPOINT_UNRESOLVED` | No unambiguous entry point found |
| `ERR_ENTRYPOINT_MISSING` | `entryPoint` references a non-existent file |
| `ERR_VERSION_UNSUPPORTED` | `manifest.mdz` major version not supported |
