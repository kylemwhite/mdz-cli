/**
 * mdz-reader.js — core .mdz parsing and rendering utilities
 *
 * Implements the spec-defined algorithms for opening and rendering .mdz archives
 * in the browser. Covers entry point discovery (spec §5.5), path resolution
 * (spec §9), ZIP entry lookup, and image resolution from archive contents.
 *
 * Depends on JSZip (https://stuk.github.io/jszip/) being loaded separately.
 * Does not depend on any UI framework — suitable for use in any browser context.
 *
 * Public API:
 *   findEntry(zip, path)              → JSZipObject | null
 *   resolveEntryPoint(zip)            → Promise<string>
 *   resolveImages(zip, el, filePath)  → Promise<void>
 *   resolvePath(base, relative)       → string
 *   MIME_TYPES                        → object
 */

// ── MIME types ────────────────────────────────────────────────────────────────

/** Map of lowercase file extension to MIME type for image formats. */
const MIME_TYPES = {
  png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg',
  gif: 'image/gif', webp: 'image/webp', svg: 'image/svg+xml',
  avif: 'image/avif', ico: 'image/x-icon',
};

// ── Path utilities ────────────────────────────────────────────────────────────

/**
 * Returns the directory portion of an archive-relative file path.
 *
 * @param {string} filePath - Archive-relative path (e.g. `"chapters/intro.md"`)
 * @returns {string} Directory prefix with trailing slash, or `""` for root files
 *
 * @example
 * dirOf('chapters/intro.md') // => 'chapters/'
 * dirOf('index.md')          // => ''
 */
function dirOf(filePath) {
  const i = filePath.lastIndexOf('/');
  return i >= 0 ? filePath.slice(0, i + 1) : '';
}

/**
 * Resolves a relative reference against a base file path within a ZIP archive,
 * implementing path resolution per spec §9.
 *
 * Handles `..` and `.` components. Absolute paths (starting with `/`) are
 * treated as archive-root-relative by stripping the leading slash.
 *
 * @param {string} base     - Path of the referencing file (e.g. `"chapters/intro.md"`)
 * @param {string} relative - Reference to resolve (e.g. `"../images/fig.png"`)
 * @returns {string} Resolved archive-root-relative path
 * @throws {Error} If the resolved path escapes the archive root via `..`
 *
 * @example
 * resolvePath('chapters/intro.md', '../images/fig.png') // => 'images/fig.png'
 * resolvePath('index.md', 'assets/logo.svg')            // => 'assets/logo.svg'
 * resolvePath('a/b/c.md', '../../d.md')                 // => 'd.md'
 */
function resolvePath(base, relative) {
  if (relative.startsWith('/')) return relative.slice(1);
  const parts = (dirOf(base) + relative).split('/');
  const out = [];
  for (const p of parts) {
    if (p === '..') {
      if (out.length === 0) throw new Error('Path escapes archive root');
      out.pop();
    } else if (p !== '.') {
      out.push(p);
    }
  }
  return out.join('/');
}

// ── ZIP entry lookup (case-insensitive, backslash-tolerant) ───────────────────

/**
 * WeakMap cache keyed on the JSZip instance, so the lookup table is
 * automatically released when the zip object is garbage collected.
 * @type {WeakMap<object, Record<string, import('jszip').JSZipObject>>}
 */
const _entriesCache = new WeakMap();

/**
 * Looks up an entry in a JSZip instance by path.
 *
 * First attempts an exact match, then falls back to a case-insensitive search
 * with backslash normalisation — required for archives created on Windows where
 * paths may use `\` as the separator.
 *
 * @param {object} zip  - Open JSZip instance
 * @param {string} path - Archive-relative path to look up
 * @returns {object|null} The matching JSZipObject, or `null` if not found
 *
 * @example
 * const entry = findEntry(zip, 'assets/images/logo.png');
 * if (entry) {
 *   const base64 = await entry.async('base64');
 * }
 */
function findEntry(zip, path) {
  const normalized = path.replace(/\\/g, '/').replace(/^\//, '');
  if (zip.files[normalized]) return zip.files[normalized];
  if (!_entriesCache.has(zip)) {
    _entriesCache.set(zip, Object.fromEntries(
      Object.entries(zip.files).map(([k, v]) => [k.replace(/\\/g, '/').toLowerCase(), v])
    ));
  }
  return _entriesCache.get(zip)[normalized.toLowerCase()] || null;
}

// ── Entry point discovery (spec §5.5) ────────────────────────────────────────

/**
 * Resolves the primary Markdown file from an open .mdz archive, implementing
 * the entry point discovery algorithm defined in spec §5.5.
 *
 * Discovery order:
 * 1. `manifest.json` `entryPoint` field, if present and valid
 * 2. `index.md` at the archive root
 * 3. The sole `.md` or `.markdown` file at the archive root
 * 4. Error — ambiguous or no entry point found
 *
 * @param {object} zip - Open JSZip instance
 * @returns {Promise<string>} Archive-relative path of the primary Markdown file
 * @throws {Error} `ERR_MANIFEST_INVALID` — manifest.json is not valid JSON
 * @throws {Error} `ERR_ENTRYPOINT_MISSING` — manifest.json entryPoint references a missing file
 * @throws {Error} `ERR_ENTRYPOINT_UNRESOLVED` — no unambiguous entry point could be determined
 *
 * @example
 * const buf = await file.arrayBuffer();
 * const zip = await JSZip.loadAsync(buf);
 * const entryPoint = await resolveEntryPoint(zip); // e.g. 'index.md'
 * const text = await findEntry(zip, entryPoint).async('text');
 */
async function resolveEntryPoint(zip) {
  // 1. manifest.json entryPoint
  if (zip.files['manifest.json']) {
    const text = await zip.files['manifest.json'].async('text');
    let manifest;
    try { manifest = JSON.parse(text); } catch {
      throw new Error('ERR_MANIFEST_INVALID: manifest.json is not valid JSON');
    }
    if (manifest.entryPoint) {
      if (zip.files[manifest.entryPoint]) return manifest.entryPoint;
      throw new Error(`ERR_ENTRYPOINT_MISSING: manifest.json references "${manifest.entryPoint}" which is not in the archive`);
    }
  }

  // Root-level .md / .markdown files
  const rootMd = Object.keys(zip.files)
    .filter(p => !zip.files[p].dir && !p.includes('/') && !p.includes('\\') && (p.endsWith('.md') || p.endsWith('.markdown')))
    .sort();

  // 2. index.md at root
  if (zip.files['index.md']) return 'index.md';

  // 3. Exactly one root .md / .markdown
  if (rootMd.length === 1) return rootMd[0];

  // 4. Ambiguous
  if (rootMd.length > 1) {
    throw new Error('ERR_ENTRYPOINT_UNRESOLVED: Multiple Markdown files at the archive root and no manifest.json entryPoint. Add an index.md or a manifest.json to specify which file to open first.');
  }
  throw new Error('ERR_ENTRYPOINT_UNRESOLVED: No Markdown file found at the archive root.');
}

// ── Image resolution from ZIP ─────────────────────────────────────────────────

/**
 * Resolves `data-src` attributes on `<img>` elements within a rendered Markdown
 * document, replacing them with inline base64 data URIs sourced from the archive.
 *
 * Call this after setting `innerHTML` from `marked.parse()`. Images must be
 * written with `data-src` instead of `src` before calling — this prevents the
 * browser from making requests for the original relative paths, which are only
 * meaningful inside the archive.
 *
 * External URLs (starting with `http`) and existing data URIs are passed through
 * unchanged. Images that cannot be resolved are silently left without a `src`.
 *
 * @param {object}  zip      - Open JSZip instance
 * @param {Element} el       - DOM element containing the rendered Markdown HTML
 * @param {string}  filePath - Archive path of the Markdown file that was rendered,
 *                             used as the base for resolving relative image paths
 * @returns {Promise<void>}
 *
 * @example
 * const html = marked.parse(markdown);
 * // Prevent browser requests for relative paths by using data-src
 * el.innerHTML = html.replace(/(<img[^>]+)\ssrc="/gi, '$1 data-src="');
 * await resolveImages(zip, el, 'chapters/intro.md');
 */
async function resolveImages(zip, el, filePath) {
  const imgs = el.querySelectorAll('img[data-src]');
  await Promise.all([...imgs].map(async img => {
    const src = img.getAttribute('data-src');
    img.removeAttribute('data-src');
    if (!src || src.startsWith('http') || src.startsWith('data:')) {
      img.src = src; // external or data URI — set directly
      return;
    }
    let resolved;
    try { resolved = resolvePath(filePath, src); } catch { return; }
    const entry = findEntry(zip, resolved);
    if (!entry) return;
    try {
      const ext = resolved.split('.').pop().toLowerCase();
      const type = MIME_TYPES[ext] || 'application/octet-stream';
      const b64 = await entry.async('base64');
      img.src = `data:${type};base64,${b64}`;
    } catch { /* leave broken */ }
  }));
}
