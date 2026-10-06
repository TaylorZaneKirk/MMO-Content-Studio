// Small transport and page-navigation helpers; each workspace owns its editor workflow.
export function createRequest(getSession, onUnauthorized) {
return async function request(path, { method = 'GET', body, raw = false } = {}) {
    const response = await fetch(`/studio/api${path}`, { method, credentials: 'same-origin', cache: 'no-store',
        headers: { ...(body !== undefined ? { 'Content-Type': raw ? 'image/png' : 'application/json' } : {}),
            ...(method !== 'GET' ? { 'X-Studio-CSRF': getSession().csrf_token || '' } : {}) },
        body: body === undefined ? undefined : raw ? body : JSON.stringify(body) });
    if (response.status === 204) return null;
    let data; try { data = await response.json(); } catch { data = null; }
    if (!response.ok || data?.success === false) {
        const error = new Error(data?.errors?.map(e => e.message).join(' ') || `Request failed (${response.status}).`);
        error.status = response.status; error.errors = data?.errors || [];
        if (response.status === 401) { onUnauthorized(); }
        throw error;
    }
    return data && Object.hasOwn(data, 'success') ? data.data : data;
}
}

// Hash navigation only changes the phone pane: drafts are retained on Back/Forward.
// Full-document links and browser Back out of the page use the native unload guard.
export function installNavigation(hasWork, isBlocked, report) {
    const route = () => {
        const view = location.hash.slice(1);
        document.body.dataset.view = ['workspace', 'list', 'detail'].includes(view) ? view : 'workspace';
    };
    window.addEventListener('hashchange', route);
    window.addEventListener('pageshow', route);
    window.addEventListener('beforeunload', event => {
        if (hasWork()) { event.preventDefault(); event.returnValue = ''; }
    });
    document.addEventListener('click', event => {
        const link = event.target.closest('a[href]');
        if (!link || link.origin !== location.origin || link.pathname === location.pathname) return;
        if (isBlocked()) { event.preventDefault(); report('Finish or reconcile the current request before changing workspaces.'); }
    });
    function updateKeyboardActions() {
        const editingOnPhone = matchMedia('(max-width: 800px)').matches && ['INPUT', 'SELECT', 'TEXTAREA'].includes(document.activeElement?.tagName);
        const keyboardShrankViewport = window.visualViewport && window.innerHeight - window.visualViewport.height > 120;
        document.body.classList.toggle('keyboard-open', editingOnPhone || keyboardShrankViewport);
    }
    window.visualViewport?.addEventListener('resize', updateKeyboardActions);
    document.addEventListener('focusin', updateKeyboardActions);
    document.addEventListener('focusout', () => queueMicrotask(updateKeyboardActions));
    route();
}
