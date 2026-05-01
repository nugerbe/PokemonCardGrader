/**
 * Canvas-based card overlay — Digital Centering Tool.
 *
 * Architecture:
 *   The canvas overlaps an <img> element. The area outside the detected card
 *   boundary is dimmed, effectively "cropping" the card from the photo and
 *   creating a transparent digital grading tool experience.
 *
 *   All guide / ruler / dimension positions use bilinear interpolation
 *   within the actual boundary quad — not an axis-aligned rectangle —
 *   so the overlay tracks the card accurately even when rotated or skewed.
 *
 *   Rendering layers (bottom to top):
 *     1. Dimmed background — dark mask outside the card boundary
 *     2. Outer frame (amber) — detected card edge quad, locked
 *     3. Border regions — amber tint between outer frame and inner guides
 *     4. Ruler marks — tick marks along the border regions
 *     5. Inner guides (green, dashed) — 4 draggable artwork-boundary lines
 *     6. Dimension labels & arrows — border width percentages
 *     7. Crosshairs (white, thin) — card center lines
 *     8. Defect markers — red ellipses for detected defects
 *
 *   Centering readout badges are rendered in Blazor HTML, not canvas.
 *
 *   init(dotNetRef, canvasEl, imgEl, data)
 *     → { updateData, dismiss, undismiss, reset, setRotation, dispose }
 *
 * Data shape:
 *   { boundary: [[x,y],...],  // 4 corners (TL,TR,BR,BL), image-normalised 0-1
 *     borders:  {left, right, top, bottom},  // card-fractions 0-1
 *     defects:  [{type,severity,confidence,x,y,w,h},...],
 *     rotation: number }
 *
 * .NET callbacks:
 *   OnBorderDragEnd(left, right, top, bottom)
 *   OnCenteringUpdated(lrString, tbString)
 *   OnRotationDetected(degrees)
 *   OnDragCancelled()
 */

(function () {
    'use strict';

    // ── Constants ──

    var GUIDE_HIT       = 14;           // px tolerance for guide hit-testing
    var GUIDE_COLOR     = 'rgba(34,197,94,0.85)';      // green #22c55e
    var GUIDE_HOVER     = 'rgba(34,197,94,1.0)';
    var GUIDE_DASH      = [8, 4];

    var OUTER_COLOR     = 'rgba(245,158,11,0.9)';      // amber #f59e0b
    var OUTER_WIDTH     = 2;

    var DIM_BG          = 'rgba(15,23,42,0.65)';       // dark mask outside card
    var BORDER_TINT     = 'rgba(245,158,11,0.10)';     // amber fill in border regions

    var RULER_COLOR     = 'rgba(255,255,255,0.18)';
    var RULER_MAJOR     = 'rgba(255,255,255,0.30)';

    var DIM_LABEL_COLOR = 'rgba(255,255,255,0.55)';
    var DIM_LABEL_FONT  = '500 10px "Inter Tight", "Inter", system-ui, sans-serif';

    var CENTER_COLOR    = 'rgba(255,255,255,0.12)';
    var CENTER_DASH     = [4, 6];

    var DEFECT_COLOR    = '#f87171';

    var CORNER_HANDLE_RADIUS = 7;
    var CORNER_HIT           = 16;          // px tolerance for corner hit-testing
    var CORNER_COLOR         = 'rgba(245,158,11,0.85)';
    var CORNER_HOVER         = 'rgba(245,158,11,1.0)';

    var PCT_GUIDE_COLORS = {
        '45': 'rgba(96,165,250,0.35)',    // blue
        '50': 'rgba(250,204,21,0.45)',    // yellow
        '55': 'rgba(96,165,250,0.35)'     // blue
    };
    var PCT_GUIDE_DASH   = [4, 6];

    // ── Geometry helpers ──

    function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }
    function clamp01(v) { return clamp(v, 0, 1); }

    function lerp(a, b, t) { return a + (b - a) * t; }

    /** Bilinear interpolation within a quad: (u,v) in [0,1] → [x,y]. */
    function bilinear(tl, tr, br, bl, u, v) {
        var topX = lerp(tl[0], tr[0], u);
        var topY = lerp(tl[1], tr[1], u);
        var botX = lerp(bl[0], br[0], u);
        var botY = lerp(bl[1], br[1], u);
        return [lerp(topX, botX, v), lerp(topY, botY, v)];
    }

    /** Inverse bilinear: given a point in image-norm space, find (u,v) in card space. */
    function inverseBilinear(tl, tr, br, bl, nx, ny) {
        // Iterative refinement — 4 iterations converges well for convex quads.
        var u = 0.5, v = 0.5;
        for (var iter = 0; iter < 4; iter++) {
            var leftX  = lerp(tl[0], bl[0], v);
            var rightX = lerp(tr[0], br[0], v);
            var span   = rightX - leftX;
            u = span > 0.001 ? (nx - leftX) / span : 0.5;
            u = clamp01(u);

            var topY = lerp(tl[1], tr[1], u);
            var botY = lerp(bl[1], br[1], u);
            var vSpan = botY - topY;
            v = vSpan > 0.001 ? (ny - topY) / vSpan : 0.5;
            v = clamp01(v);
        }
        return [u, v];
    }

    function dist(a, b) {
        var dx = a[0] - b[0], dy = a[1] - b[1];
        return Math.sqrt(dx * dx + dy * dy);
    }

    // ── Main class ──

    function CardOverlay(canvas, img, dotNetRef, data) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        this.img = img;
        this.ref = dotNetRef;
        this.dpr = window.devicePixelRatio || 1;

        // Card boundary quad: [TL, TR, BR, BL] in image-normalised 0-1 space
        this.boundary = null;
        // Inner guide positions: card-fractions 0-1
        this.guides = { left: 0.05, right: 0.95, top: 0.05, bottom: 0.95 };
        this.defects = [];
        this.dismissed = {};

        // Boundary mode: 'detected' | 'manual' | 'default' | 'rectified'
        this._boundaryMode = 'default';

        // Rectified mode: the underlying <img> IS the perspective-corrected card.
        // The boundary is locked to identity (full-image axis-aligned), corner handles
        // are hidden, and CSS rotation is suppressed. This is the digital equivalent
        // of a Luxiv overlay sitting on a physically-flat card.
        this._rectified = false;

        // Percentage guide lines
        this._percentGuidesVisible = false;
        this._percentGuidePositions = [0.45, 0.50, 0.55];

        // Rotation
        this._rotation = 0;
        this._autoRotated = false;

        // Drag state
        this._drag = null;   // { side: 'left'|..., type: 'guide' } or { cornerIdx: 0-3, type: 'corner' }
        this._hover = null;  // guide side string or 'corner:0' etc.

        this._parseData(data);
        this._ensureBoundary();

        this._onDown   = this._handleDown.bind(this);
        this._onMove   = this._handleMove.bind(this);
        this._onUp     = this._handleUp.bind(this);
        this._onCancel = this._handleCancel.bind(this);

        canvas.style.touchAction = 'none';
        canvas.style.userSelect = 'none';
        canvas.addEventListener('pointerdown', this._onDown);
        canvas.addEventListener('pointermove', this._onMove);

        this._resize();
        this._render();

        var self = this;
        this._ro = new ResizeObserver(function () {
            self._resize();
            self._render();
        });
        this._ro.observe(canvas.parentElement);

        if (!img.complete) {
            this._imgLoad = function () {
                self._resize();
                self._autoRotate();
                self._render();
                self._fireCenteringUpdate();
            };
            img.addEventListener('load', this._imgLoad);
        } else {
            this._autoRotate();
            this._fireCenteringUpdate();
        }
    }

    // ── Data parsing ──

    CardOverlay.prototype._parseData = function (data) {
        // Rectified mode is sticky for the lifetime of the instance — it only
        // changes when explicitly toggled via setRectifiedMode. _parseData is
        // also called from updateData/reset, and we don't want a stale data
        // payload (e.g. one without the flag) to silently flip the mode off.
        if (data && data.rectified === true) {
            this._rectified = true;
        }

        if (this._rectified) {
            // Identity quad — the entire image IS the card.
            this.boundary = [[0, 0], [1, 0], [1, 1], [0, 1]];
            this._boundaryMode = 'rectified';
        } else if (data && data.boundary && data.boundary.length === 4) {
            this.boundary = data.boundary.map(function (p) { return [p[0], p[1]]; });
        }
        if (data && data.borders) {
            this.guides = {
                left:   clamp01(data.borders.left),
                right:  clamp01(data.borders.right),
                top:    clamp01(data.borders.top),
                bottom: clamp01(data.borders.bottom)
            };
        }
        if (data && data.defects) {
            this.defects = data.defects;
        }
        if (data && typeof data.rotation === 'number' && !this._rectified) {
            this._rotation = data.rotation;
        }
    };

    /** Ensure a boundary always exists — use a default if none was detected. */
    CardOverlay.prototype._ensureBoundary = function () {
        if (this.boundary && this.boundary.length === 4) {
            if (this._boundaryMode === 'default') {
                this._boundaryMode = 'detected';
            }
            return;
        }
        this.boundary = [[0.02, 0.02], [0.98, 0.02], [0.98, 0.98], [0.02, 0.98]];
        this._boundaryMode = 'default';
    };

    // ── Layout ──

    CardOverlay.prototype._resize = function () {
        var rect = this.canvas.getBoundingClientRect();
        var w = rect.width;
        var h = rect.height;
        if (w === 0 || h === 0) return;

        this.width = w;
        this.height = h;
        this.canvas.width = Math.round(w * this.dpr);
        this.canvas.height = Math.round(h * this.dpr);
        this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
    };

    // ── Coordinate conversion ──

    /** Pointer event → image-normalised (0-1). */
    CardOverlay.prototype._toNorm = function (e) {
        var rect = this.canvas.getBoundingClientRect();
        return [
            clamp01((e.clientX - rect.left) / rect.width),
            clamp01((e.clientY - rect.top) / rect.height)
        ];
    };

    /** Image-normalised → canvas pixels. */
    CardOverlay.prototype._imgToCanvas = function (norm) {
        return [norm[0] * this.width, norm[1] * this.height];
    };

    /**
     * Card-fraction (u,v) → image-normalised, via bilinear interpolation
     * within the boundary quad.
     *   u: 0 = left edge,  1 = right edge
     *   v: 0 = top edge,   1 = bottom edge
     */
    CardOverlay.prototype._cardToNorm = function (u, v) {
        if (!this.boundary) return [u, v];
        return bilinear(
            this.boundary[0], this.boundary[1],
            this.boundary[2], this.boundary[3],
            u, v
        );
    };

    /** Card-fraction (u,v) → canvas pixels. */
    CardOverlay.prototype._cardToPixel = function (u, v) {
        return this._imgToCanvas(this._cardToNorm(u, v));
    };

    /** Image-normalised → card-fraction (u,v) via inverse bilinear. */
    CardOverlay.prototype._normToCard = function (nx, ny) {
        if (!this.boundary) return [nx, ny];
        return inverseBilinear(
            this.boundary[0], this.boundary[1],
            this.boundary[2], this.boundary[3],
            nx, ny
        );
    };

    // ── Auto-rotation ──

    CardOverlay.prototype._computeAutoRotation = function () {
        if (!this.boundary || this.boundary.length < 4) return 0;

        var TL = this.boundary[0], TR = this.boundary[1];
        var BR = this.boundary[2], BL = this.boundary[3];
        var imgW = this.img.naturalWidth || 1;
        var imgH = this.img.naturalHeight || 1;

        var topDx  = (TR[0] - TL[0]) * imgW;
        var topDy  = (TR[1] - TL[1]) * imgH;
        var topAng = Math.atan2(topDy, topDx);

        var botDx  = (BR[0] - BL[0]) * imgW;
        var botDy  = (BR[1] - BL[1]) * imgH;
        var botAng = Math.atan2(botDy, botDx);

        var avgAng  = (topAng + botAng) / 2;
        var degrees = -(avgAng * 180 / Math.PI);

        return clamp(Math.round(degrees * 10) / 10, -10, 10);
    };

    CardOverlay.prototype._autoRotate = function () {
        // In rectified mode the server has already perspective-corrected the card,
        // so any further rotation would just skew it back off-axis.
        if (this._rectified) return;
        if (this._rotation !== 0 || !this.boundary || this._autoRotated) return;

        var autoRot = this._computeAutoRotation();
        if (Math.abs(autoRot) > 0.05) {
            this._rotation = autoRot;
            this._autoRotated = true;
            this._applyRotation();

            if (this.ref) {
                this.ref.invokeMethodAsync('OnRotationDetected', autoRot);
            }
        }
    };

    CardOverlay.prototype._applyRotation = function () {
        if (!this.img) return;
        // Rectified images are pre-aligned by the server perspective warp.
        // Forcing CSS rotation on top would visually re-tilt them.
        if (this._rectified) {
            this.img.style.transform = '';
            return;
        }
        this.img.style.transform = this._rotation !== 0
            ? 'rotate(' + this._rotation + 'deg)'
            : '';
    };

    // ── Rendering ──

    CardOverlay.prototype._render = function () {
        var ctx = this.ctx;
        var w = this.width;
        var h = this.height;
        if (!w || !h) return;

        ctx.clearRect(0, 0, w, h);

        this._ensureBoundary();

        this._drawDimmedBackground(ctx, w, h);   // 1. Dark mask outside card
        this._drawOuterFrame(ctx);                // 2. Outer frame (amber)
        this._drawCornerHandles(ctx);             // 2b. Draggable corner handles
        this._drawBorderRegions(ctx);             // 3. Border tint regions
        this._drawRulerMarks(ctx);                // 4. Ruler ticks
        this._drawGuides(ctx);                    // 5. Inner guides (green)
        this._drawPercentGuides(ctx);             // 5b. Percentage guide lines
        this._drawDimensionLabels(ctx);           // 6. Dimension labels + arrows
        this._drawCrosshairs(ctx);                // 7. Crosshairs
        this._drawDefects(ctx);                   // 8. Defect markers
    };

    // ── 1. Dimmed background (mask outside card) ──

    CardOverlay.prototype._drawDimmedBackground = function (ctx, w, h) {
        // In rectified mode the entire image IS the card, so there's no
        // "outside the card" region to dim.
        if (this._rectified) return;
        var pts = this.boundary.map(this._imgToCanvas.bind(this));

        ctx.save();
        ctx.fillStyle = DIM_BG;

        // Even-odd fill: full rect minus card quad → dims everything outside
        ctx.beginPath();
        ctx.rect(0, 0, w, h);
        ctx.moveTo(pts[0][0], pts[0][1]);
        ctx.lineTo(pts[1][0], pts[1][1]);
        ctx.lineTo(pts[2][0], pts[2][1]);
        ctx.lineTo(pts[3][0], pts[3][1]);
        ctx.closePath();
        ctx.fill('evenodd');

        ctx.restore();
    };

    // ── 2. Outer frame (card edge — locked, amber) ──

    CardOverlay.prototype._drawOuterFrame = function (ctx) {
        var pts = this.boundary.map(this._imgToCanvas.bind(this));

        ctx.save();
        ctx.strokeStyle = OUTER_COLOR;
        ctx.lineWidth = OUTER_WIDTH;
        ctx.setLineDash([]);

        // Draw the quad
        ctx.beginPath();
        ctx.moveTo(pts[0][0], pts[0][1]);
        for (var i = 1; i < 4; i++) ctx.lineTo(pts[i][0], pts[i][1]);
        ctx.closePath();
        ctx.stroke();

        // Corner L-bracket markers
        ctx.lineWidth = 2.5;
        var markerLen = 10;
        for (var j = 0; j < 4; j++) {
            var cur  = pts[j];
            var next = pts[(j + 1) % 4];
            var prev = pts[(j + 3) % 4];

            var toNextLen = dist(cur, next) || 1;
            var toPrevLen = dist(cur, prev) || 1;

            ctx.beginPath();
            ctx.moveTo(
                cur[0] + (next[0] - cur[0]) / toNextLen * markerLen,
                cur[1] + (next[1] - cur[1]) / toNextLen * markerLen
            );
            ctx.lineTo(cur[0], cur[1]);
            ctx.lineTo(
                cur[0] + (prev[0] - cur[0]) / toPrevLen * markerLen,
                cur[1] + (prev[1] - cur[1]) / toPrevLen * markerLen
            );
            ctx.stroke();
        }

        ctx.restore();
    };

    // ── 2b. Draggable corner handles ──

    CardOverlay.prototype._drawCornerHandles = function (ctx) {
        if (!this.boundary) return;
        // Rectified mode: handles would just sit at the four image corners and
        // aren't meaningful since the image already represents the rectified card.
        if (this._rectified) return;
        var pts = this.boundary.map(this._imgToCanvas.bind(this));

        ctx.save();
        for (var i = 0; i < 4; i++) {
            var isHover = this._hover === 'corner:' + i;
            var isDrag  = this._drag && this._drag.type === 'corner' && this._drag.cornerIdx === i;
            var active  = isHover || isDrag;

            ctx.fillStyle = active ? CORNER_HOVER : CORNER_COLOR;
            ctx.strokeStyle = 'rgba(255,255,255,0.9)';
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(pts[i][0], pts[i][1], active ? CORNER_HANDLE_RADIUS + 2 : CORNER_HANDLE_RADIUS, 0, Math.PI * 2);
            ctx.fill();
            ctx.stroke();
        }
        ctx.restore();
    };

    // ── 3. Border regions (amber tint between outer frame and inner guides) ──

    CardOverlay.prototype._drawBorderRegions = function (ctx) {
        var g = this.guides;

        ctx.save();
        ctx.fillStyle = BORDER_TINT;

        // Left region: quad from (0,0)→(g.left,0)→(g.left,1)→(0,1)
        this._fillQuadRegion(ctx, 0, 0, g.left, 0, g.left, 1, 0, 1);
        // Right region
        this._fillQuadRegion(ctx, g.right, 0, 1, 0, 1, 1, g.right, 1);
        // Top region (between inner left and inner right)
        this._fillQuadRegion(ctx, g.left, 0, g.right, 0, g.right, g.top, g.left, g.top);
        // Bottom region
        this._fillQuadRegion(ctx, g.left, g.bottom, g.right, g.bottom, g.right, 1, g.left, 1);

        ctx.restore();
    };

    /** Fill a quad defined by 4 card-fraction (u,v) pairs. */
    CardOverlay.prototype._fillQuadRegion = function (ctx, u0, v0, u1, v1, u2, v2, u3, v3) {
        var p0 = this._cardToPixel(u0, v0);
        var p1 = this._cardToPixel(u1, v1);
        var p2 = this._cardToPixel(u2, v2);
        var p3 = this._cardToPixel(u3, v3);
        ctx.beginPath();
        ctx.moveTo(p0[0], p0[1]);
        ctx.lineTo(p1[0], p1[1]);
        ctx.lineTo(p2[0], p2[1]);
        ctx.lineTo(p3[0], p3[1]);
        ctx.closePath();
        ctx.fill();
    };

    // ── 4. Ruler marks ──

    CardOverlay.prototype._drawRulerMarks = function (ctx) {
        var g = this.guides;
        ctx.save();
        ctx.lineWidth = 1;

        // Left border ticks (from u=0 to u=g.left, centred vertically)
        if (g.left > 0.005) {
            this._drawTicksAlongEdge(ctx, 0, g.left, 0.25, 0.75, 'horizontal');
        }
        // Right border ticks
        if ((1 - g.right) > 0.005) {
            this._drawTicksAlongEdge(ctx, g.right, 1, 0.25, 0.75, 'horizontal');
        }
        // Top border ticks
        if (g.top > 0.005) {
            this._drawTicksAlongEdge(ctx, 0, g.top, 0.25, 0.75, 'vertical');
        }
        // Bottom border ticks
        if ((1 - g.bottom) > 0.005) {
            this._drawTicksAlongEdge(ctx, g.bottom, 1, 0.25, 0.75, 'vertical');
        }

        ctx.restore();
    };

    /** Draw tick marks along a border region edge. */
    CardOverlay.prototype._drawTicksAlongEdge = function (ctx, fracStart, fracEnd, crossStart, crossEnd, orientation) {
        var fracSpan = Math.abs(fracEnd - fracStart);
        var steps = Math.max(2, Math.min(10, Math.round(fracSpan * 100)));

        for (var i = 0; i <= steps; i++) {
            var t = i / steps;
            var frac = lerp(fracStart, fracEnd, t);
            var isMajor = (i === 0 || i === steps || i === Math.round(steps / 2));
            var tickFrac = isMajor ? 0.015 : 0.008;

            ctx.strokeStyle = isMajor ? RULER_MAJOR : RULER_COLOR;

            if (orientation === 'horizontal') {
                // Ticks along vertical extent at this u-fraction
                var pTop = this._cardToPixel(frac, crossStart);
                var pTopTick = this._cardToPixel(frac, crossStart + tickFrac);
                ctx.beginPath(); ctx.moveTo(pTop[0], pTop[1]); ctx.lineTo(pTopTick[0], pTopTick[1]); ctx.stroke();

                var pBot = this._cardToPixel(frac, crossEnd);
                var pBotTick = this._cardToPixel(frac, crossEnd - tickFrac);
                ctx.beginPath(); ctx.moveTo(pBot[0], pBot[1]); ctx.lineTo(pBotTick[0], pBotTick[1]); ctx.stroke();
            } else {
                // Ticks along horizontal extent at this v-fraction
                var pLeft = this._cardToPixel(crossStart, frac);
                var pLeftTick = this._cardToPixel(crossStart + tickFrac, frac);
                ctx.beginPath(); ctx.moveTo(pLeft[0], pLeft[1]); ctx.lineTo(pLeftTick[0], pLeftTick[1]); ctx.stroke();

                var pRight = this._cardToPixel(crossEnd, frac);
                var pRightTick = this._cardToPixel(crossEnd - tickFrac, frac);
                ctx.beginPath(); ctx.moveTo(pRight[0], pRight[1]); ctx.lineTo(pRightTick[0], pRightTick[1]); ctx.stroke();
            }
        }
    };

    // ── 5. Inner guide lines (draggable, green) ──

    CardOverlay.prototype._drawGuides = function (ctx) {
        var g = this.guides;

        var guideLines = [
            { side: 'left',   u0: g.left,  v0: 0, u1: g.left,  v1: 1, dir: 'ew' },
            { side: 'right',  u0: g.right, v0: 0, u1: g.right, v1: 1, dir: 'ew' },
            { side: 'top',    u0: 0, v0: g.top,    u1: 1, v1: g.top,    dir: 'ns' },
            { side: 'bottom', u0: 0, v0: g.bottom, u1: 1, v1: g.bottom, dir: 'ns' }
        ];

        ctx.save();

        for (var i = 0; i < guideLines.length; i++) {
            var gl = guideLines[i];
            var p0 = this._cardToPixel(gl.u0, gl.v0);
            var p1 = this._cardToPixel(gl.u1, gl.v1);

            var isHover = this._hover === gl.side;
            var isDrag  = this._drag && this._drag.type === 'guide' && this._drag.side === gl.side;
            var active  = isHover || isDrag;

            // Guide line
            ctx.strokeStyle = active ? GUIDE_HOVER : GUIDE_COLOR;
            ctx.lineWidth = active ? 2.5 : 1.5;
            ctx.setLineDash(GUIDE_DASH);
            ctx.beginPath();
            ctx.moveTo(p0[0], p0[1]);
            ctx.lineTo(p1[0], p1[1]);
            ctx.stroke();

            // Grab handle at midpoint
            var mx = (p0[0] + p1[0]) / 2;
            var my = (p0[1] + p1[1]) / 2;
            ctx.setLineDash([]);
            ctx.fillStyle = active ? GUIDE_HOVER : 'rgba(34,197,94,0.6)';

            var hw = gl.dir === 'ew' ? 4 : 12;
            var hh = gl.dir === 'ew' ? 12 : 4;
            ctx.beginPath();
            ctx.roundRect(mx - hw, my - hh, hw * 2, hh * 2, 3);
            ctx.fill();

            // Arrow triangles on handle
            ctx.fillStyle = 'rgba(23,23,26,0.6)';
            if (gl.dir === 'ew') {
                ctx.beginPath();
                ctx.moveTo(mx - 1, my - 3); ctx.lineTo(mx - 3, my); ctx.lineTo(mx - 1, my + 3); ctx.fill();
                ctx.beginPath();
                ctx.moveTo(mx + 1, my - 3); ctx.lineTo(mx + 3, my); ctx.lineTo(mx + 1, my + 3); ctx.fill();
            } else {
                ctx.beginPath();
                ctx.moveTo(mx - 3, my - 1); ctx.lineTo(mx, my - 3); ctx.lineTo(mx + 3, my - 1); ctx.fill();
                ctx.beginPath();
                ctx.moveTo(mx - 3, my + 1); ctx.lineTo(mx, my + 3); ctx.lineTo(mx + 3, my + 1); ctx.fill();
            }
        }

        ctx.restore();
    };

    // ── 5b. Percentage guide lines (configurable positions e.g. 45/50/55%) ──

    CardOverlay.prototype._drawPercentGuides = function (ctx) {
        if (!this._percentGuidesVisible || !this._percentGuidePositions) return;
        if (!this.boundary) return;

        ctx.save();
        ctx.lineWidth = 1;
        ctx.setLineDash(PCT_GUIDE_DASH);

        for (var i = 0; i < this._percentGuidePositions.length; i++) {
            var pct = this._percentGuidePositions[i];
            var key = (pct * 100).toFixed(0);
            ctx.strokeStyle = PCT_GUIDE_COLORS[key] || 'rgba(148,163,184,0.3)';

            // Vertical line at this horizontal percentage
            var vTop = this._cardToPixel(pct, 0);
            var vBot = this._cardToPixel(pct, 1);
            ctx.beginPath();
            ctx.moveTo(vTop[0], vTop[1]);
            ctx.lineTo(vBot[0], vBot[1]);
            ctx.stroke();

            // Horizontal line at this vertical percentage
            var hLeft  = this._cardToPixel(0, pct);
            var hRight = this._cardToPixel(1, pct);
            ctx.beginPath();
            ctx.moveTo(hLeft[0], hLeft[1]);
            ctx.lineTo(hRight[0], hRight[1]);
            ctx.stroke();

            // Label at top of vertical line
            if (pct !== 0.50) {
                ctx.setLineDash([]);
                ctx.font = '9px "JetBrains Mono", monospace';
                ctx.fillStyle = ctx.strokeStyle;
                ctx.textAlign = 'center';
                ctx.fillText(key + '%', vTop[0], vTop[1] - 4);
                ctx.setLineDash(PCT_GUIDE_DASH);
            }
        }

        ctx.restore();
    };

    // ── 6. Dimension labels & arrows ──

    CardOverlay.prototype._drawDimensionLabels = function (ctx) {
        var widths = this._computeBorderWidths();
        if (!widths) return;
        var g = this.guides;

        ctx.save();
        ctx.font = DIM_LABEL_FONT;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        var leftPct  = (widths.left  * 100).toFixed(1) + '%';
        var rightPct = (widths.right * 100).toFixed(1) + '%';
        var topPct   = (widths.top   * 100).toFixed(1) + '%';
        var botPct   = (widths.bot   * 100).toFixed(1) + '%';

        // Left: centre of left border region
        var lMid = this._cardToPixel(g.left / 2, 0.5);
        var lOuter = this._cardToPixel(0, 0.5);
        var lInner = this._cardToPixel(g.left, 0.5);
        if (dist(lOuter, lInner) > 28) {
            this._drawDimBadge(ctx, lMid[0], lMid[1], leftPct);
            this._drawDimArrow(ctx, lOuter[0], lOuter[1], lInner[0], lInner[1]);
        }

        // Right
        var rFrac = (g.right + 1) / 2;
        var rMid = this._cardToPixel(rFrac, 0.5);
        var rOuter = this._cardToPixel(1, 0.5);
        var rInner = this._cardToPixel(g.right, 0.5);
        if (dist(rOuter, rInner) > 28) {
            this._drawDimBadge(ctx, rMid[0], rMid[1], rightPct);
            this._drawDimArrow(ctx, rInner[0], rInner[1], rOuter[0], rOuter[1]);
        }

        // Top
        var tMid = this._cardToPixel(0.5, g.top / 2);
        var tOuter = this._cardToPixel(0.5, 0);
        var tInner = this._cardToPixel(0.5, g.top);
        if (dist(tOuter, tInner) > 18) {
            this._drawDimBadge(ctx, tMid[0], tMid[1], topPct);
            this._drawDimArrow(ctx, tOuter[0], tOuter[1], tInner[0], tInner[1]);
        }

        // Bottom
        var bFrac = (g.bottom + 1) / 2;
        var bMid = this._cardToPixel(0.5, bFrac);
        var bOuter = this._cardToPixel(0.5, 1);
        var bInner = this._cardToPixel(0.5, g.bottom);
        if (dist(bOuter, bInner) > 18) {
            this._drawDimBadge(ctx, bMid[0], bMid[1], botPct);
            this._drawDimArrow(ctx, bInner[0], bInner[1], bOuter[0], bOuter[1]);
        }

        ctx.restore();
    };

    /** Draw a pill-shaped label badge. */
    CardOverlay.prototype._drawDimBadge = function (ctx, cx, cy, text) {
        var tw = ctx.measureText(text).width;
        var pw = 5, ph = 3;
        var bw = tw + pw * 2;
        var bh = 14 + ph * 2;

        ctx.fillStyle = 'rgba(23,23,26,0.65)';
        ctx.beginPath();
        ctx.roundRect(cx - bw / 2, cy - bh / 2, bw, bh, 4);
        ctx.fill();

        ctx.fillStyle = DIM_LABEL_COLOR;
        ctx.fillText(text, cx, cy + 1);
    };

    /** Draw a thin arrow line between two points. */
    CardOverlay.prototype._drawDimArrow = function (ctx, x1, y1, x2, y2) {
        var len = dist([x1, y1], [x2, y2]);
        if (len < 12) return;

        ctx.strokeStyle = 'rgba(255,255,255,0.15)';
        ctx.lineWidth = 0.75;
        ctx.setLineDash([]);

        ctx.beginPath();
        ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();

        var arrowLen = 4;
        var angle = Math.atan2(y2 - y1, x2 - x1);

        // Arrowhead at end
        ctx.beginPath();
        ctx.moveTo(x2 + arrowLen * Math.cos(angle + Math.PI * 0.75), y2 + arrowLen * Math.sin(angle + Math.PI * 0.75));
        ctx.lineTo(x2, y2);
        ctx.lineTo(x2 + arrowLen * Math.cos(angle - Math.PI * 0.75), y2 + arrowLen * Math.sin(angle - Math.PI * 0.75));
        ctx.stroke();

        // Arrowhead at start
        ctx.beginPath();
        ctx.moveTo(x1 + arrowLen * Math.cos(angle + Math.PI * 0.25), y1 + arrowLen * Math.sin(angle + Math.PI * 0.25));
        ctx.lineTo(x1, y1);
        ctx.lineTo(x1 + arrowLen * Math.cos(angle - Math.PI * 0.25), y1 + arrowLen * Math.sin(angle - Math.PI * 0.25));
        ctx.stroke();
    };

    // ── 7. Crosshairs ──

    CardOverlay.prototype._drawCrosshairs = function (ctx) {
        var vTop = this._cardToPixel(0.5, 0);
        var vBot = this._cardToPixel(0.5, 1);
        var hLeft  = this._cardToPixel(0, 0.5);
        var hRight = this._cardToPixel(1, 0.5);

        ctx.save();
        ctx.strokeStyle = CENTER_COLOR;
        ctx.lineWidth = 1;
        ctx.setLineDash(CENTER_DASH);

        ctx.beginPath(); ctx.moveTo(vTop[0], vTop[1]); ctx.lineTo(vBot[0], vBot[1]); ctx.stroke();
        ctx.beginPath(); ctx.moveTo(hLeft[0], hLeft[1]); ctx.lineTo(hRight[0], hRight[1]); ctx.stroke();

        ctx.restore();
    };

    // ── 8. Defect markers ──

    CardOverlay.prototype._drawDefects = function (ctx) {
        if (!this.defects || !this.boundary) return;

        ctx.save();

        // Clip to card boundary
        var pts = this.boundary.map(this._imgToCanvas.bind(this));
        ctx.beginPath();
        ctx.moveTo(pts[0][0], pts[0][1]);
        for (var i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0], pts[i][1]);
        ctx.closePath();
        ctx.clip();

        var tl = this.boundary[0], tr = this.boundary[1];
        var br = this.boundary[2], bl = this.boundary[3];
        var self = this;

        this.defects.forEach(function (d, idx) {
            if (self.dismissed[idx]) return;

            var center = bilinear(tl, tr, br, bl, d.x + d.w / 2, d.y + d.h / 2);
            var cp = self._imgToCanvas(center);

            var cardW = dist(self._imgToCanvas(tl), self._imgToCanvas(tr));
            var cardH = dist(self._imgToCanvas(tl), self._imgToCanvas(bl));
            var rx = Math.max(6, Math.min(20, d.w * cardW / 2));
            var ry = Math.max(6, Math.min(20, d.h * cardH / 2));

            var conf = d.confidence || 0.5;
            var color, alpha, label;
            if (conf >= 0.7) {
                color = '#f43f5e'; alpha = 0.9; label = d.type;
            } else if (conf >= 0.4) {
                color = '#f59e0b'; alpha = 0.7; label = d.type + '?';
            } else {
                color = '#94a3b8'; alpha = 0.5; label = d.type + '??';
            }

            ctx.strokeStyle = color;
            ctx.lineWidth = conf >= 0.7 ? 2 : 1.5;
            ctx.globalAlpha = alpha;
            ctx.setLineDash(conf >= 0.4 ? [] : [3, 3]);
            ctx.beginPath();
            ctx.ellipse(cp[0], cp[1], rx, ry, 0, 0, Math.PI * 2);
            ctx.stroke();
            ctx.setLineDash([]);

            ctx.fillStyle = color;
            ctx.font = '11px monospace';
            ctx.fillText(label, cp[0] + rx + 3, cp[1] + 4);
        });

        ctx.restore();
    };

    // ── Centering computation ──

    CardOverlay.prototype._computeBorderWidths = function () {
        var g = this.guides;
        return {
            left:  Math.max(0, g.left),
            right: Math.max(0, 1 - g.right),
            top:   Math.max(0, g.top),
            bot:   Math.max(0, 1 - g.bottom)
        };
    };

    CardOverlay.prototype._computeCentering = function () {
        var widths = this._computeBorderWidths();
        if (!widths) return null;

        var lrTotal = widths.left + widths.right;
        var tbTotal = widths.top + widths.bot;

        var lPct = lrTotal < 0.0001 ? 50 : Math.round(widths.left / lrTotal * 100);
        var tPct = tbTotal < 0.0001 ? 50 : Math.round(widths.top / tbTotal * 100);

        return {
            lr: lPct + '/' + (100 - lPct),
            tb: tPct + '/' + (100 - tPct)
        };
    };

    CardOverlay.prototype._fireCenteringUpdate = function () {
        var centering = this._computeCentering();
        if (centering && this.ref) {
            this.ref.invokeMethodAsync('OnCenteringUpdated', centering.lr, centering.tb);
        }
    };

    // ── Hit testing ──

    CardOverlay.prototype._hitTest = function (normPos) {
        if (!this.boundary || !this.guides) return null;

        var px = this._imgToCanvas(normPos);

        // In rectified mode, corner handles are hidden — only the inner border
        // guides are draggable (matching a Luxiv-style centering tool).
        if (!this._rectified) {
            var pts = this.boundary.map(this._imgToCanvas.bind(this));
            for (var ci = 0; ci < 4; ci++) {
                if (dist(px, pts[ci]) <= CORNER_HIT) {
                    return 'corner:' + ci;
                }
            }
        }

        var g = this.guides;

        // Test each guide line — compute its pixel endpoints and measure distance
        var guides = [
            { side: 'left',   u0: g.left,  v0: 0, u1: g.left,  v1: 1 },
            { side: 'right',  u0: g.right, v0: 0, u1: g.right, v1: 1 },
            { side: 'top',    u0: 0, v0: g.top,    u1: 1, v1: g.top },
            { side: 'bottom', u0: 0, v0: g.bottom, u1: 1, v1: g.bottom }
        ];

        for (var i = 0; i < guides.length; i++) {
            var gl = guides[i];
            var p0 = this._cardToPixel(gl.u0, gl.v0);
            var p1 = this._cardToPixel(gl.u1, gl.v1);

            var d = distToSegment(px, p0, p1);
            if (d <= GUIDE_HIT) return gl.side;
        }

        return null;
    };

    /** Distance from point p to line segment (a, b). */
    function distToSegment(p, a, b) {
        var dx = b[0] - a[0], dy = b[1] - a[1];
        var lenSq = dx * dx + dy * dy;
        if (lenSq === 0) return dist(p, a);
        var t = clamp(((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / lenSq, 0, 1);
        return dist(p, [a[0] + t * dx, a[1] + t * dy]);
    }

    // ── Cursor ──

    CardOverlay.prototype._updateCursor = function (hit) {
        if (!hit) {
            this.canvas.style.cursor = 'default';
            return;
        }
        if (typeof hit === 'string' && hit.indexOf('corner:') === 0) {
            this.canvas.style.cursor = 'move';
            return;
        }
        this.canvas.style.cursor = (hit === 'left' || hit === 'right')
            ? 'ew-resize' : 'ns-resize';
    };

    // ── Pointer events ──

    CardOverlay.prototype._handleDown = function (e) {
        if (e.button !== 0) return;
        var pos = this._toNorm(e);
        var hit = this._hitTest(pos);
        if (!hit) return;

        e.preventDefault();
        e.stopPropagation();

        if (typeof hit === 'string' && hit.indexOf('corner:') === 0) {
            var idx = parseInt(hit.split(':')[1], 10);
            this._drag = { type: 'corner', cornerIdx: idx };
        } else {
            this._drag = { type: 'guide', side: hit };
        }

        this.canvas.setPointerCapture(e.pointerId);
        this.canvas.addEventListener('pointerup', this._onUp);
        this.canvas.addEventListener('pointercancel', this._onCancel);
        this._updateCursor(hit);
        this._render();
    };

    CardOverlay.prototype._handleMove = function (e) {
        var pos = this._toNorm(e);

        if (this._drag) {
            e.preventDefault();
            if (this._drag.type === 'corner') {
                this._applyCornerDrag(pos);
            } else {
                this._applyGuideDrag(pos);
            }
            this._render();
            return;
        }

        var hit = this._hitTest(pos);
        var prev = this._hover;
        this._hover = hit;
        this._updateCursor(hit);
        if (hit !== prev) this._render();
    };

    CardOverlay.prototype._applyCornerDrag = function (pos) {
        if (!this._drag || this._drag.type !== 'corner' || !this.boundary) return;
        var idx = this._drag.cornerIdx;
        this.boundary[idx] = [clamp01(pos[0]), clamp01(pos[1])];
        this._boundaryMode = 'manual';
    };

    CardOverlay.prototype._applyGuideDrag = function (pos) {
        if (!this._drag || !this.boundary) return;

        // Convert image-norm position to card-fraction
        var cardFrac = this._normToCard(pos[0], pos[1]);
        var u = cardFrac[0], v = cardFrac[1];
        var side = this._drag.side;

        if (side === 'left') {
            this.guides.left = clamp(u, 0.005, this.guides.right - 0.02);
        } else if (side === 'right') {
            this.guides.right = clamp(u, this.guides.left + 0.02, 0.995);
        } else if (side === 'top') {
            this.guides.top = clamp(v, 0.005, this.guides.bottom - 0.02);
        } else if (side === 'bottom') {
            this.guides.bottom = clamp(v, this.guides.top + 0.02, 0.995);
        }
    };

    CardOverlay.prototype._handleUp = function (e) {
        if (!this._drag) return;
        e.preventDefault();

        var pos = this._toNorm(e);
        var dragType = this._drag.type;

        if (dragType === 'corner') {
            this._applyCornerDrag(pos);
        } else {
            this._applyGuideDrag(pos);
        }
        this._render();

        this.canvas.removeEventListener('pointerup', this._onUp);
        this.canvas.removeEventListener('pointercancel', this._onCancel);
        try { this.canvas.releasePointerCapture(e.pointerId); } catch (ex) { }

        this._drag = null;
        this._updateCursor(this._hitTest(pos));

        // Fire callbacks
        if (dragType === 'corner') {
            if (this.ref) {
                var corners = this.boundary.map(function (p) { return [p[0], p[1]]; });
                this.ref.invokeMethodAsync('OnBoundaryDragEnd', corners);
            }
        } else {
            var g = this.guides;
            if (this.ref) {
                this.ref.invokeMethodAsync('OnBorderDragEnd', g.left, g.right, g.top, g.bottom);
            }
        }
        this._fireCenteringUpdate();
    };

    CardOverlay.prototype._handleCancel = function (e) {
        if (!this._drag) return;
        this.canvas.removeEventListener('pointerup', this._onUp);
        this.canvas.removeEventListener('pointercancel', this._onCancel);
        try { this.canvas.releasePointerCapture(e.pointerId); } catch (ex) { }

        this._drag = null;
        this._hover = null;
        this.canvas.style.cursor = 'default';
        this._render();

        this.ref.invokeMethodAsync('OnDragCancelled');
    };

    // ── Public API ──

    CardOverlay.prototype.updateData = function (data) {
        this._parseData(data);
        this._ensureBoundary();
        this.dismissed = {};
        this._applyRotation();
        this._render();
        this._fireCenteringUpdate();
    };

    CardOverlay.prototype.dismiss = function (index) {
        this.dismissed[index] = true;
        this._render();
    };

    CardOverlay.prototype.undismiss = function (index) {
        delete this.dismissed[index];
        this._render();
    };

    CardOverlay.prototype.reset = function (data) {
        this._parseData(data);
        this._ensureBoundary();
        this.dismissed = {};
        this._drag = null;
        this._hover = null;
        this.canvas.style.cursor = 'default';
        this._applyRotation();
        this._render();
        this._fireCenteringUpdate();
    };

    CardOverlay.prototype.setBoundary = function (corners) {
        if (corners && corners.length === 4) {
            this.boundary = corners.map(function (p) { return [p[0], p[1]]; });
            this._boundaryMode = 'detected';
        }
        this._render();
        this._fireCenteringUpdate();
    };

    CardOverlay.prototype.setPercentGuides = function (visible, positions) {
        this._percentGuidesVisible = !!visible;
        if (positions && positions.length > 0) {
            this._percentGuidePositions = positions;
        }
        this._render();
    };

    CardOverlay.prototype.getBoundaryMode = function () {
        return this._boundaryMode;
    };

    CardOverlay.prototype.setRotation = function (degrees) {
        this._rotation = degrees;
        this._applyRotation();
        this._render();
        this._fireCenteringUpdate();
    };

    /**
     * Toggle rectified mode. When true, the underlying <img> is treated as the
     * already-rectified card image (server-side perspective warp output) and
     * the boundary is locked to identity. When false, the boundary returns to
     * whatever was last set via _parseData / setBoundary.
     */
    CardOverlay.prototype.setRectifiedMode = function (rectified) {
        var wantRectified = !!rectified;
        if (wantRectified === this._rectified) return;

        this._rectified = wantRectified;

        if (wantRectified) {
            this.boundary = [[0, 0], [1, 0], [1, 1], [0, 1]];
            this._boundaryMode = 'rectified';
            this._rotation = 0;
            this._autoRotated = true;  // suppress auto-rotate
        } else {
            this._autoRotated = false;
            // Boundary will be repopulated by the next updateData/setBoundary call.
        }

        this._applyRotation();
        this._render();
        this._fireCenteringUpdate();
    };

    CardOverlay.prototype.dispose = function () {
        if (!this.canvas) return;
        this.canvas.removeEventListener('pointerdown', this._onDown);
        this.canvas.removeEventListener('pointermove', this._onMove);
        this.canvas.removeEventListener('pointerup', this._onUp);
        this.canvas.removeEventListener('pointercancel', this._onCancel);
        if (this._ro) { this._ro.disconnect(); this._ro = null; }
        if (this._imgLoad) {
            this.img.removeEventListener('load', this._imgLoad);
            this._imgLoad = null;
        }
        if (this.img) { this.img.style.transform = ''; }
        this.ref = null;
        this.canvas = null;
        this.ctx = null;
        this.img = null;
    };

    // ── Global entry point ──

    window.cardOverlay = {
        init: function (dotNetRef, canvasEl, imgEl, data) {
            if (!canvasEl || !imgEl) {
                console.warn('[card-overlay] init: missing canvas or img element');
                return {
                    updateData: function () { }, dismiss: function () { },
                    undismiss: function () { }, reset: function () { },
                    setRotation: function () { }, setRectifiedMode: function () { },
                    setBoundary: function () { }, setPercentGuides: function () { },
                    getBoundaryMode: function () { return 'default'; },
                    dispose: function () { }
                };
            }
            var instance = new CardOverlay(canvasEl, imgEl, dotNetRef, data);
            return {
                updateData:       function (d)           { instance.updateData(d); },
                dismiss:          function (i)           { instance.dismiss(i); },
                undismiss:        function (i)           { instance.undismiss(i); },
                reset:            function (d)           { instance.reset(d); },
                setRotation:      function (deg)         { instance.setRotation(deg); },
                setRectifiedMode: function (r)           { instance.setRectifiedMode(r); },
                setBoundary:      function (corners)     { instance.setBoundary(corners); },
                setPercentGuides: function (vis, pos)    { instance.setPercentGuides(vis, pos); },
                getBoundaryMode:  function ()            { return instance.getBoundaryMode(); },
                dispose:          function ()            { instance.dispose(); }
            };
        }
    };
})();
