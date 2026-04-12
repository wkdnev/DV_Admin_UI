// Document Viewer - Unified OpenSeadragon viewer for TIFF/image documents
// Uses server-side tile generation for progressive deep-zoom viewing
// OpenSeadragon 6.0.2

(function () {
    var viewers = {};

    // Fetch image metadata from the API and create a tiled viewer
    async function createTiledViewer(containerId, pageId, options) {
        var opts = options || {};
        var prefixUrl = 'https://cdn.jsdelivr.net/npm/openseadragon@6.0.2/build/openseadragon/images/';

        // Fetch image info from API
        var response = await fetch('/api/DocumentBlob/page/' + pageId + '/image-info', {
            credentials: 'include'
        });

        if (!response.ok) {
            console.error('Failed to fetch image info for page ' + pageId);
            return null;
        }

        var info = await response.json();
        var tileSize = info.tileSize || 256;
        var maxLevel = info.maxLevel || 0;

        // Build custom tile source
        var tileSource = {
            width: info.width,
            height: info.height,
            tileSize: tileSize,
            maxLevel: maxLevel,
            minLevel: Math.max(0, maxLevel - 8), // Don't fetch tiny levels
            getTileUrl: function (level, x, y) {
                return '/api/DocumentBlob/page/' + pageId + '/tile/' + level + '/' + x + '/' + y;
            }
        };

        var viewer = OpenSeadragon({
            id: containerId,
            prefixUrl: prefixUrl,
            tileSources: tileSource,
            // Smooth viewing
            animationTime: 0.4,
            blendTime: 0.2,
            constrainDuringPan: true,
            maxZoomPixelRatio: 4,
            minZoomLevel: 0.3,
            visibilityRatio: 0.8,
            zoomPerScroll: 1.3,
            zoomPerClick: 2,
            // Hide default controls (we use custom toolbar)
            showNavigationControl: false,
            showZoomControl: false,
            showHomeControl: false,
            showFullPageControl: false,
            showRotationControl: false,
            // Navigator minimap
            showNavigator: opts.showNavigator !== undefined ? opts.showNavigator : true,
            navigatorPosition: 'TOP_RIGHT',
            navigatorSizeRatio: 0.15,
            navigatorAutoFade: true,
            // Gesture settings
            gestureSettingsMouse: {
                clickToZoom: false,
                dblClickToZoom: true,
                dblClickDragToZoom: true
            },
            gestureSettingsTouch: {
                pinchToZoom: true,
                flickEnabled: true
            },
            // Load options
            loadTilesWithAjax: true,
            ajaxHeaders: {},
            ajaxWithCredentials: true,
            // Smoothness
            springStiffness: 12,
            immediateRender: false,
            // Crosshairs
            crossOriginPolicy: false
        });

        return viewer;
    }

    // Create a simple image viewer (for small images or fallback)
    function createSimpleViewer(containerId, imageUrl, options) {
        var opts = options || {};
        var prefixUrl = 'https://cdn.jsdelivr.net/npm/openseadragon@6.0.2/build/openseadragon/images/';

        return OpenSeadragon({
            id: containerId,
            prefixUrl: prefixUrl,
            tileSources: {
                type: 'image',
                url: imageUrl,
                buildPyramid: true
            },
            animationTime: 0.4,
            blendTime: 0.2,
            constrainDuringPan: true,
            maxZoomPixelRatio: 4,
            minZoomLevel: 0.3,
            visibilityRatio: 0.8,
            zoomPerScroll: 1.3,
            zoomPerClick: 2,
            showNavigationControl: false,
            showZoomControl: false,
            showHomeControl: false,
            showFullPageControl: false,
            showRotationControl: false,
            showNavigator: opts.showNavigator !== undefined ? opts.showNavigator : true,
            navigatorPosition: 'TOP_RIGHT',
            navigatorSizeRatio: 0.15,
            navigatorAutoFade: true,
            gestureSettingsMouse: {
                clickToZoom: false,
                dblClickToZoom: true,
                dblClickDragToZoom: true
            },
            gestureSettingsTouch: {
                pinchToZoom: true,
                flickEnabled: true
            },
            springStiffness: 12,
            immediateRender: false
        });
    }

    // ==================== Public API ====================

    window.DocViewer = {

        /**
         * Initialize a tiled deep-zoom viewer for an image page.
         * @param {string} containerId - DOM element ID for the viewer
         * @param {number} pageId - Document page ID (for tile API)
         * @param {object} options - Optional configuration
         * @returns {Promise<boolean>} Success
         */
        initTiled: async function (containerId, pageId, options) {
            try {
                if (typeof OpenSeadragon === 'undefined') {
                    console.error('OpenSeadragon library not loaded');
                    return false;
                }

                var container = document.getElementById(containerId);
                if (!container) {
                    console.error('Container not found: ' + containerId);
                    return false;
                }

                // Destroy existing viewer for this container
                if (viewers[containerId]) {
                    viewers[containerId].destroy();
                    viewers[containerId] = null;
                }

                var viewer = await createTiledViewer(containerId, pageId, options);
                if (!viewer) return false;

                viewers[containerId] = viewer;

                viewer.addHandler('open', function () {
                    viewer.viewport.goHome(true);
                });

                viewer.addHandler('open-failed', function (event) {
                    console.error('Failed to open tiled image:', event);
                });

                return true;
            } catch (error) {
                console.error('Error initializing tiled viewer:', error);
                return false;
            }
        },

        /**
         * Initialize a simple image viewer (for non-tiled images).
         * @param {string} containerId - DOM element ID
         * @param {string} imageUrl - Direct URL to the image
         * @param {object} options - Optional configuration
         * @returns {boolean} Success
         */
        initSimple: function (containerId, imageUrl, options) {
            try {
                if (typeof OpenSeadragon === 'undefined') {
                    console.error('OpenSeadragon library not loaded');
                    return false;
                }

                var container = document.getElementById(containerId);
                if (!container) {
                    console.error('Container not found: ' + containerId);
                    return false;
                }

                if (viewers[containerId]) {
                    viewers[containerId].destroy();
                    viewers[containerId] = null;
                }

                var viewer = createSimpleViewer(containerId, imageUrl, options);
                viewers[containerId] = viewer;

                viewer.addHandler('open', function () {
                    viewer.viewport.goHome(true);
                });

                return true;
            } catch (error) {
                console.error('Error initializing simple viewer:', error);
                return false;
            }
        },

        /**
         * Switch to a different tiled page (keeps same container).
         */
        switchToTiledPage: async function (containerId, pageId) {
            if (!viewers[containerId]) {
                return await this.initTiled(containerId, pageId);
            }

            try {
                var response = await fetch('/api/DocumentBlob/page/' + pageId + '/image-info', {
                    credentials: 'include'
                });

                if (!response.ok) return false;

                var info = await response.json();
                var tileSize = info.tileSize || 256;

                viewers[containerId].open({
                    width: info.width,
                    height: info.height,
                    tileSize: tileSize,
                    maxLevel: info.maxLevel || 0,
                    minLevel: Math.max(0, (info.maxLevel || 0) - 8),
                    getTileUrl: function (level, x, y) {
                        return '/api/DocumentBlob/page/' + pageId + '/tile/' + level + '/' + x + '/' + y;
                    }
                });

                return true;
            } catch (error) {
                console.error('Error switching tiled page:', error);
                return false;
            }
        },

        /**
         * Switch to a simple image URL.
         */
        switchToSimpleImage: function (containerId, imageUrl) {
            if (!viewers[containerId]) {
                return this.initSimple(containerId, imageUrl);
            }

            try {
                viewers[containerId].open({
                    type: 'image',
                    url: imageUrl,
                    buildPyramid: true
                });
                return true;
            } catch (error) {
                console.error('Error switching image:', error);
                return false;
            }
        },

        // ==================== Controls ====================

        zoomIn: function (containerId) {
            var v = viewers[containerId];
            if (v) {
                v.viewport.zoomBy(1.5);
                v.viewport.applyConstraints();
            }
        },

        zoomOut: function (containerId) {
            var v = viewers[containerId];
            if (v) {
                v.viewport.zoomBy(0.67);
                v.viewport.applyConstraints();
            }
        },

        home: function (containerId) {
            var v = viewers[containerId];
            if (v) v.viewport.goHome();
        },

        fullPage: function (containerId) {
            var v = viewers[containerId];
            if (v) v.setFullPage(!v.isFullPage());
        },

        rotateLeft: function (containerId) {
            var v = viewers[containerId];
            if (v) {
                var r = v.viewport.getRotation();
                v.viewport.setRotation(r - 90);
            }
        },

        rotateRight: function (containerId) {
            var v = viewers[containerId];
            if (v) {
                var r = v.viewport.getRotation();
                v.viewport.setRotation(r + 90);
            }
        },

        getZoomLevel: function (containerId) {
            var v = viewers[containerId];
            if (!v) return 1;
            return v.viewport.getZoom();
        },

        setZoomLevel: function (containerId, zoom) {
            var v = viewers[containerId];
            if (v) {
                v.viewport.zoomTo(zoom);
                v.viewport.applyConstraints();
            }
        },

        getRotation: function (containerId) {
            var v = viewers[containerId];
            if (!v) return 0;
            return v.viewport.getRotation();
        },

        /**
         * Destroy viewer and free resources.
         */
        destroy: function (containerId) {
            if (viewers[containerId]) {
                viewers[containerId].destroy();
                delete viewers[containerId];
            }
        }
    };

    // ==================== Legacy API compatibility ====================
    // These maintain backward compatibility with the old openseadragon-viewer.js

    window.initializeOpenSeadragon = function (imageUrl) {
        DocViewer.initSimple('openseadragon-viewer', imageUrl);
    };

    window.updateOpenSeadragonImage = function (imageUrl) {
        DocViewer.switchToSimpleImage('openseadragon-viewer', imageUrl);
    };

    window.zoomInOpenSeadragon = function () {
        DocViewer.zoomIn('openseadragon-viewer');
    };

    window.zoomOutOpenSeadragon = function () {
        DocViewer.zoomOut('openseadragon-viewer');
    };

    window.homeOpenSeadragon = function () {
        DocViewer.home('openseadragon-viewer');
    };

    window.fullPageOpenSeadragon = function () {
        DocViewer.fullPage('openseadragon-viewer');
    };

    window.rotateLeftOpenSeadragon = function () {
        DocViewer.rotateLeft('openseadragon-viewer');
    };

    window.rotateRightOpenSeadragon = function () {
        DocViewer.rotateRight('openseadragon-viewer');
    };
})();
