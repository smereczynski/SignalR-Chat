
// SSO-first + email OTP fallback login logic for SignalR Chat
(function(){
  // Helpers
  const $ = sel => document.querySelector(sel);
  const on = (el, evt, h, opts) => el && el.addEventListener(evt, h, opts||false);
  function postJson(url, data) {
    return fetch(url, { method:'POST', headers:{'Content-Type':'application/json'}, credentials:'same-origin', body: JSON.stringify(data||{}) });
  }
  function setOtpError(msg) {
    const el = document.getElementById('otpError');
    if (!el) return;
    if (msg) { el.textContent = msg; el.classList.remove('d-none'); }
    else { el.textContent = ''; el.classList.add('d-none'); }
  }

  document.addEventListener('DOMContentLoaded', () => {
    // SSO/Microsoft login button logic
    const msLoginBtn = document.getElementById('btn-microsoft-login');
    if (msLoginBtn) {
      msLoginBtn.addEventListener('click', (e) => {
        e.preventDefault();
        window.location.href = '/login/entra';
      });
    }

    // OTP fallback logic (email input, not dropdown)
    function startOtpSend(isResend){
      setOtpError(null);
      const email = (document.getElementById('otpEmail')?.value || '').trim();
      if (!email) { setOtpError(window.i18n?.emailRequired || 'Email is required'); return; }
      window.__otpFlow = window.__otpFlow || {};
      const flow = window.__otpFlow;
      const container = document.getElementById('otpContainer');
      const resendDelayMs = parseInt(container?.getAttribute('data-otp-resend-delay-ms')||'300000',10);
      if (!isResend) {
        flow.firstSendAt = Date.now();
      } else {
        const sinceFirst = Date.now() - (flow.firstSendAt || 0);
        if (sinceFirst < resendDelayMs) {
          const remainingSeconds = Math.ceil((resendDelayMs - sinceFirst) / 1000);
          const message = window.i18n?.pleaseWaitSeconds?.replace('{0}', remainingSeconds) || `Please wait ${remainingSeconds} seconds before resending`;
          setOtpError(message);
          return;
        }
      }
      const indicator = document.getElementById('otpSendingIndicator');
      const countdownEl = document.getElementById('otpSendCountdown');
      const retryLink = document.getElementById('otpRetryLink');
      const retryCountdown = document.getElementById('otpRetryCountdown');
      const resendBtn = document.getElementById('btn-resend-otp');
      const sendBtn = document.getElementById('btn-send-otp');
      if (!isResend && sendBtn) sendBtn.disabled = true; if (isResend && resendBtn) resendBtn.disabled = true;

      const rawTimeoutMs = parseInt(container?.getAttribute('data-otp-timeout-ms')||'29000',10);
      const countdownTotalMs = 30000;
      const retryCooldownMs = parseInt(container?.getAttribute('data-otp-retry-cooldown-ms')||'5000',10);

      if (indicator) {
        indicator.classList.remove('d-none','fade-hidden');
        indicator.querySelectorAll('[data-state]')?.forEach(n=>n.classList.add('d-none'));
        const sending = indicator.querySelector('[data-state="sending"]');
        if (sending) sending.classList.remove('d-none');
      }
      if (retryLink) { retryLink.classList.add('disabled-link'); retryLink.dataset.cooldownLeft = '0'; }
      if (retryCountdown) retryCountdown.textContent='';

      if (flow.startAbort) { try { flow.startAbort.abort(); } catch(_){} }
      const controller = new AbortController(); flow.startAbort = controller;
      flow.activeEmail = email;
      flow.lastSendStartTs = performance.now(); flow.lastSendCompletedTs = null;

      let remainingMs = countdownTotalMs; if (countdownEl) countdownEl.textContent = Math.ceil(remainingMs/1000);
      if (flow.countdownInterval) clearInterval(flow.countdownInterval);
      flow.countdownInterval = setInterval(()=>{ remainingMs -= 1000; if (remainingMs<=0){ if (countdownEl) countdownEl.textContent='0'; clearInterval(flow.countdownInterval); flow.countdownInterval=null; } else if (countdownEl) countdownEl.textContent=Math.ceil(remainingMs/1000); },1000);
      if (flow.startTimeout) clearTimeout(flow.startTimeout);
      flow.startTimeout = setTimeout(()=>{ if (!controller.signal.aborted){ try { controller.abort(); } catch(_){} } }, rawTimeoutMs);

      postJson('/api/auth/start', { email })
        .then(r => { if (!r.ok) throw new Error(window.i18n?.failedToSendCode || 'Failed to send code'); return r.json().catch(()=>({})); })
        .then(() => {
          if (flow.activeEmail !== email || controller.signal.aborted) return;
          flow.lastSendCompletedTs = performance.now();
          if (!isResend){
            $('#otp-step1')?.classList.add('d-none');
            $('#otp-step2')?.classList.remove('d-none');
            document.getElementById('otpCode')?.focus();
          }
          if (indicator) {
            indicator.querySelectorAll('[data-state]')?.forEach(n=>n.classList.add('d-none'));
            indicator.querySelector('[data-state="success"]')?.classList.remove('d-none');
            setTimeout(()=>indicator.classList.add('fade-hidden'),2000);
            setTimeout(()=>indicator.classList.add('d-none'),2500);
          }
        })
        .catch(e2 => {
          const aborted = controller.signal.aborted;
          if (!aborted) {
            setOtpError(e2.message || window.i18n?.errorSendingCode || 'Error sending code');
            if (indicator) {
              indicator.querySelectorAll('[data-state]')?.forEach(n=>n.classList.add('d-none'));
              indicator.querySelector('[data-state="error"]')?.classList.remove('d-none');
            }
            if (retryLink) {
              let left = retryCooldownMs; retryLink.classList.add('disabled-link'); retryLink.dataset.cooldownLeft = left.toString();
              if (retryCountdown) retryCountdown.textContent = '(' + Math.ceil(left/1000) + 's)';
              if (flow.retryInterval) clearInterval(flow.retryInterval);
              flow.retryInterval = setInterval(()=>{ left -= 1000; if (left<=0){ clearInterval(flow.retryInterval); flow.retryInterval=null; retryLink.classList.remove('disabled-link'); retryLink.dataset.cooldownLeft='0'; if (retryCountdown) retryCountdown.textContent=''; } else { retryLink.dataset.cooldownLeft = left.toString(); if (retryCountdown) retryCountdown.textContent='(' + Math.ceil(left/1000) + 's)'; } },1000);
            }
          }
        })
        .finally(()=>{ if (sendBtn) sendBtn.disabled = false; if (resendBtn) resendBtn.disabled = false; if (flow.startTimeout) { clearTimeout(flow.startTimeout); flow.startTimeout=null; } if (flow.countdownInterval) { clearInterval(flow.countdownInterval); flow.countdownInterval=null; } });
    }

    function executeOtpVerify(){
      setOtpError(null);
      window.__otpFlow = window.__otpFlow || {}; const flow = window.__otpFlow; if (flow.verifyInFlight) return;
      const email = (document.getElementById('otpEmail')?.value || '').trim();
      const code = (document.getElementById('otpCode')?.value || '').trim();
      if (!email || !code) { setOtpError(window.i18n?.emailAndCodeRequired || 'Email and code are required'); return; }
      const btn = document.getElementById('btn-verify-otp'); if (btn) btn.disabled = true; flow.verifyInFlight = true;
      const returnUrl = (typeof window.__returnUrl === 'string' ? window.__returnUrl : '/chat');
      postJson('/api/auth/verify', { email, code, returnUrl })
        .then(r => { if (!r.ok) throw new Error(window.i18n?.invalidVerificationCode || 'Invalid code'); return r.json().catch(()=>({})); })
        .then(body => {
          const next = body && typeof body.nextUrl === 'string' ? body.nextUrl : '/chat';
          if (next.startsWith('/') && !next.startsWith('//')) {
            window.location.href = next;
          } else {
            window.location.href = '/chat';
          }
        })
        .catch(e2 => setOtpError(e2.message || window.i18n?.verificationFailed || 'Verification failed'))
        .finally(()=>{ flow.verifyInFlight = false; if (btn) btn.disabled = false; });
    }

    document.addEventListener('click', e => {
      if (e.target.closest('#btn-send-otp')) { startOtpSend(false); return; }
      if (e.target.closest('#btn-resend-otp')) { startOtpSend(true); return; }
      if (e.target.closest('#btn-verify-otp')) { executeOtpVerify(); return; }
    });

    const otpCodeInput = document.getElementById('otpCode');
    if (otpCodeInput) {
      otpCodeInput.addEventListener('keydown', ev => { if (ev.key === 'Enter') { ev.preventDefault(); executeOtpVerify(); } });
    }
  });
})();
