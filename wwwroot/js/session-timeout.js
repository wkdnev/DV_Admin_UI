/**
 * Session Timeout Manager — NIST SP 800-53 Rev 5 Compliance
 *
 * Implements:
 *   AC-11     Device/Session Lock — locks the screen after inactivity
 *   AC-11(01) Pattern-Hiding Displays — obscures page content during lock
 *   AC-12(03) Timeout Warning Message — warns user before session expires
 *
 * Configuration is injected via data attributes on the script tag or
 * a global window.dvSessionConfig object set by the server.
 */
(function () {
    'use strict';

    // --- Configuration (minutes) ---
    var config = window.dvSessionConfig || {};
    var IDLE_TIMEOUT   = (config.idleTimeoutMinutes   || 30) * 60 * 1000; // ms
    var WARNING_BEFORE = (config.warningMinutes        || 5)  * 60 * 1000; // ms
    var LOCK_AFTER     = (config.lockMinutes           || 25) * 60 * 1000; // ms
    var EXPIRED_URL    = config.expiredUrl    || '/auth/session-expired';
    var KEEPALIVE_URL  = config.keepaliveUrl  || '/';

    var warningTime  = IDLE_TIMEOUT - WARNING_BEFORE;
    var idleTimer    = null;
    var warningTimer = null;
    var lockTimer    = null;
    var countdownInterval = null;
    var isLocked     = false;
    var isWarning    = false;

    // ---- UI Elements (created once) ----

    function createOverlay() {
        // Lock screen overlay (AC-11(01): pattern-hiding)
        var overlay = document.createElement('div');
        overlay.id = 'dv-session-lock';
        overlay.style.cssText =
            'display:none;position:fixed;top:0;left:0;width:100%;height:100%;' +
            'background:rgba(0,0,0,0.92);z-index:999999;' +
            'align-items:center;justify-content:center;flex-direction:column;' +
            'font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif;';
        overlay.innerHTML =
            '<div style="text-align:center;color:white;max-width:420px;padding:40px;">' +
            '  <div style="font-size:4rem;margin-bottom:20px;"><i class="bi bi-shield-lock" style="color:#ffc107;"></i></div>' +
            '  <h2 id="dv-lock-title" style="color:white;margin-bottom:10px;">Session Locked</h2>' +
            '  <p id="dv-lock-message" style="color:#ccc;font-size:1rem;margin-bottom:8px;">' +
            '    Your session has been locked due to inactivity.</p>' +
            '  <p id="dv-lock-countdown" style="color:#ffc107;font-size:1.2rem;font-weight:600;margin-bottom:25px;"></p>' +
            '  <button id="dv-lock-extend" style="' +
            '    background:#667eea;color:white;border:none;padding:12px 32px;' +
            '    border-radius:8px;font-size:1rem;cursor:pointer;margin-right:10px;' +
            '    transition:background 0.2s;"' +
            '    onmouseover="this.style.background=\'#5a6fd6\'" onmouseout="this.style.background=\'#667eea\'">' +
            '    <i class="bi bi-arrow-counterclockwise me-1"></i> Continue Session</button>' +
            '  <button id="dv-lock-logout" style="' +
            '    background:transparent;color:#ccc;border:1px solid #666;padding:12px 32px;' +
            '    border-radius:8px;font-size:1rem;cursor:pointer;' +
            '    transition:background 0.2s;"' +
            '    onmouseover="this.style.background=\'rgba(255,255,255,0.1)\'" onmouseout="this.style.background=\'transparent\'">' +
            '    <i class="bi bi-box-arrow-right me-1"></i> Sign Out</button>' +
            '</div>';
        document.body.appendChild(overlay);

        document.getElementById('dv-lock-extend').addEventListener('click', extendSession);
        document.getElementById('dv-lock-logout').addEventListener('click', function () {
            window.location.href = '/logout';
        });

        return overlay;
    }

    // ---- Timer Management ----

    function resetTimers() {
        clearTimeout(idleTimer);
        clearTimeout(warningTimer);
        clearTimeout(lockTimer);
        clearInterval(countdownInterval);

        if (isLocked || isWarning) {
            hideLockScreen();
        }

        // Warning timer: fires at (idleTimeout - warningMinutes)
        warningTimer = setTimeout(showWarning, warningTime);

        // Lock timer: fires at lockMinutes
        lockTimer = setTimeout(showLockScreen, LOCK_AFTER);

        // Hard timeout: redirect to session-expired
        idleTimer = setTimeout(expireSession, IDLE_TIMEOUT);
    }

    function showWarning() {
        if (isLocked) return; // Already showing lock
        isWarning = true;

        var overlay = document.getElementById('dv-session-lock') || createOverlay();
        var title = document.getElementById('dv-lock-title');
        var msg = document.getElementById('dv-lock-message');

        title.textContent = 'Session Timeout Warning';
        msg.textContent = 'Your session will expire soon due to inactivity.';
        overlay.style.display = 'flex';
        overlay.style.background = 'rgba(0,0,0,0.75)';

        startCountdown(WARNING_BEFORE);
    }

    function showLockScreen() {
        isLocked = true;
        isWarning = false;

        var overlay = document.getElementById('dv-session-lock') || createOverlay();
        var title = document.getElementById('dv-lock-title');
        var msg = document.getElementById('dv-lock-message');

        title.textContent = 'Session Locked';
        msg.textContent = 'Your session has been locked due to inactivity.';
        overlay.style.display = 'flex';
        overlay.style.background = 'rgba(0,0,0,0.92)';

        var remaining = IDLE_TIMEOUT - LOCK_AFTER;
        startCountdown(remaining);
    }

    function hideLockScreen() {
        var overlay = document.getElementById('dv-session-lock');
        if (overlay) overlay.style.display = 'none';
        isLocked = false;
        isWarning = false;
        clearInterval(countdownInterval);
    }

    function startCountdown(durationMs) {
        var countdownEl = document.getElementById('dv-lock-countdown');
        if (!countdownEl) return;

        var remaining = Math.ceil(durationMs / 1000);
        clearInterval(countdownInterval);

        function update() {
            var mins = Math.floor(remaining / 60);
            var secs = remaining % 60;
            countdownEl.textContent = 'Session expires in ' + mins + ':' + (secs < 10 ? '0' : '') + secs;
            if (remaining <= 0) {
                clearInterval(countdownInterval);
                expireSession();
            }
            remaining--;
        }

        update();
        countdownInterval = setInterval(update, 1000);
    }

    function extendSession() {
        // Touch the server to reset the server-side sliding timeout
        fetch(KEEPALIVE_URL, { method: 'HEAD', credentials: 'same-origin' })
            .catch(function () { /* ignore network errors */ });

        resetTimers();
    }

    function expireSession() {
        clearTimeout(idleTimer);
        clearTimeout(warningTimer);
        clearTimeout(lockTimer);
        clearInterval(countdownInterval);
        window.location.href = EXPIRED_URL;
    }

    // ---- Activity Detection ----

    var activityEvents = ['mousedown', 'mousemove', 'keydown', 'scroll', 'touchstart', 'click'];
    var lastActivity = Date.now();
    var THROTTLE_MS = 5000; // Only reset timers every 5 seconds of activity

    function onActivity() {
        var now = Date.now();
        if (now - lastActivity < THROTTLE_MS) return;
        lastActivity = now;

        // Only reset if user is not on the lock screen.
        // If on lock screen, they must click "Continue Session".
        if (!isLocked) {
            resetTimers();
        }
    }

    // ---- Initialisation ----

    function init() {
        // Don't run on login/logout/session-expired pages
        var path = window.location.pathname.toLowerCase();
        if (path.indexOf('/auth/') === 0 || path.indexOf('/login') === 0 ||
            path.indexOf('/logout') === 0 || path.indexOf('/session-expired') === 0) {
            return;
        }

        // Create the overlay element
        createOverlay();

        // Attach activity listeners
        for (var i = 0; i < activityEvents.length; i++) {
            document.addEventListener(activityEvents[i], onActivity, { passive: true });
        }

        // Start timers
        resetTimers();
    }

    // Run when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
