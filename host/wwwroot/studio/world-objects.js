// One owner for World Object fields, artwork/alignment, preview/apply and recovery.
// The existing host service decides eligibility, dependencies and lifecycle transitions.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const clone = value => structuredClone(value);
const state = { session: {}, options: [], definition: null, draft: null, base: null, id: '', version: null,
    preview: null, pending: false, uncertain: false, remote: undefined, editRevision: 0, loadRevision: 0,
    searchRevision: 0, optionsRevision: 0, artTarget: null, assets: [], assetRevision: 0, frame: -1, playing: false, zoom: 1 };
const fields = new Map();
function element(tag, text, className) {
    const node = document.createElement(tag); if (text != null) node.textContent = text; if (className) node.className = className; return node;
}
function button(text, action, className) {
    const node = element('button', text, className); node.type = 'button'; node.addEventListener('click', action); return node;
}
function notice(message, error = false) { $('notice').textContent = message; $('notice').classList.toggle('error', error); }
function dirty() { return state.draft !== null && (!state.base || JSON.stringify(state.draft) !== JSON.stringify(state.base)); }
function changed() {
    state.editRevision++; state.preview = null; $('review').hidden = true;
    $('dirty-badge').textContent = dirty() ? 'Unsaved edits' : 'Saved'; $('dirty-badge').classList.toggle('dirty', dirty());
    $('object-title').textContent = state.draft?.display_name || 'Untitled world object'; updateActions();
}
function busy(value) { state.pending = value; updateActions(); }
function updateActions() {
    const locked = state.pending || state.uncertain || !state.draft;
    $('form-fields').disabled = state.pending || state.uncertain;
    $('new-object').disabled = state.pending || state.uncertain;
    $('operation').disabled = locked;
    $('preview').disabled = locked || !state.session.can_edit;
    $('apply').disabled = locked || !state.preview?.valid || !state.session.can_edit;
    $('reload-object').disabled = state.pending || !state.id;
    $('session-button').disabled = state.pending;
    $('use-server').disabled = state.pending; $('keep-local').disabled = state.pending;
    const notes = {
        save_draft: state.definition?.publication_state === 'Published'
            ? 'Saving unpublishes this definition and removes it from runtime export. Update Tiled placements before regenerating maps; publish again after saving.'
            : 'Save the complete definition as Draft. New definitions must be saved before publishing.',
        publish: 'Publish the saved definition. Save or discard local edits first. Artwork must exist.',
        disable: 'Disable the saved definition and remove it from runtime export. Review any Tiled placement references.',
        delete: 'Permanently delete a Draft or Disabled definition. Disable a Published definition first; review Tiled references.'
    };
    $('art-upload').disabled = locked || !state.session.can_edit;
    $('close-art').disabled = state.pending;
    $('operation-note').textContent = notes[$('operation').value];
}
const request = createRequest(() => state.session, () => {
    state.session.authenticated = false; state.session.can_edit = false; updateSession();
});
async function refreshSession() { state.session = await request('/session'); updateSession(); }
function updateSession() {
    $('connection').textContent = state.session.trusted_home_lan ? (state.session.read_only ? 'Home LAN · read only' : 'Home LAN · shared editor')
        : !state.session.configured ? 'Local preview · read only' : !state.session.authenticated ? 'Sign in to edit'
        : state.session.read_only ? 'Signed in · read only' : 'Connected · owner';
    $('session-button').textContent = state.session.authenticated ? 'Sign out' : 'Sign in';
    $('session-button').hidden = !state.session.configured || state.session.trusted_home_lan;
    $('access-notice').hidden = !state.session.trusted_home_lan;
    $('access-notice').textContent = state.session.read_only
        ? 'Trusted home LAN · no individual sign-in. This host currently permits viewing only.'
        : 'Trusted home LAN · anyone on the allowed network can view, upload, edit, publish and delete content. No individual sign-in.';
    updateActions();
}
async function loadOptions() {
    const revision = ++state.optionsRevision;
    try {
        const result = await request('/world-objects/options');
        if (revision === state.optionsRevision) state.options = result;
    } catch (error) { if (revision === state.optionsRevision) notice(`Defaults unavailable: ${error.message} Refresh to retry.`, true); }
}
async function search() {
    const revision = ++state.searchRevision; $('catalog-count').textContent = 'Loading…';
    try {
        const result = await request(`/world-objects?search=${encodeURIComponent($('search').value)}`);
        if (revision !== state.searchRevision) return;
        $('object-list').replaceChildren(); $('catalog-count').textContent = `${result.items.length} world objects`;
        for (const object of result.items) {
            const card = button('', () => loadObject(object.definition_id), `item-card${object.definition_id === state.id ? ' selected' : ''}`);
            const copy = element('span'); copy.append(element('strong', object.display_name), element('small', `${object.definition_id} · ${object.publication_state}`));
            card.append(copy); $('object-list').append(card);
        }
        if (!result.items.length) $('object-list').append(element('p', 'No matching world objects. Try another search or create a new world object.', 'list-message'));
    } catch (error) {
        if (revision !== state.searchRevision) return;
        $('catalog-count').textContent = 'Catalog unavailable'; $('object-list').replaceChildren(element('p', `${error.message} Use Refresh to retry.`, 'list-message'));
    }
}
function mayReplace() {
    if (state.pending) { notice('Wait for the current request to finish.'); return false; }
    if (state.uncertain) { notice('Reload and compare the World Object before replacing an uncertain write.', true); return false; }
    return !dirty() || confirm('Discard your unsaved World Object changes?');
}
async function loadObject(id) {
    if (!mayReplace()) return;
    const revision = ++state.loadRevision; busy(true);
    try {
        const definition = await request(`/world-objects/${encodeURIComponent(id)}`);
        if (revision !== state.loadRevision) return;
        adopt(definition); location.hash = 'detail'; notice('');
    } catch (error) { if (revision === state.loadRevision) notice(error.message, true); }
    finally { if (revision === state.loadRevision) busy(false); }
}
function adopt(definition) {
    state.definition = definition; state.id = definition.definition_id; state.version = definition.updated_at_utc;
    state.draft = clone(definition.draft); state.base = clone(definition.draft); state.uncertain = false; state.remote = undefined;
    $('reconcile').hidden = true; $('operation').value = 'save_draft'; render();
}
function newObject() {
    if (!mayReplace()) return;
    state.loadRevision++; state.definition = null; state.id = ''; state.version = null; state.base = null;
    state.uncertain = false; state.remote = undefined;
    state.draft = clone(state.options.defaults || { display_name: '', blocks_movement: false, footprint_width_tiles: 1, footprint_height_tiles: 1,
        visual_texture_path: '', source_width: 32, source_height: 32, visual_anchor_offset_x: 0, visual_anchor_offset_y: 0,
        visual_render_scale: 1, visual_animation_fps: null, public_interactions: [], visual_animation_frames: [] });
    $('operation').value = 'save_draft'; $('reconcile').hidden = true; render(); location.hash = 'detail';
    notice('Choose a stable World Object ID, then choose artwork and interactions. Preview before saving.');
}
function section(title, open, hint) {
    const details = element('details', null, 'section'); details.open = open; details.dataset.title = title;
    details.append(element('summary', title)); const content = element('div', null, 'section-content');
    if (hint) content.append(element('p', hint, 'muted')); details.append(content); $('form-fields').append(details); return content;
}
function grid(parent) { const node = element('div', null, 'fields'); parent.append(node); return node; }
function field(parent, owner, key, label, { integer = false, number = false, nullable = false, positive = false, readonly = false, path = key, hint = '', required = false } = {}) {
    const wrapper = element('div', null, 'field'), input = element('input');
    input.id = `object-field-${fields.size}`; fields.set(path, input); input.name = path;
    input.type = 'text'; input.value = owner[key] ?? ''; input.readOnly = readonly; input.required = required;
    if (number || integer) input.inputMode = 'decimal';
    const caption = element('label', label); caption.htmlFor = input.id; wrapper.append(caption, input);
    function validate() {
        const blank = input.value.trim() === '';
        const numeric = /^[+-]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?$/i.test(input.value.trim());
        const value = Number(input.value);
        const valid = !(integer || number) || nullable && blank || numeric && Number.isFinite(value)
            && (!positive || value > 0) && (!integer || Number.isInteger(value) && value > 0 && value <= 2147483647);
        input.setCustomValidity(valid ? '' : integer ? 'Enter a positive whole number up to 2147483647.' : 'Enter a finite number' + (positive ? ' greater than zero.' : '.'));
        return valid;
    }
    validate(); input.addEventListener('input', () => {
        const valid = validate();
        owner[key] = nullable && input.value.trim() === '' ? null : (number || integer) && valid ? Number(input.value) : input.value;
        changed(); drawAlignment();
    });
    if (hint) wrapper.append(element('small', hint)); parent.append(wrapper); return input;
}
function toggle(parent, owner, key, label, path = key) {
    const wrapper = element('label', null, 'toggle'), input = element('input'); input.type = 'checkbox'; input.checked = owner[key]; fields.set(path, input);
    input.addEventListener('change', () => { owner[key] = input.checked; changed(); drawAlignment(); });
    wrapper.append(input, document.createTextNode(label)); parent.append(wrapper); return input;
}
function orderActions(parent, array, index, label) {
    const actions = element('div', null, 'row-actions');
    const up = button('Move up', () => { [array[index - 1], array[index]] = [array[index], array[index - 1]]; render(); }); up.disabled = index === 0;
    const down = button('Move down', () => { [array[index + 1], array[index]] = [array[index], array[index + 1]]; render(); }); down.disabled = index === array.length - 1;
    actions.append(up, down, button('Remove', () => { if (confirm(`Remove ${label} ${index + 1}?`)) { array.splice(index, 1); render(); } })); parent.append(actions);
}
function artwork(parent, owner, key, label, path) {
    const group = grid(parent); field(group, owner, key, label, { path, required: true, hint: 'Canonical res://assets/… PNG path. Missing references are preserved.' });
    const controls = element('div', null, 'art-controls'); const image = element('img'); image.alt = label; image.className = 'world-thumbnail'; image.loading = 'lazy';
    const update = () => { image.src = artUrl(owner[key]); };
    update(); fields.get(path).addEventListener('input', update);
    image.addEventListener('error', () => { image.alt = 'Artwork unavailable'; });
    controls.append(image, button('Choose / import PNG', () => openArt(owner, key))); group.append(controls);
}
function render() {
    stopPlayback(); state.frame = -1;
    const opened = new Set([...document.querySelectorAll('.section[open]')].map(node => node.dataset.title));
    $('empty').hidden = true; $('editor').hidden = false; $('form-fields').replaceChildren(); fields.clear();
    $('object-state').textContent = state.definition?.publication_state?.toUpperCase() || 'NEW WORLD OBJECT';
    $('object-meta').textContent = state.definition ? `${state.id} · Updated ${state.version}` : 'Unsaved definition';
    const d = state.draft;
    const basics = section('Definition & footprint', true, 'Reusable definitions only. Place instances, elevation and surfaces in Tiled.');
    const basicsGrid = grid(basics);
    field(basicsGrid, state, 'id', 'Definition ID', { readonly: !!state.definition, path: 'definition_id', required: true, hint: 'Stable lowercase ID, such as garden_fountain.' });
    field(basicsGrid, d, 'display_name', 'Display name', { required: true });
    field(basicsGrid, d, 'footprint_width_tiles', 'Footprint width (tiles)', { integer: true });
    field(basicsGrid, d, 'footprint_height_tiles', 'Footprint height (tiles)', { integer: true });
    toggle(basics, d, 'blocks_movement', 'Blocks movement across the footprint');
    const art = section('Artwork & alignment', true, 'Solid teal = footprint; amber cross = placement origin; dashed outline = declared source dimensions. Artwork starts at origin + anchor offset. One tile is 32 game pixels.');
    artwork(art, d, 'visual_texture_path', 'Base texture', 'visual_texture_path');
    const numbers = grid(art);
    field(numbers, d, 'source_width', 'Source width (pixels)', { integer: true });
    field(numbers, d, 'source_height', 'Source height (pixels)', { integer: true });
    field(numbers, d, 'visual_anchor_offset_x', 'Anchor X (game pixels)', { number: true, hint: 'Positive moves right; negative moves left.' });
    field(numbers, d, 'visual_anchor_offset_y', 'Anchor Y (game pixels)', { number: true, hint: 'Positive moves down; negative moves up.' });
    field(numbers, d, 'visual_render_scale', 'Render scale', { number: true, positive: true, hint: '1 = one source pixel per game pixel. Offsets are not scaled.' });
    const canvas = element('canvas'); canvas.id = 'alignment-canvas'; canvas.width = 960; canvas.height = 560; canvas.setAttribute('aria-label', 'World Object artwork aligned with tile footprint'); art.append(canvas);
    const info = element('p', null, 'muted'); info.id = 'alignment-info'; info.setAttribute('role', 'status'); art.append(info);
    const controls = element('div', null, 'row-actions');
    controls.append(button('Zoom −', () => { state.zoom = Math.max(.25, state.zoom / 1.5); drawAlignment(); }), button('Fit preview', () => { state.zoom = 1; drawAlignment(); }), button('Zoom +', () => { state.zoom = Math.min(8, state.zoom * 1.5); drawAlignment(); }));
    for (const [label, key, delta] of [['← 1 px', 'visual_anchor_offset_x', -1], ['→ 1 px', 'visual_anchor_offset_x', 1], ['↑ 1 px', 'visual_anchor_offset_y', -1], ['↓ 1 px', 'visual_anchor_offset_y', 1]]) controls.append(button(label, () => {
        if (typeof d[key] !== 'number' || !Number.isFinite(d[key] + delta)) return;
        d[key] += delta; fields.get(key).value = d[key]; fields.get(key).dispatchEvent(new Event('input'));
    }));
    controls.append(button('Use PNG dimensions', () => {
        const image = images.get(d.visual_texture_path); if (!image?.naturalWidth) { notice('Load the base PNG before using its dimensions.', true); return; }
        d.source_width = image.naturalWidth; d.source_height = image.naturalHeight;
        for (const key of ['source_width', 'source_height']) { fields.get(key).value = d[key]; fields.get(key).dispatchEvent(new Event('input')); }
    })); art.append(controls);
    art.append(element('p', 'Preview uses actual full PNG dimensions, matching the game renderer. Source dimensions are metadata, not a crop. Elevation, occlusion, placement markers and depleted resource artwork belong to the game/Tiled.', 'muted'));
    const interactions = section('Ordered interactions', true, 'Actions appear top to bottom. At most one can be default; no default is allowed. Choosing a default here replaces the previous default explicitly.');
    d.public_interactions.forEach((row, index) => {
        const box = element('div', null, 'repeat-row'); box.append(element('p', `Interaction ${index + 1}`, 'row-heading')); const inputs = grid(box);
        field(inputs, row, 'action_id', 'Action ID', { path: `public_interactions.${index}.action_id`, required: true });
        field(inputs, row, 'label', 'Player-facing label', { path: `public_interactions.${index}.label`, required: true });
        const check = toggle(box, row, 'is_default', 'Default action', `public_interactions.${index}.is_default`);
        check.addEventListener('change', () => { if (check.checked) { for (const other of d.public_interactions) if (other !== row) other.is_default = false; render(); } });
        orderActions(box, d.public_interactions, index, 'interaction'); interactions.append(box);
    });
    if (!d.public_interactions.length) interactions.append(element('p', 'No public interactions.', 'muted'));
    interactions.append(button('+ Add interaction', () => { d.public_interactions.push({ action_id: '', label: '', is_default: false }); render(); }));
    const animation = section('Animation frames & playback', true, 'Ordered PNGs loop at the authored FPS. With no frames, the base texture is used. Missing frames remain visible for correction.');
    field(animation, d, 'visual_animation_fps', 'Frames per second (optional without frames)', { number: true, positive: true, nullable: true });
    const playback = element('div', null, 'row-actions');
    const select = element('select'); select.id = 'preview-frame'; select.setAttribute('aria-label', 'Preview frame');
    const base = element('option', 'Base texture'); base.value = '-1'; select.append(base);
    d.visual_animation_frames.forEach((_, index) => { const option = element('option', `Frame ${index + 1}`); option.value = String(index); select.append(option); });
    select.addEventListener('change', () => { stopPlayback(); state.frame = Number(select.value); drawAlignment(); });
    const play = button('Play animation', () => {
        if (state.playing) { stopPlayback(); return; }
        if (!d.visual_animation_frames.length || typeof d.visual_animation_fps !== 'number' || !(d.visual_animation_fps > 0)) { notice('Add frames and a positive FPS to play the preview.', true); return; }
        state.playing = true; play.textContent = 'Pause animation'; animationStart = performance.now(); animate(animationStart);
    }); play.id = 'play-animation'; playback.append(select, play, button('View alignment', () => { art.parentElement.open = true; canvas.scrollIntoView({ block: 'center', behavior: 'smooth' }); })); animation.append(playback);
    d.visual_animation_frames.forEach((_, index) => {
        const box = element('div', null, 'repeat-row'); box.append(element('p', `Frame ${index + 1}`, 'row-heading'));
        artwork(box, d.visual_animation_frames, index, 'Frame PNG', `visual_animation_frames.${index}`);
        orderActions(box, d.visual_animation_frames, index, 'frame'); animation.append(box);
    });
    if (!d.visual_animation_frames.length) animation.append(element('p', 'Static artwork — no animation frames.', 'muted'));
    animation.append(button('+ Add frame', () => { d.visual_animation_frames.push(''); render(); }));
    for (const node of document.querySelectorAll('.section')) if (opened.has(node.dataset.title)) node.open = true;
    changed(); drawAlignment();
}
// Preview-only state never enters the complete authored aggregate.
const images = new Map(); let animationHandle, animationStart = 0;
function artUrl(resource) { return `/studio/api/world-objects/asset?resource=${encodeURIComponent(resource || '')}`; }
function getImage(resource) {
    if (!resource) return null;
    if (!images.has(resource)) {
        const image = new Image(); images.set(resource, image);
        image.onload = () => drawAlignment(); image.onerror = () => { image.dataset.failed = 'true'; drawAlignment(); }; image.src = artUrl(resource);
    }
    return images.get(resource);
}
function stopPlayback() {
    state.playing = false; cancelAnimationFrame(animationHandle);
    if ($('play-animation')) $('play-animation').textContent = 'Play animation';
}
function animate(now) {
    if (!state.playing) return;
    const fps = state.draft?.visual_animation_fps, count = state.draft?.visual_animation_frames.length;
    if (!count || typeof fps !== 'number' || !Number.isFinite(fps) || fps <= 0) { stopPlayback(); return; }
    // Modulo time before multiplication keeps very large valid FPS finite.
    const frame = Math.floor(((now - animationStart) / 1000 % (count / fps)) * fps) % count;
    if (frame !== state.frame) { state.frame = frame; $('preview-frame').value = String(frame); drawAlignment(); }
    animationHandle = requestAnimationFrame(animate);
}
function drawAlignment() {
    const canvas = $('alignment-canvas'), d = state.draft; if (!canvas || !d) return;
    const ctx = canvas.getContext('2d'); ctx.clearRect(0, 0, canvas.width, canvas.height); ctx.fillStyle = '#edf3f0'; ctx.fillRect(0, 0, canvas.width, canvas.height);
    const values = [d.footprint_width_tiles, d.footprint_height_tiles, d.source_width, d.source_height, d.visual_anchor_offset_x, d.visual_anchor_offset_y, d.visual_render_scale];
    if (values.some(value => typeof value !== 'number' || !Number.isFinite(value)) || values.slice(0,4).some(value => value <= 0) || d.visual_render_scale <= 0) { $('alignment-info').textContent = 'Enter valid dimensions, offsets and scale to see alignment.'; return; }
    const path = state.frame < 0 ? d.visual_texture_path : d.visual_animation_frames[state.frame];
    const image = getImage(path), scale = d.visual_render_scale, x = d.visual_anchor_offset_x, y = d.visual_anchor_offset_y;
    const fw = d.footprint_width_tiles * 32, fh = d.footprint_height_tiles * 32;
    const sw = d.source_width * scale, sh = d.source_height * scale;
    const iw = (image?.naturalWidth || d.source_width) * scale, ih = (image?.naturalHeight || d.source_height) * scale;
    const left = Math.min(0, x), top = Math.min(0, y), right = Math.max(fw, x + Math.max(iw,sw)), bottom = Math.max(fh, y + Math.max(ih,sh));
    if (![sw,sh,iw,ih,right-left,bottom-top].every(Number.isFinite) || right-left <= 0 || bottom-top <= 0) { $('alignment-info').textContent = 'Values are preserved, but these dimensions exceed the preview drawing range.'; return; }
    const fit = Math.min((canvas.width-100)/(right-left), (canvas.height-100)/(bottom-top)) * state.zoom;
    const ox = canvas.width/2 - (left/2+right/2)*fit, oy = canvas.height/2 - (top/2+bottom/2)*fit;
    ctx.save(); ctx.translate(ox,oy); ctx.scale(fit,fit); ctx.imageSmoothingEnabled = false;
    ctx.fillStyle = d.blocks_movement ? '#38918c44' : '#38918c1a'; ctx.fillRect(0,0,fw,fh);
    ctx.strokeStyle = '#318b83'; ctx.lineWidth = 2/fit; ctx.strokeRect(0,0,fw,fh);
    // Bound grid work even for enormous legal footprints. The outer footprint is always exact.
    if (d.footprint_width_tiles <= 64 && d.footprint_height_tiles <= 64) {
        ctx.lineWidth = 1/fit; ctx.beginPath();
        for (let i=1;i<d.footprint_width_tiles;i++) { ctx.moveTo(i*32,0); ctx.lineTo(i*32,fh); }
        for (let i=1;i<d.footprint_height_tiles;i++) { ctx.moveTo(0,i*32); ctx.lineTo(fw,i*32); } ctx.stroke();
    }
    if (image?.naturalWidth) ctx.drawImage(image,x,y,iw,ih);
    ctx.strokeStyle = '#52647d'; ctx.lineWidth = 2/fit; ctx.setLineDash([6/fit,5/fit]); ctx.strokeRect(x,y,sw,sh); ctx.setLineDash([]);
    ctx.strokeStyle = '#b87516'; ctx.lineWidth = 3/fit; ctx.beginPath(); ctx.moveTo(-10/fit,0);ctx.lineTo(10/fit,0);ctx.moveTo(0,-10/fit);ctx.lineTo(0,10/fit);ctx.stroke(); ctx.restore();
    const status = image?.naturalWidth ? `PNG ${image.naturalWidth} × ${image.naturalHeight}px${image.naturalWidth !== d.source_width || image.naturalHeight !== d.source_height ? ' — differs from declared source dimensions' : ''}` : image?.dataset.failed ? 'PNG unavailable; reference preserved' : 'Loading PNG…';
    $('alignment-info').textContent = `${state.frame < 0 ? 'Base texture' : `Frame ${state.frame+1}`} · ${status}. Footprint ${d.footprint_width_tiles} × ${d.footprint_height_tiles} tiles. ${Math.round(state.zoom*100)}% of fit.`;
}
async function loadAssets() {
    const revision = ++state.assetRevision; $('art-status').textContent = 'Loading artwork…';
    try { const result = await request('/world-objects/assets'); if (revision !== state.assetRevision) return; state.assets = result.assets; $('art-status').textContent = `${state.assets.length} PNGs available. Upload limit: 16 MiB, 4096px per side, 16 million pixels.`; renderArt(); }
    catch (error) { if (revision === state.assetRevision) $('art-status').textContent = `Artwork unavailable: ${error.message} Use Refresh artwork to retry.`; }
}
function openArt(owner, key) {
    if (state.pending || state.uncertain) return;
    state.artTarget = { owner, key }; $('art-search').value = ''; $('art-dialog').showModal(); $('art-search').focus(); void loadAssets();
}
function renderArt() {
    $('art-results').replaceChildren(); const query = $('art-search').value.trim().toLowerCase();
    const matches = state.assets.filter(asset => `${asset.display_name} ${asset.resource_path}`.toLowerCase().includes(query));
    // Page the display only, never the stored references or catalog. Search covers every asset.
    for (const asset of matches.slice(0, 120)) {
        const choice = button('', () => { if (state.pending || state.uncertain || !state.artTarget) return; state.artTarget.owner[state.artTarget.key] = asset.resource_path; $('art-dialog').close(); state.artTarget = null; render(); });
        const img = element('img'); img.src = asset.url; img.loading = 'lazy'; img.alt = ''; choice.append(img, element('strong',asset.display_name),element('small',asset.resource_path)); $('art-results').append(choice);
    }
    if (matches.length > 120) $('art-results').append(element('p', `${matches.length} matches. Showing the first 120; narrow the search to find another PNG.`, 'muted'));
    if (!matches.length) $('art-results').append(element('p','No matching PNGs. Try another path/name or refresh artwork.','muted'));
}
async function uploadArt() {
    const file = $('art-upload').files[0]; if (!file || state.pending || state.uncertain || !state.artTarget || !state.session.can_edit) return;
    if (file.size > 16*1024*1024) { $('art-status').textContent = 'Choose a PNG no larger than 16 MiB.'; return; }
    if (!confirm(`Import ${file.name}? This creates artwork immediately, independently of saving the definition. Existing different files are never overwritten.`)) return;
    busy(true); $('art-status').textContent = 'Importing PNG…';
    try { const result = await request(`/world-objects/assets/upload?name=${encodeURIComponent(file.name)}`, { method:'POST', body:file, raw:true });
        if (!result?.asset?.resource_path) throw new Error('Upload outcome was not confirmed.');
        state.artTarget.owner[state.artTarget.key] = result.asset.resource_path; images.delete(result.asset.resource_path); $('art-dialog').close(); state.artTarget = null; render(); notice(`${result.message} The definition has not been saved.`);
    } catch(error) { $('art-status').textContent = `${error.message} The upload may already exist; retry with the same name and bytes to safely reuse it. Definition edits are retained.`; }
    finally { $('art-upload').value = ''; busy(false); }
}
function messages(entries) {
    $('review-messages').replaceChildren();
    for (const message of entries) {
        const box = element('div', `${message.message}${message.remediation ? ' ' + message.remediation : ''}`, `message ${message.severity}`);
        const input = fields.get(message.field) || (message.field === 'invalid_dimensions' ? fields.get('source_width') : null);
        if (input) box.append(button('Go to field', () => { input.closest('details').open = true; input.focus(); input.scrollIntoView({ block: 'center' }); }));
        $('review-messages').append(box);
    }
}
function showChanges(changes) {
    $('review-changes').replaceChildren();
    for (const change of changes) {
        const box = element('div', null, 'change'); box.append(element('strong', change.field.replaceAll('_', ' ')),
            element('pre', `Before\n${JSON.stringify(change.before, null, 2)}\n\nAfter\n${JSON.stringify(change.after, null, 2)}`)); $('review-changes').append(box);
    }
    if (!changes.length) $('review-changes').append(element('p', 'No differences in this preview.', 'muted'));
}
function validForm() {
    const invalid = $('object-form').querySelector('input:invalid, select:invalid, textarea:invalid');
    if (invalid) { invalid.closest('details').open = true; invalid.reportValidity(); invalid.focus(); return false; }
    return true;
}
async function preview() {
    if (state.pending || state.uncertain || !state.draft || !validForm()) return;
    const operation = $('operation').value;
    if (operation !== 'save_draft' && dirty()) { notice('Save or discard local edits before applying an operation to the saved World Object.', true); return; }
    if (!/^[a-z][a-z0-9_]*$/.test(state.id)) { notice('Use a stable lowercase World Object ID, with optional underscore-separated words.', true); fields.get('definition_id').focus(); return; }
    const revision = state.editRevision, id = state.id;
    const payload = { draft: clone(state.draft), expected_updated_at_utc: state.version, preview_signature: null, target_operation: operation };
    state.preview = null; busy(true);
    try {
        const result = await request(`/world-objects/${encodeURIComponent(id)}/preview`, { method: 'POST', body: payload });
        if (revision !== state.editRevision || id !== state.id) return;
        state.preview = { valid: result.applicable, signature: result.preview_signature, revision, id, operation, payload };
        messages(result.messages); showChanges(result.changes); $('review').hidden = false;
        notice(result.applicable ? 'Review the changes below, then apply this exact preview.' : 'Resolve the validation messages and preview again.', !result.applicable);
        $('review').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (error) { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    finally { busy(false); }
}
async function apply() {
    const reviewed = state.preview;
    if (state.pending || state.uncertain || !reviewed?.valid || reviewed.revision !== state.editRevision || reviewed.id !== state.id) return;
    if (['delete', 'disable'].includes(reviewed.operation) && !confirm(`${reviewed.operation === 'delete' ? 'Permanently delete' : 'Disable'} World Object ${state.id}?`)) return;
    busy(true);
    try {
        const path = reviewed.operation === 'save_draft' ? 'draft' : reviewed.operation.replaceAll('_', '-');
        const result = await request(`/world-objects/${encodeURIComponent(reviewed.id)}/${path}`, {
            method: reviewed.operation === 'save_draft' ? 'PUT' : 'POST', body: { ...reviewed.payload, preview_signature: reviewed.signature }
        });
        if (result?.database_committed !== true || (reviewed.operation !== 'delete' && !result.definition)) throw new Error('The host response did not confirm the complete write outcome.');
        if (result.definition) adopt(result.definition);
        else { stopPlayback(); state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.preview = null;
            $('editor').hidden = true; $('empty').hidden = false; location.hash = 'list'; }
        const exportNote = { succeeded: 'World Object catalog export succeeded.', failed: 'World Object catalog export failed; inspect host logs without repeating this mutation.',
            skipped: 'World Object catalog export was skipped; inspect host configuration.', not_requested: 'No catalog export was requested.' }[result.catalog_export] || 'Catalog export outcome is unknown.';
        notice(`Database change committed. ${exportNote} The live game was not restarted.`, ['failed', 'skipped'].includes(result.catalog_export));
        messages(result.messages || []); $('review-changes').replaceChildren(); $('review').hidden = !(result.messages?.length);
        void search(); void loadOptions();
    } catch (error) {
        state.preview = null;
        // Transport/server failures may occur after commit. Never offer blind resubmission.
        if (!error.status || error.status >= 500 || error.status === 409 || error.status === 404) {
            state.uncertain = true; state.remote = undefined; $('reconcile').hidden = true;
            notice(`${error.message} The write may have completed or the World Object changed. Reload / compare before continuing. Local edits are retained.`, true);
        } else { notice(error.message, true); messages(error.errors || []); $('review').hidden = false; }
    } finally { busy(false); }
}
async function reconcile() {
    if (state.pending || !state.id) return;
    busy(true); state.preview = null; state.remote = undefined; $('reconcile').hidden = true;
    try {
        try { state.remote = await request(`/world-objects/${encodeURIComponent(state.id)}`); }
        catch (error) { if (error.status === 404) state.remote = null; else throw error; }
        state.uncertain = true; $('comparison').replaceChildren();
        const local = element('pre', JSON.stringify(state.draft, null, 2), 'readonly');
        $('comparison').append(element('h3', 'Your local definition'), local);
        if (state.remote) {
            $('comparison').append(element('h3', `Server · ${state.remote.publication_state} · ${state.remote.updated_at_utc}`), element('pre', JSON.stringify(state.remote.draft, null, 2), 'readonly'));
            $('use-server').textContent = 'Use server version'; $('keep-local').textContent = 'Keep my edits on this version';
        } else {
            $('comparison').append(element('p', 'The World Object is absent on the server. A prior deletion may have completed.'));
            $('use-server').textContent = 'Acknowledge missing World Object'; $('keep-local').textContent = 'Keep edits as a new draft';
        }
        $('keep-local').hidden = false; $('reconcile').hidden = false;
        notice('Compare before continuing. Stored content does not prove catalog export or live-game reload.');
        $('reconcile').scrollIntoView({ block: 'start', behavior: 'smooth' });
    } catch (error) { notice(`Could not reconcile: ${error.message} Local edits remain.`, true); }
    finally { busy(false); }
}
$('use-server').addEventListener('click', () => {
    if (state.pending || state.remote === undefined || !confirm('Discard local edits and accept the current server result?')) return;
    if (state.remote) adopt(state.remote);
    else { stopPlayback(); state.draft = null; state.base = null; state.definition = null; state.id = ''; state.version = null; state.uncertain = false; state.remote = undefined;
        $('editor').hidden = true; $('empty').hidden = false; $('reconcile').hidden = true; location.hash = 'list'; }
    notice('Server result acknowledged. Export status is not inferred from stored content.'); updateActions(); void search();
});
$('keep-local').addEventListener('click', () => {
    if (state.pending || state.remote === undefined || !confirm('Keep these edits against the current server result? A fresh preview is required before writing.')) return;
    state.definition = state.remote; state.version = state.remote?.updated_at_utc ?? null; state.base = state.remote ? clone(state.remote.draft) : null;
    state.uncertain = false; state.remote = undefined; $('reconcile').hidden = true;
    $('operation').value = 'save_draft'; render();
    notice('Local edits retained against the current version. Preview again before applying.');
});
$('world-objects-workspace').addEventListener('click', () => location.hash = 'list');
$('new-object').addEventListener('click', newObject);
$('refresh-list').addEventListener('click', () => { void search(); void loadOptions(); });
let searchTimer; $('search').addEventListener('input', () => { state.searchRevision++; clearTimeout(searchTimer); searchTimer = setTimeout(search, 200); });
$('operation').addEventListener('change', changed); $('preview').addEventListener('click', preview); $('apply').addEventListener('click', apply);
$('reload-object').addEventListener('click', reconcile); $('object-form').addEventListener('submit', event => event.preventDefault());
$('close-art').addEventListener('click', () => $('art-dialog').close());
$('art-dialog').addEventListener('cancel', event => { if (state.pending) event.preventDefault(); });
$('art-search').addEventListener('input', renderArt); $('refresh-art').addEventListener('click', loadAssets); $('art-upload').addEventListener('change', uploadArt);
document.addEventListener('visibilitychange', () => { if (document.hidden) stopPlayback(); });
$('session-button').addEventListener('click', async () => {
    if (state.session.authenticated) {
        try { await request('/logout', { method: 'POST' }); await refreshSession(); notice('Signed out. Unsaved edits remain in this tab.'); }
        catch (error) { notice(error.message, true); } return;
    }
    $('login-dialog').showModal(); $('password').focus();
});
$('cancel-login').addEventListener('click', () => $('login-dialog').close());
$('login-form').addEventListener('submit', async event => {
    event.preventDefault(); const submit = event.submitter; submit.disabled = true; $('login-error').textContent = '';
    try {
        await refreshSession(); await request('/login', { method: 'POST', body: { password: $('password').value } });
        $('password').value = ''; await refreshSession(); $('login-dialog').close(); void loadOptions(); void search(); notice('Signed in. Local edits retained.');
    } catch (error) { $('login-error').textContent = error.message; } finally { submit.disabled = false; }
});
$('search').value = new URLSearchParams(location.search).get('search') || '';
installNavigation(() => dirty() || state.pending || state.uncertain, () => state.pending || state.uncertain, message => notice(message, true));
try {
    await refreshSession();
    if (state.session.configured && !state.session.authenticated) { notice('Sign in to load the World Object catalog.'); $('catalog-count').textContent = 'Sign in required'; }
    else { await Promise.all([loadOptions(), search()]); if (!state.session.configured && !state.session.trusted_home_lan) notice('Local read-only preview.'); }
} catch (error) { notice(`Cannot reach Studio: ${error.message} Refresh the page to reconnect.`, true); $('connection').textContent = 'Disconnected'; }
