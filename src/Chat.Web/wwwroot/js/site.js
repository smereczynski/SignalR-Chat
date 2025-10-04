// Lightweight frontend logger with trace correlation (jQuery-free)
(function(){
    if (window.appLogger) return; // singleton
    let currentTraceId = null;
    function setTraceId(id){ currentTraceId = id; }
    function fmt(level, msg, extra){
        const base = { level, msg, traceId: currentTraceId, ts: new Date().toISOString() };
        if (extra) { Object.keys(extra).forEach(k => base[k]=extra[k]); }
        return base;
    }
    function log(level, msg, extra){
        const payload = fmt(level, msg, extra);
        let line = '['+payload.level+']';
        if (payload.traceId) line += '['+payload.traceId+']';
        line += ' ' + payload.msg;
        if (level === 'error') console.error(line, extra||''); else if (level==='warn') console.warn(line, extra||''); else console.log(line, extra||'');
        return payload;
    }
    const originalFetch = window.fetch;
    window.fetch = function(input, init){
        return originalFetch(input, init).then(resp => { try { const hdr = resp.headers.get('X-Trace-Id'); if (hdr) setTraceId(hdr);} catch(_){} return resp; });
    };
    window.appLogger = {
        info: (m,e)=>log('info',m,e),
        warn: (m,e)=>log('warn',m,e),
        error: (m,e)=>log('error',m,e),
        setTraceId,
        getTraceId: ()=>currentTraceId
    };
})();

document.addEventListener('DOMContentLoaded', () => {
    appLogger.info('site.js init (vanilla)');

    // Helper short-hands
    const $ = sel => document.querySelector(sel);
    const $$ = sel => Array.from(document.querySelectorAll(sel));
    const on = (el, evt, h, opts) => el && el.addEventListener(evt, h, opts||false);

    // User list private message insertion (event delegation)
    const usersList = document.getElementById('users-list');
    if (usersList) {
        usersList.addEventListener('click', e => {
            const li = e.target.closest('li');
            if (!li || !usersList.contains(li)) return;
            const username = li.getAttribute('data-username');
            const input = document.getElementById('message-input');
            if (!username || !input) return;
            let text = input.value || '';
            if (text.startsWith('/')) {
                const parts = text.split(')');
                if (parts.length > 1) text = parts.slice(1).join(')');
            }
            text = '/private(' + username + ') ' + text.trim();
            input.value = text;
            input.dispatchEvent(new Event('change'));
            input.focus();
        });
    }

    // Sidebar / users panel toggles
    on($('#expand-sidebar'), 'click', () => {
        document.querySelectorAll('.sidebar').forEach(s => s.classList.toggle('open'));
        document.querySelectorAll('.users-container').forEach(u => u.classList.remove('open'));
    });
    on($('#expand-users-list'), 'click', () => {
        document.querySelectorAll('.users-container').forEach(u => u.classList.toggle('open'));
        document.querySelectorAll('.sidebar').forEach(s => s.classList.remove('open'));
    });
    document.addEventListener('click', e => {
        if (e.target.closest('.sidebar.open ul li a') || e.target.closest('#users-list li')) {
            document.querySelectorAll('.sidebar, .users-container').forEach(el => el.classList.remove('open'));
        }
    });

    // Modal focus / clearing (Bootstrap 5 events)
    $$('.modal').forEach(modalEl => {
        on(modalEl, 'shown.bs.modal', () => {
            const input = modalEl.querySelector('input[type=text]:first-child');
            if (input) input.focus();
        });
        on(modalEl, 'hidden.bs.modal', () => {
            modalEl.querySelectorAll('.modal-body input:not(#newRoomName)').forEach(i => i.value = '');
        });
    });

    // Alert close buttons
    document.addEventListener('click', e => {
        const btn = e.target.closest('.alert .btn-close');
        if (btn) {
            const parent = btn.closest('.alert');
            if (parent) parent.style.display = 'none';
        }
    });

    // Tooltips init (Bootstrap)
    if (window.bootstrap && bootstrap.Tooltip) {
        $$("[data-bs-toggle='tooltip']").forEach(el => new bootstrap.Tooltip(el, { delay: { show: 500 } }));
    }

    // Remove message modal id capture
    const removeMsgModal = document.getElementById('remove-message-modal');
    if (removeMsgModal) {
        on(removeMsgModal, 'shown.bs.modal', ev => {
            const trigger = ev.relatedTarget;
            if (trigger) {
                const id = trigger.getAttribute('data-messageId');
                const input = document.getElementById('itemToDelete');
                if (id && input) input.value = id;
            }
        });
    }

    // Hover actions for own messages (mouseenter/mouseleave simulation)
    document.addEventListener('mouseover', e => {
        const mine = e.target.closest('.ismine');
        if (mine) {
            const actions = mine.querySelector('.actions');
            if (actions) actions.classList.remove('d-none');
        }
    });
    document.addEventListener('mouseout', e => {
        const mine = e.target.closest('.ismine');
        if (mine && !mine.querySelector('.dropdown-menu.show')) {
            const actions = mine.querySelector('.actions');
            if (actions) actions.classList.add('d-none');
        }
    });
    document.addEventListener('hidden.bs.dropdown', e => {
        const dropdown = e.target.closest('.actions .dropdown');
        if (dropdown) {
            const actions = dropdown.closest('.actions');
            if (actions) actions.classList.add('d-none');
        }
    });

    // OTP helpers
    function postJson(url, data) {
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify(data || {})
        });
    }
    function setOtpError(msg) {
        const el = document.getElementById('otpError');
        if (!el) return;
        if (msg) { el.textContent = msg; el.classList.remove('d-none'); }
        else { el.textContent = ''; el.classList.add('d-none'); }
    }

    document.addEventListener('click', e => {
        const sendBtn = e.target.closest('#btn-send-otp');
        if (sendBtn) {
            setOtpError(null);
            const userName = (document.getElementById('otpUserName')?.value || '').trim();
            const destination = (document.getElementById('otpDestination')?.value || '').trim();
            if (!userName) { setOtpError('Username is required'); return; }
            postJson('/api/auth/start', { userName, destination: destination || null })
                .then(r => { if (!r.ok) throw new Error('Failed to send code'); return r.json().catch(()=>({})); })
                .then(() => {
                    appLogger.info('OTP code sent', { user: userName, dest: destination || null });
                    $('#otp-step1')?.classList.add('d-none');
                    $('#otp-step2')?.classList.remove('d-none');
                    $('#otpCode')?.focus();
                })
                .catch(e2 => { appLogger.error('OTP send failed', { error: e2.message }); setOtpError(e2.message || 'Error sending code'); });
            return;
        }
        const verifyBtn = e.target.closest('#btn-verify-otp');
        if (verifyBtn) {
            setOtpError(null);
            const userName = (document.getElementById('otpUserName')?.value || '').trim();
            const code = (document.getElementById('otpCode')?.value || '').trim();
            if (!userName || !code) { setOtpError('Username and code are required'); return; }
            postJson('/api/auth/verify', { userName, code })
                .then(r => { if (!r.ok) throw new Error('Invalid code'); return r.json().catch(()=>({})); })
                .then(() => {
                    appLogger.info('OTP verify success', { user: userName });
                    const modalEl = document.getElementById('otp-login-modal');
                    if (modalEl && window.bootstrap) {
                        try {
                            (bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl)).hide();
                        } catch(_){}
                    }
                    if (window.chatApp && typeof window.chatApp.onAuthenticated === 'function') {
                        window.chatApp.onAuthenticated();
                    } else {
                        appLogger.warn('chatApp.onAuthenticated not available');
                    }
                })
                .catch(e2 => { appLogger.error('OTP verify failed', { user: userName, error: e2.message }); setOtpError(e2.message || 'Verification failed'); });
            return;
        }
        const logoutBtn = e.target.closest('#btn-logout');
        if (logoutBtn) {
            postJson('/api/auth/logout', {})
                .then(() => {
                    appLogger.info('Logout success');
                    if (window.chatApp?.logoutCleanup) window.chatApp.logoutCleanup();
                })
                .catch(() => {
                    appLogger.warn('Logout request error');
                    if (window.chatApp?.logoutCleanup) window.chatApp.logoutCleanup();
                });
            return;
        }
    });

    const otpLoginModal = document.getElementById('otp-login-modal');
    if (otpLoginModal) {
        on(otpLoginModal, 'show.bs.modal', () => {
            setOtpError(null);
            $('#otp-step1')?.classList.remove('d-none');
            $('#otp-step2')?.classList.add('d-none');
            const code = $('#otpCode'); if (code) code.value='';
        });
    }
});