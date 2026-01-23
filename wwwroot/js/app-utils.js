// ============================================================================
// app-utils.js - Utility functions for DocViewer_Proto Blazor application
// ============================================================================

// Check if the current window is inside an iframe
window.isInIFrame = function () {
    try {
        return window.self !== window.top;
    } catch (e) {
        return true;
    }
};

// Handle CSS loading errors gracefully
window.handleCssLoadError = function () {
    // Remove broken CSS links to prevent 404 errors in console
    const cssLinks = document.querySelectorAll('link[rel="stylesheet"]');
    cssLinks.forEach(link => {
        link.onerror = function() {
            console.warn('CSS file not found:', this.href);
            // Optionally remove the link to prevent repeated 404s
            // this.remove();
        };
    });
};

// Initialize utilities when DOM is loaded
document.addEventListener('DOMContentLoaded', function() {
    handleCssLoadError();
});

// ============================================================================
// CSS Isolation Fallback Handler
// ============================================================================

// Function to check if CSS isolation file exists and handle gracefully
window.checkCssIsolationFile = function (fileName) {
    return new Promise((resolve) => {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = fileName;
        
        link.onload = function() {
            console.log('CSS isolation file loaded successfully:', fileName);
            resolve(true);
        };
        
        link.onerror = function() {
            console.warn('CSS isolation file not found (this is normal in development):', fileName);
            resolve(false);
        };
        
        document.head.appendChild(link);
    });
};

// Auto-detect and handle CSS isolation
(function() {
    const projectName = 'DocViewer_Proto';
    const cssIsolationFile = `${projectName}.styles.css`;
    
    // Check if the CSS isolation file exists
    fetch(cssIsolationFile, { method: 'HEAD' })
        .then(response => {
            if (!response.ok) {
                console.info(`CSS isolation bundle not found (${response.status}). This is normal in development builds.`);
                // Optionally add individual component CSS files here
                // loadIndividualComponentStyles();
            } else {
                console.log('CSS isolation bundle loaded successfully');
            }
        })
        .catch(error => {
            console.info('CSS isolation bundle not available. Individual component styles will be used.');
        });
})();

// ============================================================================
// Development vs Production CSS Loading
// ============================================================================

window.loadCssConditionally = function(cssFile, fallbackCss) {
    fetch(cssFile, { method: 'HEAD' })
        .then(response => {
            if (response.ok) {
                // File exists, it should already be loaded via link tag
                console.log(`CSS file available: ${cssFile}`);
            } else {
                // File doesn't exist, load fallback if provided
                if (fallbackCss) {
                    const link = document.createElement('link');
                    link.rel = 'stylesheet';
                    link.href = fallbackCss;
                    document.head.appendChild(link);
                    console.log(`Loaded fallback CSS: ${fallbackCss}`);
                }
            }
        })
        .catch(() => {
            console.info(`CSS file not accessible: ${cssFile}`);
        });
};

// ============================================================================
// File Download Utility
// ============================================================================

window.downloadFile = function(filename, base64Content) {
    try {
        // Convert base64 to blob
        const byteCharacters = atob(base64Content);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        const blob = new Blob([byteArray], { type: 'application/zip' });
        
        // Create download link
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        
        // Trigger download
        document.body.appendChild(link);
        link.click();
        
        // Cleanup
        document.body.removeChild(link);
        window.URL.revokeObjectURL(url);
        
        console.log(`File download initiated: ${filename}`);
    } catch (error) {
        console.error('Error downloading file:', error);
        alert('Failed to download file. Please try again.');
    }
};