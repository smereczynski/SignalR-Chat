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

    // Private message feature removed: clicking users no longer injects /private command.

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

    // Reusable OTP verify logic so it can be triggered by button click or Enter key
    function executeOtpVerify() {
        setOtpError(null);
        window.__otpFlow = window.__otpFlow || {};
        const flow = window.__otpFlow;
        if (flow.verifyInFlight) return; // prevent double-submit
        const userName = (document.getElementById('otpUserName')?.value || '').trim();
        const code = (document.getElementById('otpCode')?.value || '').trim();
        if (!userName || !code) { setOtpError('User and code are required'); return; }
        const verifyBtn = document.getElementById('btn-verify-otp');
        if (verifyBtn) verifyBtn.disabled = true;
        flow.verifyInFlight = true;
        const verifyStart = performance.now();
        postJson('/api/auth/verify', { userName, code })
            .then(r => { if (!r.ok) throw new Error('Invalid code'); return r.json().catch(()=>({})); })
            .then(() => {
                appLogger.info('OTP verify success', { user: userName });
                if (flow.lastSendId) {
                    const verifyLatencyMs = Math.round(performance.now() - (flow.lastSendCompletedTs || flow.lastSendStartTs || verifyStart));
                    appLogger.info('OTP verify latency', { user: userName, sendId: flow.lastSendId, verifyLatencyMs });
                }
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
            .catch(e2 => { appLogger.error('OTP verify failed', { user: userName, error: e2.message }); setOtpError(e2.message || 'Verification failed'); })
            .finally(() => { flow.verifyInFlight = false; if (verifyBtn) verifyBtn.disabled = false; });
    }

    // Unified OTP send logic with timeout, countdown, cooldown, resend, telemetry
    function startOtpSend(options){
        options = options || {};
        const isResend = !!options.resend;
        setOtpError(null);
        const userName = (document.getElementById('otpUserName')?.value || '').trim();
        if (!userName) { setOtpError('User selection is required'); return; }
        window.__otpFlow = window.__otpFlow || {};
        const flow = window.__otpFlow;
        const sendBtn = document.getElementById('btn-send-otp');
        if (sendBtn && sendBtn.disabled && !isResend) return; // prevent duplicate primary sends
        const resendBtn = document.getElementById('btn-resend-otp');
        // Acquire container first, then read configured attributes from it
        const container = document.getElementById('otpContainer');
        // Enforce resend delay window (default 5 min via data-otp-resend-delay-ms)
        const resendDelayMs = parseInt(container?.getAttribute('data-otp-resend-delay-ms')||'300000',10);
        flow.firstSendAt = flow.firstSendAt || 0;
        const nowTs = Date.now();
        if (!isResend) {
            // mark the first send timestamp
            flow.firstSendAt = nowTs;
        } else {
            const sinceFirst = nowTs - (flow.firstSendAt || 0);
            if (sinceFirst < resendDelayMs) {
                // ignore early resend attempts
                return;
            }
        }

        // Config
    // Backend/network hard timeout (29s) while UI shows a full 30s countdown for cleaner UX
    const rawTimeoutMs = parseInt(container?.getAttribute('data-otp-timeout-ms')||'29000',10);
    const timeoutMs = rawTimeoutMs; // abort / fetch timeout threshold
    const countdownTotalMs = 30000; // always show 30s countdown visually
        const retryCooldownMs = parseInt(container?.getAttribute('data-otp-retry-cooldown-ms')||'5000',10);

        // UI state elements
        const indicator = document.getElementById('otpSendingIndicator');
        const countdownEl = document.getElementById('otpSendCountdown');
        const retryLink = document.getElementById('otpRetryLink');
        const retryCountdown = document.getElementById('otpRetryCountdown');

        // Disable relevant buttons
    if (!isResend && sendBtn) sendBtn.disabled = true;
    if (isResend && resendBtn) resendBtn.disabled = true;

        // Reset indicator to sending
        if (indicator) {
            indicator.classList.remove('d-none','fade-hidden');
            indicator.querySelectorAll('[data-state]')?.forEach(n=>n.classList.add('d-none'));
            const sending = indicator.querySelector('[data-state="sending"]');
            if (sending) sending.classList.remove('d-none');
        }
        if (retryLink) {
            retryLink.classList.add('disabled-link');
            retryLink.dataset.cooldownLeft = '0';
        }
        if (retryCountdown) retryCountdown.textContent='';

        // Abort existing send
        if (flow.startAbort) { try { flow.startAbort.abort(); } catch(_){} }
        const controller = new AbortController();
        flow.startAbort = controller;
        flow.activeUser = userName;
        const sendId = 'send_' + Date.now() + '_' + Math.random().toString(36).slice(2,8);
        flow.lastSendId = sendId;
        flow.lastSendStartTs = performance.now();
        flow.lastSendCompletedTs = null;

        // Timeout countdown
    let remainingMs = countdownTotalMs;
        if (countdownEl) countdownEl.textContent = Math.ceil(remainingMs/1000);
        if (flow.countdownInterval) { clearInterval(flow.countdownInterval); }
        flow.countdownInterval = setInterval(()=>{
            remainingMs -= 1000;
            if (remainingMs <= 0){
                if (countdownEl) countdownEl.textContent = '0';
                clearInterval(flow.countdownInterval); flow.countdownInterval=null;
            } else if (countdownEl) countdownEl.textContent = Math.ceil(remainingMs/1000);
        },1000);

        // Hard timeout
        if (flow.startTimeout) { clearTimeout(flow.startTimeout); }
        flow.startTimeout = setTimeout(()=>{
            if (!controller.signal.aborted) {
                appLogger.warn('OTP send timeout', { user: userName, timeoutMs, sendId });
                try { controller.abort(); } catch(_){}
            }
        }, timeoutMs);

        fetch('/api/auth/start', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ userName }), credentials:'same-origin', signal: controller.signal })
            .then(r => { if (!r.ok) throw new Error('Failed to send code'); return r.json().catch(()=>({})); })
            .then(() => {
                if (flow.activeUser !== userName || controller.signal.aborted) return;
                flow.lastSendCompletedTs = performance.now();
                const durMs = Math.round(flow.lastSendCompletedTs - flow.lastSendStartTs);
                appLogger.info('OTP code sent', { user: userName, durationMs: durMs, sendId });
                if (indicator) {
                    indicator.querySelectorAll('[data-state]')?.forEach(n=>n.classList.add('d-none'));
                    const success = indicator.querySelector('[data-state="success"]');
                    if (success) success.classList.remove('d-none');
                    setTimeout(()=>{ indicator.classList.add('fade-hidden'); }, 2000);
                    setTimeout(()=>{ indicator.classList.add('d-none'); }, 2500);
                }
                // Move to step2 only on initial send, keep on step2 for resend
                if (!isResend){
                    const step1 = $('#otp-step1');
                    const step2 = $('#otp-step2');
                    if (step1 && step2) { step1.classList.add('d-none'); step2.classList.remove('d-none'); }
                    const codeEl = $('#otpCode'); if (codeEl) codeEl.focus();
                    // Start 5-minute resend availability countdown on button
                    const rb = document.getElementById('btn-resend-otp');
                    if (rb) {
                        rb.disabled = true;
                        const endAt = (flow.firstSendAt || Date.now()) + resendDelayMs;
                        if (flow.resendAvailInterval) clearInterval(flow.resendAvailInterval);
                        const updateLabel = ()=>{
                            const left = endAt - Date.now();
                            if (left <= 0){
                                clearInterval(flow.resendAvailInterval); flow.resendAvailInterval=null;
                                rb.disabled = false; rb.textContent = 'Resend';
                            } else {
                                const s = Math.ceil(left/1000);
                                rb.textContent = 'Resend in ' + s + 's';
                            }
                        };
                        updateLabel();
                        flow.resendAvailInterval = setInterval(updateLabel, 1000);
                    }
                }
            })
            .catch(e2 => {
                const aborted = controller.signal.aborted;
                const now = performance.now();
                const durMs = Math.round(now - flow.lastSendStartTs);
                if (aborted){
                    appLogger.warn('OTP send aborted', { user: userName, durationMs: durMs, sendId });
                } else {
                    appLogger.error('OTP send failed', { error: e2.message, user: userName, durationMs: durMs, sendId });
                    setOtpError(e2.message || 'Error sending code');
                }
                if (indicator) {
                    indicator.querySelectorAll('[data-state]')?.forEach(n=>n.classList.add('d-none'));
                    if (!aborted) {
                        const errNode = indicator.querySelector('[data-state="error"]');
                        if (errNode) errNode.classList.remove('d-none');
                        // Start retry cooldown
                        if (retryLink) {
                            let left = retryCooldownMs;
                            retryLink.classList.add('disabled-link');
                            retryLink.dataset.cooldownLeft = left.toString();
                            if (retryCountdown) retryCountdown.textContent = '(' + Math.ceil(left/1000) + 's)';
                            if (flow.retryInterval) clearInterval(flow.retryInterval);
                            flow.retryInterval = setInterval(()=>{
                                left -= 1000;
                                if (left <= 0){
                                    clearInterval(flow.retryInterval); flow.retryInterval=null;
                                    retryLink.classList.remove('disabled-link');
                                    retryLink.dataset.cooldownLeft = '0';
                                    if (retryCountdown) retryCountdown.textContent='';
                                } else {
                                    retryLink.dataset.cooldownLeft = left.toString();
                                    if (retryCountdown) retryCountdown.textContent='(' + Math.ceil(left/1000) + 's)';
                                }
                            },1000);
                        }
                    } else {
                        indicator.classList.add('fade-hidden');
                        setTimeout(()=>indicator.classList.add('d-none'),250);
                    }
                }
            })
            .finally(()=>{
                if (sendBtn) sendBtn.disabled = false;
                if (resendBtn) resendBtn.disabled = false;
                if (flow.startTimeout) { clearTimeout(flow.startTimeout); flow.startTimeout=null; }
                if (flow.countdownInterval) { clearInterval(flow.countdownInterval); flow.countdownInterval=null; }
            });
    }

    document.addEventListener('click', e => {
        const sendBtn = e.target.closest('#btn-send-otp');
        if (sendBtn) { startOtpSend({ resend:false }); return; }
        const resendBtn = e.target.closest('#btn-resend-otp');
        if (resendBtn) { startOtpSend({ resend:true }); return; }
        const verifyBtn = e.target.closest('#btn-verify-otp');
        if (verifyBtn) {
            executeOtpVerify();
            return;
        }
        const logoutBtn = e.target.closest('#btn-logout');
        if (logoutBtn) {
            postJson('/api/auth/logout', {})
                .then(() => { appLogger.info('Logout success'); })
                .catch(() => { appLogger.warn('Logout request error'); })
                .finally(() => { if (window.chatApp?.logoutCleanup) window.chatApp.logoutCleanup(); window.location.replace('/login?ReturnUrl=/chat'); });
            return;
        }
    });

    // Retry link handler (event delegation)
    document.addEventListener('click', ev => {
        const retry = ev.target.closest('#otpRetryLink');
        if (retry) {
            ev.preventDefault();
            if (retry.classList.contains('disabled-link')) return;
            startOtpSend({ resend:false });
        }
    });

    // Attach Enter key handler for OTP code input to trigger verification
    const otpCodeInput = document.getElementById('otpCode');
    if (otpCodeInput) {
        otpCodeInput.addEventListener('keydown', ev => {
            if (ev.key === 'Enter') {
                ev.preventDefault();
                executeOtpVerify();
            }
        });
    }

    const otpLoginModal = document.getElementById('otp-login-modal');
    if (otpLoginModal) {
        on(otpLoginModal, 'show.bs.modal', () => {
            setOtpError(null);
            const flow = (window.__otpFlow = window.__otpFlow || {});
            flow.activeUser = null; // reset active user context
            $('#otp-step1')?.classList.remove('d-none');
            $('#otp-step2')?.classList.add('d-none');
            const code = $('#otpCode'); if (code) code.value='';
            const sel = document.getElementById('otpUserName');
            if (sel && sel.dataset.loaded !== 'true') {
                fetch('/api/auth/users', { credentials: 'same-origin' })
                    .then(r => { if (!r.ok) throw new Error('Failed to load users'); return r.json(); })
                    .then(users => {
                        sel.innerHTML = '<option value="" disabled selected>Select user...</option>' + users.map(u => `<option value="${u.userName}">${u.fullName || u.userName}</option>`).join('');
                        sel.dataset.loaded = 'true';
                    })
                    .catch(err => { setOtpError(err.message); });
            }
        });
        on(otpLoginModal, 'hide.bs.modal', () => {
            const flow = window.__otpFlow;
            if (flow?.startAbort) { try { flow.startAbort.abort(); } catch(_){} }
            if (flow) { flow.activeUser = null; }
            // Reset steps
            $('#otp-step1')?.classList.remove('d-none');
            $('#otp-step2')?.classList.add('d-none');
            setOtpError(null);
            // Safeguard: ensure send button is enabled after closing modal (handles early abort before response)
            const sendBtn = document.getElementById('btn-send-otp');
            if (sendBtn) sendBtn.disabled = false;
            const ind = document.getElementById('otpSendingIndicator');
            if (ind) {
                ind.classList.add('d-none');
                ind.classList.remove('fade-hidden');
                ind.querySelectorAll('[data-state]')?.forEach(n=>n.classList.add('d-none'));
            }
            const resendBtn = document.getElementById('btn-resend-otp');
            if (resendBtn) resendBtn.disabled = false;
            if (flow.startTimeout) { clearTimeout(flow.startTimeout); flow.startTimeout=null; }
            if (flow.countdownInterval) { clearInterval(flow.countdownInterval); flow.countdownInterval=null; }
            if (flow.retryInterval) { clearInterval(flow.retryInterval); flow.retryInterval=null; }
            const countdownEl = document.getElementById('otpSendCountdown'); if (countdownEl) countdownEl.textContent='0';
            const retryCountdown = document.getElementById('otpRetryCountdown'); if (retryCountdown) retryCountdown.textContent='';
            const retryLink = document.getElementById('otpRetryLink'); if (retryLink) { retryLink.classList.add('disabled-link'); retryLink.dataset.cooldownLeft='0'; }
        });
    }
});