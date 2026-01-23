// OpenSeadragon TIFF Viewer functionality
var viewer = null;

window.initializeOpenSeadragon = function (imageUrl) {
    console.log("initializeOpenSeadragon called with URL:", imageUrl);
    
    try {
        // Check if OpenSeadragon is loaded
        if (typeof OpenSeadragon === 'undefined') {
            console.error("OpenSeadragon library not loaded!");
            return;
        }

        // Check if container exists
        var container = document.getElementById("openseadragon-viewer");
        if (!container) {
            console.error("Container element 'openseadragon-viewer' not found!");
            return;
        }

        console.log("Container found, creating viewer...");

        // Destroy existing viewer if present
        if (viewer) {
            console.log("Destroying existing viewer");
            viewer.destroy();
            viewer = null;
        }

        // Create new viewer
        viewer = OpenSeadragon({
            id: "openseadragon-viewer",
            prefixUrl: "https://cdn.jsdelivr.net/npm/openseadragon@4.1.0/build/openseadragon/images/",
            tileSources: {
                type: 'image',
                url: imageUrl
            },
            // Viewer settings
            animationTime: 0.5,
            blendTime: 0.1,
            constrainDuringPan: false,
            maxZoomPixelRatio: 2,
            minZoomLevel: 0.5,
            visibilityRatio: 1,
            zoomPerScroll: 1.2,
            zoomPerClick: 2,
            // UI settings
            showNavigationControl: false,
            showZoomControl: false,
            showHomeControl: false,
            showFullPageControl: false,
            showRotationControl: false,
            // Gesture settings
            gestureSettingsMouse: {
                clickToZoom: false,
                dblClickToZoom: true
            },
            gestureSettingsTouch: {
                pinchToZoom: true
            }
        });

        console.log("Viewer created successfully");

        // Add event handlers
        viewer.addHandler('open', function () {
            console.log("Image opened successfully");
            viewer.viewport.goHome(true);
        });

        viewer.addHandler('open-failed', function (event) {
            console.error("Failed to open image:", event);
        });

    } catch (error) {
        console.error("Error initializing OpenSeadragon:", error);
    }
};

window.updateOpenSeadragonImage = function (imageUrl) {
    console.log("updateOpenSeadragonImage called with URL:", imageUrl);
    if (viewer) {
        try {
            viewer.open({
                type: 'image',
                url: imageUrl
            });
            console.log("Image update initiated");
        } catch (error) {
            console.error("Error updating image:", error);
        }
    } else {
        console.error("Viewer not initialized!");
    }
};

window.zoomInOpenSeadragon = function () {
    if (viewer) {
        viewer.viewport.zoomBy(1.5);
        viewer.viewport.applyConstraints();
    }
};

window.zoomOutOpenSeadragon = function () {
    if (viewer) {
        viewer.viewport.zoomBy(0.67);
        viewer.viewport.applyConstraints();
    }
};

window.homeOpenSeadragon = function () {
    if (viewer) {
        viewer.viewport.goHome();
    }
};

window.fullPageOpenSeadragon = function () {
    if (viewer) {
        viewer.viewport.goHome();
    }
};

window.rotateLeftOpenSeadragon = function () {
    if (viewer) {
        var currentRotation = viewer.viewport.getRotation();
        viewer.viewport.setRotation(currentRotation - 90);
    }
};

window.rotateRightOpenSeadragon = function () {
    if (viewer) {
        var currentRotation = viewer.viewport.getRotation();
        viewer.viewport.setRotation(currentRotation + 90);
    }
};
