// One owner for Environment reads, refresh/retry, partial failures and session display.
// Status requests are bounded and replace only their own refresh generation.
import { createRequest, installNavigation } from './studio-common.js';
const $ = id => document.getElementById(id);
const catalogs = [
    ['items','Items','index'], ['shops','Shops','shops'], ['loot-tables','Loot Tables','loot-tables'],
    ['world-objects','World Objects','world-objects'], ['blacksmithing','Blacksmithing','blacksmithing'],
    ['quests','Quests','quests'], ['dialogue','Dialogue','dialogue'], ['npcs','NPCs','npcs'],
    ['mobs','Mobs','mobs'], ['spells','Spells','spells']
];
const state = { session: {}, revision: 0, controller: null, checks: [], healthLoaded: false, pending: false };
function element(tag, text, className) {
    const node = document.createElement(tag); if (text != null) node.textContent = text;
    if (className) node.className = className; return node;
}
function row(label, value, failed = false) {
    const node = element('div', null, 'environment-row');
    node.append(element('span', label), element('span', value, failed ? 'error' : '')); return node;
}
function message(id, text, failed = false) { $(id).textContent = text; $(id).classList.toggle('error', failed); }
function time() { return new Date().toLocaleTimeString(); }
function updateSession() {
    const session = state.session;
    $('connection').textContent = session.trusted_home_lan ? 'Home LAN · read-only status'
        : !session.configured ? 'Local preview · read only' : session.authenticated ? 'Connected · owner' : 'Sign in required';
    $('session-button').textContent = session.authenticated ? 'Sign out' : 'Sign in';
    $('session-button').hidden = !session.configured || session.trusted_home_lan;
    $('session-button').disabled = state.pending;
    $('access-notice').hidden = !session.trusted_home_lan;
    $('access-notice').textContent = session.read_only
        ? 'Trusted home LAN · no individual sign-in. This host currently permits viewing only.'
        : 'Trusted home LAN · anyone on the allowed network can view, upload, edit, publish and delete content. No individual sign-in. Environment only reads status.';
}
// Never display response bodies as errors: even a proxy failure can contain private details.
async function read(path, signal) {
    const response = await fetch(`/studio/api${path}`, { credentials: 'same-origin', cache: 'no-store', signal });
    if (response.status === 401) throw new Error('Sign in, then refresh to retry.');
    if (!response.ok) throw new Error(response.status === 403 ? 'Access denied by the current host policy.' : `Read unavailable (HTTP ${response.status}). Refresh to retry.`);
    const result = await response.json();
    if (result?.success === false) throw new Error('Read unavailable. Check database/schema status, then refresh.');
    return result && Object.hasOwn(result, 'success') ? result.data : result;
}
function renderHealth(health) {
    const connected = health.database_connected;
    $('health').replaceChildren(
        row('Service', 'MMO Content Studio'), row('Host version', health.host_version),
        row('API version', `${health.api_version} · supported ${health.supported_api_versions.join(', ')}`),
        row('Health', health.overall_status, health.overall_status !== 'Healthy'),
        row('PostgreSQL', connected ? 'Connected' : health.database_status === 'Unconfigured' ? 'Not configured' : 'Unavailable', !connected),
        row('Authoring schema', health.schema_verified ? 'Verified' : connected ? 'Needs attention' : 'Not verified', !health.schema_verified)
    );
    $('roots').replaceChildren(...health.asset_roots.map(root => row(root.label, root.status, root.status !== 'Healthy')));
    state.checks = health.checks; state.healthLoaded = true;
    const failed = health.checks.filter(check => check.status !== 'Healthy').length;
    message('schema-summary', `${health.checks.length} checks · ${failed} needing attention. ${!connected ? 'Database unavailable; schema verification is incomplete.' : failed ? 'Required schema conditions are not met. Review the failed check IDs with the operator.' : 'All registered authoring schema requirements passed.'}`, !health.schema_verified);
    message('health-status', `Read at ${new Date(health.checked_at_utc).toLocaleTimeString()}. ${health.overall_status === 'Healthy' ? 'Database and configured roots are healthy.' : 'Some configured resources need attention.'}`, health.overall_status !== 'Healthy');
    renderChecks();
}
function renderChecks() {
    const search = $('schema-search').value.trim().toLowerCase();
    const checks = state.checks.filter(check => ($('schema-filter').value === 'all' || check.status !== 'Healthy') && check.id.toLowerCase().includes(search));
    $('checks').replaceChildren(...checks.map(check => row(check.id, check.status, check.status !== 'Healthy')));
    if (!checks.length) $('checks').append(element('p', state.healthLoaded ? 'No checks match this view.' : 'Schema checks are not available yet.', 'muted'));
}
function resetPanels() {
    state.checks = []; state.healthLoaded = false; renderChecks();
    for (const id of ['health','roots','assets']) $(id).replaceChildren();
    for (const id of ['health-status','assets-status','schema-summary']) message(id, 'Reading…');
    $('catalogs').replaceChildren();
    for (const [key, title, page] of catalogs) {
        const card = element('div', null, 'environment-catalog');
        const link = element('a', title); link.href = `${page}.html#list`;
        const summary = element('p', 'Reading…', 'muted'); summary.id = `catalog-${key}`;
        card.append(link, summary); $('catalogs').append(card);
    }
}
function markAllUnavailable(text) {
    message('health-status', text, true); message('assets-status', text, true);
    message('schema-summary', 'Schema not verified. ' + text, true);
    for (const [key] of catalogs) message(`catalog-${key}`, 'Unavailable. ' + text, true);
}
async function refresh() {
    const revision = ++state.revision;
    state.controller?.abort();
    const controller = new AbortController(); state.controller = controller;
    const timeout = setTimeout(() => controller.abort(), 45000);
    state.pending = true; $('refresh').disabled = true; $('session-button').disabled = true;
    resetPanels(); message('notice', 'Reading current status…'); $('connection').textContent = 'Connecting…';
    let failures = 0;
    // Each independent read can finish or fail without hiding the others.
    async function panel(path, render, fail) {
        try { const data = await read(path, controller.signal); if (revision === state.revision) render(data); }
        catch (error) {
            if (revision !== state.revision) return;
            failures++;
            fail(controller.signal.aborted ? 'Read timed out. Refresh to retry.' : error instanceof SyntaxError ? 'Invalid status response. Refresh to retry.' : error.message);
        }
    }
    try {
        state.session = await read('/session', controller.signal);
        if (revision !== state.revision) return;
        updateSession();
        if (state.session.configured && !state.session.authenticated && !state.session.trusted_home_lan) {
            markAllUnavailable('Sign in to read status.'); message('notice', 'Sign in, then refresh to read Environment.'); return;
        }
        await Promise.all([
            panel('/environment/health', renderHealth, text => {
                message('health-status', text, true); message('schema-summary', 'Schema not verified. ' + text, true);
                $('roots').replaceChildren(element('p', 'Asset root status unavailable. ' + text, 'error'));
            }),
            panel('/environment/actor-assets', data => {
                $('assets').replaceChildren(...[['rigs','Actor rigs'],['calibrations','Shared calibrations'],['equipped_visuals','Equipped visuals']].map(([key,label]) =>
                    row(label, data[key].available ? `${data[key].count} ${data[key].count === 1 ? 'entry' : 'entries'}` : 'Unavailable', !data[key].available)));
                message('assets-status', `Catalogs read at ${time()}.`);
            }, text => message('assets-status', text, true)),
            ...catalogs.map(([key]) => panel(`/environment/catalog/${key}`, data => {
                message(`catalog-${key}`, `${data.count} ${data.count === 1 ? 'definition' : 'definitions'} · ${data.published} Published · ${data.draft} Draft · ${data.disabled} Disabled${data.other ? ` · ${data.other} other` : ''}. Read at ${time()}.`);
            }, text => message(`catalog-${key}`, 'Unavailable. ' + text, true)))
        ]);
        if (revision === state.revision) message('notice', failures ? `Refresh finished with ${failures} unavailable reads. Use Refresh status to retry.` : `Reads completed at ${time()}. Review resource health below.`, failures > 0);
    } catch {
        if (revision !== state.revision) return;
        $('connection').textContent = 'Disconnected';
        const text = controller.signal.aborted ? 'Connection timed out. Refresh to retry.' : 'Cannot reach Studio. Refresh to retry.';
        markAllUnavailable(text); message('notice', text, true);
    } finally {
        clearTimeout(timeout);
        if (revision === state.revision) { state.pending = false; $('refresh').disabled = false; $('session-button').disabled = false; }
    }
}
const authRequest = createRequest(() => state.session, () => { state.session.authenticated = false; updateSession(); });
$('session-button').addEventListener('click', async () => {
    if (!state.session.authenticated) { $('login-dialog').showModal(); $('password').focus(); return; }
    $('session-button').disabled = true;
    try { await authRequest('/logout', { method: 'POST' }); await refresh(); }
    catch { message('notice', 'Sign out could not be confirmed. Refresh to check the session.', true); }
    finally { $('session-button').disabled = false; }
});
$('cancel-login').addEventListener('click', () => $('login-dialog').close());
$('login-form').addEventListener('submit', async event => {
    event.preventDefault(); const submit = event.submitter; submit.disabled = true; message('login-error', '');
    try {
        state.session = await authRequest('/session');
        await authRequest('/login', { method: 'POST', body: { password: $('password').value } });
        $('password').value = ''; $('login-dialog').close(); await refresh();
    } catch { message('login-error', 'Sign in failed. Check the password and connection, then retry.', true); }
    finally { submit.disabled = false; }
});
$('refresh').addEventListener('click', refresh);
$('schema-search').addEventListener('input', renderChecks);
$('schema-filter').addEventListener('change', renderChecks);
if (!location.hash) history.replaceState(null, '', '#detail');
installNavigation(() => false, () => false, text => message('notice', text));
await refresh();
