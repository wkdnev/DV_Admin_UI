// Theme toggle – persists choice in localStorage
window.themeToggle = {
    toggle: function () {
        var html = document.documentElement;
        var current = html.getAttribute('data-theme');
        var next = current === 'light' ? 'dark' : 'light';
        if (next === 'dark') {
            html.removeAttribute('data-theme');
        } else {
            html.setAttribute('data-theme', 'light');
        }
        localStorage.setItem('dv-theme', next);
        return next;
    },
    get: function () {
        var theme = localStorage.getItem('dv-theme') || 'dark';
        themeToggle.apply(theme);
        return theme;
    },
    apply: function (theme) {
        if (theme === 'light') {
            document.documentElement.setAttribute('data-theme', 'light');
        } else {
            document.documentElement.removeAttribute('data-theme');
        }
    }
};

// Apply theme immediately on script load (covers full-page navigations)
themeToggle.apply(localStorage.getItem('dv-theme') || 'dark');

// ── Blazor enhanced-navigation guards ──────────────────────────
// Blazor's enhanced navigation fetches new pages and patches the DOM,
// which strips the data-theme attribute from <html> because the server
// response always renders <html lang="en"> with no data-theme.
// Multiple fallbacks ensure the theme is always restored.

// 1) MutationObserver – fires the instant Blazor touches <html> attributes.
//    This is the primary guard; it reacts before any Blazor event fires.
(function () {
    new MutationObserver(function () {
        var wanted = localStorage.getItem('dv-theme') || 'dark';
        var current = document.documentElement.getAttribute('data-theme');
        if (wanted === 'light' && current !== 'light') {
            document.documentElement.setAttribute('data-theme', 'light');
        } else if (wanted === 'dark' && current !== null) {
            document.documentElement.removeAttribute('data-theme');
        }
    }).observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['data-theme']
    });
})();

// 2) Blazor API event (available after blazor.web.js initialises)
try {
    if (typeof Blazor !== 'undefined' && Blazor.addEventListener) {
        Blazor.addEventListener('enhancedload', function () {
            themeToggle.apply(localStorage.getItem('dv-theme') || 'dark');
        });
    }
} catch (_) { /* Blazor not ready yet – observer handles it */ }

// 3) Standard DOM event (belt-and-suspenders for .NET 8 builds that fire it)
document.addEventListener('blazor:enhanced-load', function () {
    themeToggle.apply(localStorage.getItem('dv-theme') || 'dark');
});

// 4) Back/forward cache
window.addEventListener('pageshow', function (e) {
    if (e.persisted) {
        themeToggle.apply(localStorage.getItem('dv-theme') || 'dark');
    }
});
