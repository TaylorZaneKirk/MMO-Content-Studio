// One owner for tile selection, local drafts, preview/save and conflict reconciliation.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const state = { session: {}, catalog: null, file: '', tile: null, draft: {}, references: {}, pending: false, preview: null, uncertain: false, comparison: null, page: 0 };
const flagNames = ['ignitable', 'slick'];
const labels = { ignitable: 'Can hold fire', slick: 'Can hold slick terrain', world_object_definition_id: 'World Object definition', item_id: 'Item definition', mob_definition_id: 'Mob definition' };
const images = new Map();
function node(tag, text, className) { const e = document.createElement(tag); if (text != null) e.textContent = text; if (className) e.className = className; return e; }
function notice(text, error = false) { $('notice').textContent = text; $('catalog-notice').textContent = text; $('notice').classList.toggle('error', error); }
const request = createRequest(() => state.session, () => { state.session.can_edit = false; controls(); notice('Sign in again. Your draft is retained.', true); });
function original(name) { return state.tile?.properties.find(p => p.name === name)?.value ?? null; }
function changes() { return Object.fromEntries(Object.entries(state.draft).filter(([k, v]) => v !== original(k))); }
function dirty() { return Object.keys(changes()).length > 0; }
function controls() {
    const editable = state.session.can_edit && !state.pending && !state.uncertain;
    $('preview').disabled = !editable || !dirty(); $('save').disabled = !editable || !state.preview?.applicable;
    $('tile-fields').disabled = !editable; $('tileset').disabled = state.pending || state.uncertain;
    $('refresh').disabled = state.pending || state.uncertain; $('reload').disabled = state.pending || !state.tile;
    $('session-button').disabled = state.pending; $('compare-disk').disabled = state.pending; $('compare-draft').disabled = state.pending;
    $('connection').textContent = state.session.trusted_home_lan ? 'Home LAN · owner access' : state.session.can_edit ? 'Connected · owner' : 'Read only';
    $('session-button').hidden = !state.session.configured || state.session.trusted_home_lan;
    $('session-button').textContent = state.session.authenticated ? 'Sign out' : 'Sign in';
    $('access-notice').hidden = !state.session.trusted_home_lan;
    $('access-notice').textContent = 'Trusted home LAN · anyone on the allowed network can access Studio. Tiles saves change the configured Tiled source files.';
}
function invalidated() { state.preview = null; $('review').hidden = true; controls(); }
function setDraft(name, value) { state.draft[name] = value; invalidated(); renderDiagnostics(); }
function mayLeave() {
    if (state.pending || state.uncertain) { notice('Reload and compare the current request before selecting another tile.', true); return false; }
    return !dirty() || confirm('Discard the unsaved metadata changes for this tile?');
}
function imageCanvas(tile, large = false) {
    const canvas = node('canvas', null, large ? 'tile-preview' : 'tile-thumb');
    canvas.width = large ? 240 : 88; canvas.height = large ? 200 : 76;
    canvas.setAttribute('role', 'img'); canvas.setAttribute('aria-label', tile.image.label);
    if (!tile.image.available) { canvas.title = 'Image unavailable'; return canvas; }
    const url = `/studio/api/tiles/image?file=${encodeURIComponent(state.file)}&id=${state.catalog.atlas ? 0 : tile.id}`;
    if (!images.has(url)) images.set(url, new Promise(resolve => { const image = new Image(); image.onload = () => resolve(image); image.onerror = () => resolve(null); image.src = url; }));
    images.get(url).then(image => {
        if (!image) { canvas.title = 'Image unavailable'; return; }
        const ctx = canvas.getContext('2d'); ctx.imageSmoothingEnabled = false;
        const i = tile.image, scale = Math.min(canvas.width / i.width, canvas.height / i.height, large ? 6 : 2);
        ctx.drawImage(image, i.x, i.y, i.width, i.height, (canvas.width - i.width * scale) / 2, (canvas.height - i.height * scale) / 2, i.width * scale, i.height * scale);
    });
    return canvas;
}
function matchingTiles() {
    const text = $('tile-search').value.toLowerCase(), filter = $('tile-filter').value;
    return (state.catalog?.tiles || []).filter(t => `${t.id} ${t.image.label} ${t.properties.map(p => `${p.name} ${p.value}`).join(' ')}`.toLowerCase().includes(text)
        && (filter === 'all' || filter === 'references' && t.properties.some(p => p.name.endsWith('_id')) || t.properties.some(p => p.name === filter && p.value === 'true')));
}
function renderGrid() {
    const tiles = matchingTiles(); const pageCount = Math.max(1, Math.ceil(tiles.length / 60)); state.page = Math.min(state.page, pageCount - 1);
    $('tile-count').textContent = `${tiles.length} tiles · page ${state.page + 1} of ${pageCount}`;
    $('previous').disabled = state.page === 0; $('next').disabled = state.page + 1 >= pageCount;
    const grid = $('tile-grid'); grid.replaceChildren();
    for (const tile of tiles.slice(state.page * 60, (state.page + 1) * 60)) {
        const button = node('button', null, `tile-card${state.tile?.id === tile.id ? ' selected' : ''}`); button.type = 'button';
        button.setAttribute('aria-label', `Tile ${tile.id}: ${tile.image.label}`);
        button.append(imageCanvas(tile), node('strong', `#${tile.id}`), node('small', tile.image.label));
        button.addEventListener('click', () => { if (!mayLeave()) return; selectTile(tile); location.hash = 'detail'; }); grid.append(button);
    }
    if (!tiles.length) grid.append(node('p', 'No tiles match these filters.', 'muted'));
}
function selectTile(tile) {
    state.tile = tile; state.draft = Object.fromEntries(tile.editableFields.map(k => [k, tile.properties.find(p => p.name === k)?.value ?? null]));
    state.preview = null; state.comparison = null; $('comparison').hidden = true; $('review').hidden = true;
    renderEditor(); renderGrid(); notice('Choose metadata, then preview your changes.');
}
function renderDiagnostics() {
    const panel = $('diagnostics'); panel.replaceChildren();
    for (const name of state.tile?.editableFields || []) {
        if (flagNames.includes(name) || state.draft[name] === null) continue;
        const catalog = state.references[name], value = state.draft[name], found = catalog?.items.find(x => x.id === value);
        const message = !catalog?.available ? `${labels[name]}: catalog unavailable; ${value} is retained.` : !found ? `${labels[name]}: unknown reference “${value}” is retained. Choose a replacement or explicitly unset it.` : found.publicationState !== 'Published' ? `${found.name} is ${found.publicationState}. Publish separately before map import.` : `${found.name} · Published`;
        panel.append(node('p', message, !found || found.publicationState !== 'Published' ? 'tile-warning' : 'muted'));
    }
    if (state.tile && !state.tile.image.available) panel.append(node('p', 'Image unavailable or outside the configured artwork root. Metadata is retained.', 'tile-warning'));
}
function renderEditor() {
    $('empty').hidden = !!state.tile; $('editor').hidden = !state.tile; if (!state.tile) { controls(); return; }
    const tile = state.tile;
    $('tile-title').textContent = `Tile #${tile.id}`; $('tile-subtitle').textContent = `${state.catalog.name} · ${tile.image.label}`;
    $('tile-art').replaceChildren(imageCanvas(tile, true));
    const fields = $('tile-fields'); fields.replaceChildren();
    for (const name of tile.editableFields) {
        const field = node('div', null, 'field'), label = node('label', labels[name]); label.htmlFor = `field-${name}`; field.append(label);
        const select = node('select'); select.id = `field-${name}`;
        if (flagNames.includes(name)) {
            for (const [value, text] of [['', 'Unset (false in game)'], ['true', 'True'], ['false', 'False']]) select.add(new Option(text, value));
        } else {
            const search = node('input'); search.type = 'search'; search.placeholder = 'Find by name or ID'; search.setAttribute('aria-label', `Find ${labels[name]}`); field.append(search);
            const options = () => {
                const query = search.value.toLowerCase(), current = state.draft[name]; select.replaceChildren(new Option('Unset reference', ''));
                const catalog = state.references[name]; const candidates = catalog?.items || [];
                if (current !== null && !candidates.some(x => x.id === current)) select.add(new Option(`${current} · unknown / unavailable (retained)`, current));
                for (const item of candidates.filter(x => x.id === current || `${x.id} ${x.name}`.toLowerCase().includes(query))) select.add(new Option(`${item.name} · ${item.id} · ${item.publicationState}`, item.id));
                select.value = current ?? '';
            };
            search.addEventListener('input', options); options();
        }
        select.value = state.draft[name] ?? ''; select.addEventListener('change', () => setDraft(name, select.value || null)); field.append(select, node('small', name, 'muted')); fields.append(field);
    }
    const readonly = $('other-properties'); readonly.replaceChildren();
    const other = tile.properties.filter(p => !p.editable);
    for (const property of other) readonly.append(node('p', `${property.name} (${property.type}): ${property.value ?? 'unset'}`));
    if (!other.length) readonly.append(node('p', 'No additional properties.', 'muted'));
    renderDiagnostics(); controls();
}
async function loadFile(file) {
    state.pending = true; controls(); notice('Reading tileset…');
    try {
        const catalog = await request(`/tiles/tileset?file=${encodeURIComponent(file)}`);
        state.catalog = catalog; state.file = file; state.tile = null; state.draft = {}; state.preview = null; state.page = 0; state.comparison = null; images.clear();
        $('tileset').value = file; await renderGrid(); await renderEditor(); notice('Select a tile to inspect its metadata.');
    } catch (e) { $('tileset').value = state.file; notice(e.message, true); }
    finally { state.pending = false; controls(); }
}
async function refresh() {
    if (!mayLeave()) return;
    state.pending = true; controls();
    try {
        state.session = await request('/session'); const catalog = await request('/tiles');
        $('tileset').replaceChildren(...catalog.tilesets.map(file => new Option(file, file)));
        try { state.references = await request('/tiles/references'); } catch { state.references = {}; }
        const file = catalog.tilesets.includes(state.file) ? state.file : catalog.tilesets[0];
        if (file) await loadFile(file); else notice('No TSX tilesets were found in the configured root.');
    } catch (e) { notice(e.message, true); }
    finally { state.pending = false; controls(); }
}
async function edit(save) {
    if (state.pending || state.uncertain || !state.session.can_edit || !state.tile) return;
    if (save && !state.preview?.applicable) return;
    state.pending = true; controls();
    try {
        const result = await request(`/tiles/${save ? 'save' : 'preview'}?file=${encodeURIComponent(state.file)}&id=${state.tile.id}`, { method: save ? 'PUT' : 'POST', body: { expectedHash: state.catalog.hash, changes: changes(), previewSignature: save ? state.preview.preview_signature : null } });
        if (!save) {
            state.preview = result; $('review').hidden = false; $('review-changes').replaceChildren(...result.changes.map(c => node('p', `${c.field}: ${c.before ?? 'unset'} → ${c.after ?? 'unset'}`)), ...result.warnings.map(w => node('p', w, 'tile-warning')));
            notice('Review these changes. Save only after saving and closing this tileset in Tiled.');
        } else {
            state.catalog.hash = result.hash;
            // A successful write is known, but use a fresh read before the next edit.
            state.uncertain = true; state.preview = null; $('review').hidden = true;
            notice('Tileset saved. Map import/publication is still separate; the game was not restarted. Reload / compare to continue.');
            await compare();
        }
    } catch (e) {
        state.preview = null; $('review').hidden = true;
        if (save || e.status === 409) state.uncertain = true;
        notice(`${e.message}${state.uncertain ? ' Reload / compare before continuing; do not repeat the save.' : ''}`, true);
    } finally { state.pending = false; controls(); }
}
async function compare() {
    try {
        const disk = await request(`/tiles/tileset?file=${encodeURIComponent(state.file)}`); state.comparison = disk;
        const tile = disk.tiles.find(t => t.id === state.tile.id);
        $('comparison').hidden = false;
        $('comparison-content').replaceChildren(node('p', tile ? 'Latest disk values compared with your retained draft:' : 'This tile is no longer present on disk. Use the disk version to continue.'));
        if (tile) {
            for (const name of new Set([...tile.properties.map(p => p.name), ...Object.keys(state.draft)])) $('comparison-content').append(node('p', `${name}: disk ${tile.properties.find(p => p.name === name)?.value ?? 'unset'} · draft ${Object.hasOwn(changes(), name) ? state.draft[name] ?? 'unset' : 'not edited'}`));
        }
        $('compare-draft').hidden = !tile || Object.keys(state.draft).some(k => !tile.editableFields.includes(k));
    } catch (e) { notice(`${e.message} Draft retained. Retry Reload / compare.`, true); }
}
function acceptComparison(keep) {
    if (!state.comparison || state.pending) return;
    const draft = changes(), id = state.tile.id; state.catalog = state.comparison; state.uncertain = false;
    const tile = state.catalog.tiles.find(t => t.id === id); state.comparison = null; $('comparison').hidden = true;
    if (tile) { selectTile(tile); if (keep) { Object.assign(state.draft, draft); renderEditor(); } }
    else { state.tile = null; state.draft = {}; renderGrid(); renderEditor(); }
    invalidated(); notice(keep ? 'Draft retained against the compared disk version. Preview again before saving.' : 'Loaded the current disk version.');
}
$('tileset').addEventListener('change', () => { const file = $('tileset').value; if (mayLeave()) loadFile(file); else $('tileset').value = state.file; });
$('refresh').addEventListener('click', refresh);
for (const id of ['tile-search', 'tile-filter']) $(id).addEventListener('input', () => { state.page = 0; renderGrid(); });
$('previous').addEventListener('click', () => { state.page--; renderGrid(); }); $('next').addEventListener('click', () => { state.page++; renderGrid(); });
$('preview').addEventListener('click', () => edit(false)); $('save').addEventListener('click', () => edit(true));
$('reload').addEventListener('click', async () => { if (state.pending) return; state.pending = true; controls(); await compare(); state.pending = false; controls(); });
$('compare-disk').addEventListener('click', () => acceptComparison(false)); $('compare-draft').addEventListener('click', () => acceptComparison(true));
$('session-button').addEventListener('click', async () => {
    if (!state.session.authenticated) { $('login-dialog').showModal(); return; }
    state.pending = true; controls();
    try { await request('/logout', { method: 'POST' }); state.session = await request('/session'); } catch (e) { notice(e.message, true); }
    finally { state.pending = false; controls(); }
});
$('cancel-login').addEventListener('click', () => $('login-dialog').close());
$('login-form').addEventListener('submit', async event => {
    event.preventDefault(); const submit = event.submitter; submit.disabled = true;
    try { state.session = await request('/session'); await request('/login', { method: 'POST', body: { password: $('password').value } }); $('password').value = ''; $('login-dialog').close(); state.session = await request('/session'); controls(); notice('Signed in. Your draft is retained. Refresh if no tileset is loaded.'); }
    catch { $('login-error').textContent = 'Sign in failed. Check the password and retry.'; }
    finally { submit.disabled = false; }
});
installNavigation(() => dirty() || state.uncertain || state.pending, () => state.pending || state.uncertain, notice);
if (!location.hash) location.hash = 'list';
await refresh();
