/**
 * Client-side card boundary detection using canvas pixel analysis.
 *
 * Uses a Sobel edge detection filter on a downscaled grayscale canvas
 * to find the card boundary. Falls back to null if no clear boundary
 * is found (user then adjusts manually via corner handles).
 *
 * API:
 *   window.cardBorderDetect.detect(imgElement)
 *     → Promise<double[][]|null>   4 corners [[x,y],...] normalised 0-1, or null
 */
(function () {
    'use strict';

    var MAX_DIM = 400; // downscale target for performance
    var SOBEL_THRESHOLD = 80;
    var MIN_CONTOUR_AREA_RATIO = 0.15; // card must be at least 15% of image

    // ── Helpers ──

    function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

    /** Create a grayscale array from ImageData. */
    function toGrayscale(imageData) {
        var data = imageData.data;
        var w = imageData.width;
        var h = imageData.height;
        var gray = new Float32Array(w * h);
        for (var i = 0; i < w * h; i++) {
            var r = data[i * 4];
            var g = data[i * 4 + 1];
            var b = data[i * 4 + 2];
            gray[i] = 0.299 * r + 0.587 * g + 0.114 * b;
        }
        return gray;
    }

    /** Apply Sobel gradient magnitude. Returns gradient magnitude array. */
    function sobelGradient(gray, w, h) {
        var grad = new Float32Array(w * h);

        for (var y = 1; y < h - 1; y++) {
            for (var x = 1; x < w - 1; x++) {
                var idx = y * w + x;

                // Sobel X kernel
                var gx =
                    -gray[(y - 1) * w + (x - 1)] + gray[(y - 1) * w + (x + 1)] +
                    -2 * gray[y * w + (x - 1)] + 2 * gray[y * w + (x + 1)] +
                    -gray[(y + 1) * w + (x - 1)] + gray[(y + 1) * w + (x + 1)];

                // Sobel Y kernel
                var gy =
                    -gray[(y - 1) * w + (x - 1)] - 2 * gray[(y - 1) * w + x] - gray[(y - 1) * w + (x + 1)] +
                    gray[(y + 1) * w + (x - 1)] + 2 * gray[(y + 1) * w + x] + gray[(y + 1) * w + (x + 1)];

                grad[idx] = Math.sqrt(gx * gx + gy * gy);
            }
        }
        return grad;
    }

    /** Threshold gradient into binary edge map. */
    function threshold(grad, w, h, thresh) {
        var binary = new Uint8Array(w * h);
        for (var i = 0; i < w * h; i++) {
            binary[i] = grad[i] > thresh ? 1 : 0;
        }
        return binary;
    }

    /**
     * Find axis-aligned bounding box of the largest connected region of edge pixels.
     * Uses a simple row/column projection approach for speed.
     */
    function findCardRect(binary, w, h) {
        // Project rows and columns
        var rowSum = new Float32Array(h);
        var colSum = new Float32Array(w);

        for (var y = 0; y < h; y++) {
            for (var x = 0; x < w; x++) {
                var v = binary[y * w + x];
                rowSum[y] += v;
                colSum[x] += v;
            }
        }

        // Find bounds by looking for sustained edge density
        var rowThresh = w * 0.02;
        var colThresh = h * 0.02;

        var top = 0, bottom = h - 1, left = 0, right = w - 1;

        // Find top edge
        for (var yt = 0; yt < h; yt++) {
            if (rowSum[yt] > rowThresh) { top = yt; break; }
        }
        // Find bottom edge
        for (var yb = h - 1; yb >= 0; yb--) {
            if (rowSum[yb] > rowThresh) { bottom = yb; break; }
        }
        // Find left edge
        for (var xl = 0; xl < w; xl++) {
            if (colSum[xl] > colThresh) { left = xl; break; }
        }
        // Find right edge
        for (var xr = w - 1; xr >= 0; xr--) {
            if (colSum[xr] > colThresh) { right = xr; break; }
        }

        return { left: left, top: top, right: right, bottom: bottom };
    }

    /**
     * Refine rectangle edges by scanning inward from each edge to find
     * the strongest continuous edge line.
     */
    function refineEdge(binary, w, h, rect, side, scanDepth) {
        var bestScore = 0;
        var bestPos = rect[side];

        if (side === 'top' || side === 'bottom') {
            var startY = side === 'top' ? Math.max(0, rect.top - scanDepth) : Math.max(0, rect.bottom - scanDepth);
            var endY = side === 'top' ? Math.min(h, rect.top + scanDepth) : Math.min(h, rect.bottom + scanDepth);

            for (var y = startY; y < endY; y++) {
                var score = 0;
                for (var x = rect.left; x <= rect.right; x++) {
                    score += binary[y * w + x];
                }
                if (score > bestScore) {
                    bestScore = score;
                    bestPos = y;
                }
            }
        } else {
            var startX = side === 'left' ? Math.max(0, rect.left - scanDepth) : Math.max(0, rect.right - scanDepth);
            var endX = side === 'left' ? Math.min(w, rect.left + scanDepth) : Math.min(w, rect.right + scanDepth);

            for (var x2 = startX; x2 < endX; x2++) {
                var score2 = 0;
                for (var y2 = rect.top; y2 <= rect.bottom; y2++) {
                    score2 += binary[y2 * w + x2];
                }
                if (score2 > bestScore) {
                    bestScore = score2;
                    bestPos = x2;
                }
            }
        }

        return bestPos;
    }

    /**
     * Detect card boundary from an image element.
     * Returns 4 corners [[x,y],...] in normalised 0-1 coords, or null.
     */
    function detect(imgElement) {
        return new Promise(function (resolve) {
            if (!imgElement || !imgElement.complete || !imgElement.naturalWidth) {
                resolve(null);
                return;
            }

            try {
                var natW = imgElement.naturalWidth;
                var natH = imgElement.naturalHeight;

                // Downscale for performance
                var scale = Math.min(1, MAX_DIM / Math.max(natW, natH));
                var w = Math.round(natW * scale);
                var h = Math.round(natH * scale);

                var canvas = document.createElement('canvas');
                canvas.width = w;
                canvas.height = h;
                var ctx = canvas.getContext('2d');
                ctx.drawImage(imgElement, 0, 0, w, h);

                var imageData = ctx.getImageData(0, 0, w, h);
                var gray = toGrayscale(imageData);
                var grad = sobelGradient(gray, w, h);
                var binary = threshold(grad, w, h, SOBEL_THRESHOLD);

                var rect = findCardRect(binary, w, h);

                // Validate: card region must be significant
                var rectW = rect.right - rect.left;
                var rectH = rect.bottom - rect.top;
                var area = rectW * rectH;
                var totalArea = w * h;

                if (area < totalArea * MIN_CONTOUR_AREA_RATIO) {
                    resolve(null);
                    return;
                }

                // Refine edges
                var scanDepth = Math.round(Math.min(w, h) * 0.05);
                var refinedTop = refineEdge(binary, w, h, rect, 'top', scanDepth);
                var refinedBottom = refineEdge(binary, w, h, rect, 'bottom', scanDepth);
                var refinedLeft = refineEdge(binary, w, h, rect, 'left', scanDepth);
                var refinedRight = refineEdge(binary, w, h, rect, 'right', scanDepth);

                // Convert to normalised 0-1
                var tl = [clamp(refinedLeft / w, 0, 1), clamp(refinedTop / h, 0, 1)];
                var tr = [clamp(refinedRight / w, 0, 1), clamp(refinedTop / h, 0, 1)];
                var br = [clamp(refinedRight / w, 0, 1), clamp(refinedBottom / h, 0, 1)];
                var bl = [clamp(refinedLeft / w, 0, 1), clamp(refinedBottom / h, 0, 1)];

                // Clean up
                canvas.width = 0;
                canvas.height = 0;

                resolve([tl, tr, br, bl]);
            } catch (ex) {
                resolve(null);
            }
        });
    }

    // ── Global entry point ──
    window.cardBorderDetect = {
        detect: detect
    };
})();
