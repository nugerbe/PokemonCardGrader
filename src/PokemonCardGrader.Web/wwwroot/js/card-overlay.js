// =============================================================================
// Centering line-guide editor
// =============================================================================
//
// Renders the original photo on a canvas with two color-coded line sets
// drawn on top:
//   - Cyan lines for the OUTER card edge   (8 endpoints, 4 lines)
//   - Amber lines for the INNER artwork border (8 endpoints, 4 lines)
//
// Endpoint convention (matches AnalysisOverlay on the C# side):
//   index 0,1 → top edge      (TL, TR)
//   index 2,3 → right edge    (TR, BR)
//   index 4,5 → bottom edge   (BR, BL)
//   index 6,7 → left edge     (BL, TL)
//
// Each "corner" appears at two indices (e.g. the top-right point is index 1
// AND index 2). They start coincident; as the user drags, they can drift
// apart so each edge line tilts independently of its neighbour. That's how
// we handle slight rotation / perspective without any actual perspective
// warp — measurement is taken at line midpoints and the math handles tilt.
//
// Constraints (enforced inside the drag handler so the user can't end up
// in a state the server has to validate):
//   - Each OUTER endpoint is clamped to its quadrant of the image.
//     Indices 0 and 7 (TL) stay in [0, 0.5] × [0, 0.5]; index 1+2 (TR) in
//     [0.5, 1] × [0, 0.5]; index 3+4 (BR) in [0.5, 1] × [0.5, 1]; index 5+6
//     (BL) in [0, 0.5] × [0.5, 1]. This prevents users from dragging a
//     "left" endpoint onto the right side of the card and producing a
//     self-intersecting quad.
//   - Each INNER endpoint is clamped to the OUTER guide's bbox with a 1px
//     gap (in normalised space). Inner can never escape outer.
//
// The canvas is the source of truth for guide positions; .NET reads them
// back via JSInvokable on commit. The client computes centering percentages
// (PSA formula) inline and reports them to .NET continuously so the
// readout updates on every drag without a server roundtrip.
// =============================================================================

(function () {
    'use strict';

    const HANDLE_RADIUS = 6;          // visible handle (CSS px)
    const HIT_RADIUS = 18;            // generous hit area for fingers
    const OUTER_COLOR = '#22d3ee';    // cyan
    const INNER_COLOR = '#f59e0b';    // amber
    const HANDLE_FILL = '#0f172a';    // dark inset against bright stroke
    const LINE_WIDTH = 2;
    const ACTIVE_LINE_WIDTH = 3;

    // Corner-pair mapping: each corner appears at two indices.
    // Dragging one must sync the other so lines stay connected.
    //   TL = 0 ↔ 7,  TR = 1 ↔ 2,  BR = 3 ↔ 4,  BL = 5 ↔ 6
    const CORNER_PAIR = [7, 2, 1, 4, 3, 6, 5, 0];

    // Normalised image-space coordinates: x ∈ [0,1], y ∈ [0,1].
    // Endpoint index → quadrant constraint box (xMin, yMin, xMax, yMax).
    // Mapped pairwise: indices that share a corner share a quadrant.
    const OUTER_QUADRANT_BOX = [
        [0.0, 0.0, 0.5, 0.5], // 0: TL
        [0.5, 0.0, 1.0, 0.5], // 1: TR
        [0.5, 0.0, 1.0, 0.5], // 2: TR (start of right edge)
        [0.5, 0.5, 1.0, 1.0], // 3: BR
        [0.5, 0.5, 1.0, 1.0], // 4: BR (start of bottom edge)
        [0.0, 0.5, 0.5, 1.0], // 5: BL
        [0.0, 0.5, 0.5, 1.0], // 6: BL (start of left edge)
        [0.0, 0.0, 0.5, 0.5]  // 7: TL (start of top edge)
    ];

    function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }

    function dist2(ax, ay, bx, by) {
        const dx = ax - bx, dy = ay - by;
        return dx * dx + dy * dy;
    }

    function CardOverlay(canvas, img, dotNetRef) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        this.img = img;
        this.ref = dotNetRef;
        this.dpr = window.devicePixelRatio || 1;

        // Guide state — 8 outer + 8 inner endpoints in normalised image space.
        this.outer = this._defaultOuter();
        this.inner = this._defaultInner();

        // Drag state
        this._dragging = null;     // { set: 'outer'|'inner', index: 0-7 } | null
        this._activeEdge = -1;     // edge index 0-3 hovered/dragged, for line styling

        // Image-fit state — recomputed on resize. Maps canvas pixels ↔ image-norm.
        this._fit = { offsetX: 0, offsetY: 0, scale: 1 };

        // Bind events
        this._onPointerDown = this._onPointerDown.bind(this);
        this._onPointerMove = this._onPointerMove.bind(this);
        this._onPointerUp = this._onPointerUp.bind(this);
        canvas.addEventListener('pointerdown', this._onPointerDown);
        canvas.addEventListener('pointermove', this._onPointerMove);
        canvas.addEventListener('pointerup', this._onPointerUp);
        canvas.addEventListener('pointercancel', this._onPointerUp);
        canvas.style.touchAction = 'none';

        this._resize();
        this._render();
        // Schedule one render after paint to fire the initial centering update
        // so the parent's readout shows a value before any user input.
        requestAnimationFrame(() => this._fireUpdate());
    }

    CardOverlay.prototype._defaultOuter = function () {
        const o = 0.08;
        return [
            { x: o,     y: o     }, { x: 1 - o, y: o     },
            { x: 1 - o, y: o     }, { x: 1 - o, y: 1 - o },
            { x: 1 - o, y: 1 - o }, { x: o,     y: 1 - o },
            { x: o,     y: 1 - o }, { x: o,     y: o     }
        ];
    };

    CardOverlay.prototype._defaultInner = function () {
        const i = 0.13;
        return [
            { x: i,     y: i     }, { x: 1 - i, y: i     },
            { x: 1 - i, y: i     }, { x: 1 - i, y: 1 - i },
            { x: 1 - i, y: 1 - i }, { x: i,     y: 1 - i },
            { x: i,     y: 1 - i }, { x: i,     y: i     }
        ];
    };

    /** Apply guide positions from .NET. Called after server-side detection. */
    CardOverlay.prototype.setOverlay = function (outer, inner) {
        if (Array.isArray(outer) && outer.length === 8) {
            this.outer = outer.map(p => ({ x: clamp(p.x, 0, 1), y: clamp(p.y, 0, 1) }));
        }
        if (Array.isArray(inner) && inner.length === 8) {
            this.inner = inner.map(p => ({ x: clamp(p.x, 0, 1), y: clamp(p.y, 0, 1) }));
        }
        this._render();
        this._fireUpdate();
    };

    /** Returns current state snapshot for .NET to persist on submit. */
    CardOverlay.prototype.snapshot = function () {
        const c = this._computeCentering();
        return {
            outerGuides: this.outer.slice(),
            innerGuides: this.inner.slice(),
            leftRightPercent: c.lr,
            topBottomPercent: c.tb,
            aspectRatioStatus: this._aspectRatioStatus()
        };
    };

    /**
     * Reports whether the user's outer-guide rectangle has a sensible
     * aspect ratio for a standard Pokémon card (2.5" × 3.5", ratio
     * ≈ 0.714). Returns one of:
     *   'ok'          — ratio within 15% of canonical
     *   'unusual'     — ratio outside 15% but within 30%
     *   'wrong'       — ratio off by more than 30%
     *   'undefined'   — guide is degenerate (zero width or height)
     */
    CardOverlay.prototype._aspectRatioStatus = function () {
        const bbox = this._outerBbox();
        const w = bbox.maxX - bbox.minX;
        const h = bbox.maxY - bbox.minY;
        if (w <= 0 || h <= 0) return 'undefined';

        const aspect = w / h;
        const portraitDelta = Math.abs(aspect - 0.714) / 0.714;
        const landscapeDelta = Math.abs(aspect - 1.4) / 1.4;
        const delta = Math.min(portraitDelta, landscapeDelta);

        if (delta <= 0.15) return 'ok';
        if (delta <= 0.30) return 'unusual';
        return 'wrong';
    };

    /** Resize canvas backing store + recompute image fit. */
    CardOverlay.prototype._resize = function () {
        const rect = this.canvas.getBoundingClientRect();
        const w = Math.max(1, rect.width);
        const h = Math.max(1, rect.height);
        this.canvas.width = Math.round(w * this.dpr);
        this.canvas.height = Math.round(h * this.dpr);
        this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);

        if (this.img && this.img.naturalWidth > 0) {
            const imgAspect = this.img.naturalWidth / this.img.naturalHeight;
            const canvasAspect = w / h;
            let drawW, drawH;
            if (imgAspect > canvasAspect) {
                drawW = w;
                drawH = w / imgAspect;
            } else {
                drawH = h;
                drawW = h * imgAspect;
            }
            this._fit = {
                offsetX: (w - drawW) / 2,
                offsetY: (h - drawH) / 2,
                drawW, drawH,
                scale: drawW
            };
        }
    };

    CardOverlay.prototype._normToCanvas = function (p) {
        const f = this._fit;
        return {
            x: f.offsetX + p.x * f.drawW,
            y: f.offsetY + p.y * f.drawH
        };
    };

    CardOverlay.prototype._canvasToNorm = function (cx, cy) {
        const f = this._fit;
        if (!f.drawW || !f.drawH) return { x: 0, y: 0 };
        return {
            x: clamp((cx - f.offsetX) / f.drawW, 0, 1),
            y: clamp((cy - f.offsetY) / f.drawH, 0, 1)
        };
    };

    CardOverlay.prototype._render = function () {
        const ctx = this.ctx;
        const rect = this.canvas.getBoundingClientRect();
        ctx.clearRect(0, 0, rect.width, rect.height);

        if (!this.img || !this.img.naturalWidth) return;

        // Photo
        const f = this._fit;
        ctx.drawImage(this.img, f.offsetX, f.offsetY, f.drawW, f.drawH);

        // Lines + handles for each set
        this._drawGuideSet(this.outer, OUTER_COLOR);
        this._drawGuideSet(this.inner, INNER_COLOR);
    };

    CardOverlay.prototype._drawGuideSet = function (points, color) {
        const ctx = this.ctx;
        const cs = points.map(p => this._normToCanvas(p));

        // 4 lines: (0,1), (2,3), (4,5), (6,7)
        ctx.strokeStyle = color;
        ctx.lineWidth = LINE_WIDTH;
        ctx.lineCap = 'round';
        for (let i = 0; i < 8; i += 2) {
            ctx.beginPath();
            ctx.moveTo(cs[i].x, cs[i].y);
            ctx.lineTo(cs[i + 1].x, cs[i + 1].y);
            ctx.stroke();
        }

        // 8 handles
        for (let i = 0; i < 8; i++) {
            ctx.beginPath();
            ctx.arc(cs[i].x, cs[i].y, HANDLE_RADIUS, 0, Math.PI * 2);
            ctx.fillStyle = HANDLE_FILL;
            ctx.fill();
            ctx.strokeStyle = color;
            ctx.lineWidth = 2;
            ctx.stroke();
        }
    };

    /**
     * Find the closest endpoint to the given canvas point, across both sets.
     * Inner set wins ties because it sits on top visually.
     */
    CardOverlay.prototype._pickHandle = function (cx, cy) {
        const hitR2 = HIT_RADIUS * HIT_RADIUS;
        let best = null;

        // Outer first so inner takes precedence on tie
        for (let i = 0; i < 8; i++) {
            const c = this._normToCanvas(this.outer[i]);
            const d2 = dist2(cx, cy, c.x, c.y);
            if (d2 <= hitR2 && (best == null || d2 < best.d2)) {
                best = { set: 'outer', index: i, d2 };
            }
        }
        for (let i = 0; i < 8; i++) {
            const c = this._normToCanvas(this.inner[i]);
            const d2 = dist2(cx, cy, c.x, c.y);
            if (d2 <= hitR2 && (best == null || d2 <= best.d2)) {
                best = { set: 'inner', index: i, d2 };
            }
        }
        return best;
    };

    CardOverlay.prototype._onPointerDown = function (e) {
        const rect = this.canvas.getBoundingClientRect();
        const cx = e.clientX - rect.left;
        const cy = e.clientY - rect.top;
        const hit = this._pickHandle(cx, cy);
        if (!hit) return;

        e.preventDefault();
        this.canvas.setPointerCapture(e.pointerId);
        this._dragging = hit;
        this._render();
    };

    CardOverlay.prototype._onPointerMove = function (e) {
        if (!this._dragging) return;
        const rect = this.canvas.getBoundingClientRect();
        const cx = e.clientX - rect.left;
        const cy = e.clientY - rect.top;
        const np = this._canvasToNorm(cx, cy);

        const { set, index } = this._dragging;
        const target = (set === 'outer' ? this.outer : this.inner)[index];

        const points = set === 'outer' ? this.outer : this.inner;
        const pair = points[CORNER_PAIR[index]];

        if (set === 'outer') {
            const box = OUTER_QUADRANT_BOX[index];
            target.x = clamp(np.x, box[0], box[2]);
            target.y = clamp(np.y, box[1], box[3]);
            // Sync the paired corner endpoint so lines stay connected
            pair.x = target.x;
            pair.y = target.y;
            // Re-clamp inner to stay inside outer bbox after outer moves
            this._reclampInner();
        } else {
            const ob = this._outerBbox();
            const eps = 0.005;
            target.x = clamp(np.x, ob.minX + eps, ob.maxX - eps);
            target.y = clamp(np.y, ob.minY + eps, ob.maxY - eps);
            // Sync the paired corner endpoint so lines stay connected
            pair.x = target.x;
            pair.y = target.y;
        }

        this._render();
        this._fireUpdate();
    };

    CardOverlay.prototype._onPointerUp = function (e) {
        if (!this._dragging) return;
        try { this.canvas.releasePointerCapture(e.pointerId); } catch (_) { /* noop */ }
        this._dragging = null;
        this._render();
    };

    CardOverlay.prototype._outerBbox = function () {
        let minX = 1, minY = 1, maxX = 0, maxY = 0;
        for (const p of this.outer) {
            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }
        return { minX, minY, maxX, maxY };
    };

    CardOverlay.prototype._reclampInner = function () {
        const ob = this._outerBbox();
        const eps = 0.005;
        for (const p of this.inner) {
            p.x = clamp(p.x, ob.minX + eps, ob.maxX - eps);
            p.y = clamp(p.y, ob.minY + eps, ob.maxY - eps);
        }
    };

    /**
     * PSA-style centering.
     */
    CardOverlay.prototype._computeCentering = function () {
        const oTop = this._mid(this.outer[0], this.outer[1]);
        const oRight = this._mid(this.outer[2], this.outer[3]);
        const oBottom = this._mid(this.outer[4], this.outer[5]);
        const oLeft = this._mid(this.outer[6], this.outer[7]);
        const iTop = this._mid(this.inner[0], this.inner[1]);
        const iRight = this._mid(this.inner[2], this.inner[3]);
        const iBottom = this._mid(this.inner[4], this.inner[5]);
        const iLeft = this._mid(this.inner[6], this.inner[7]);

        const topGap = Math.max(0, iTop.y - oTop.y);
        const bottomGap = Math.max(0, oBottom.y - iBottom.y);
        const leftGap = Math.max(0, iLeft.x - oLeft.x);
        const rightGap = Math.max(0, oRight.x - iRight.x);

        return {
            lr: this._safePct(leftGap, rightGap),
            tb: this._safePct(topGap, bottomGap)
        };
    };

    CardOverlay.prototype._mid = function (a, b) {
        return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
    };

    CardOverlay.prototype._safePct = function (a, b) {
        const t = a + b;
        if (t < 1e-9) return 50;
        return Math.round(Math.max(a, b) / t * 1000) / 10;
    };

    CardOverlay.prototype._fireUpdate = function () {
        if (!this.ref) return;
        const c = this._computeCentering();
        const aspect = this._aspectRatioStatus();
        try { this.ref.invokeMethodAsync('OnOverlayChanged', c.lr, c.tb, aspect); }
        catch (_) { /* ref disposed mid-flight */ }
    };

    CardOverlay.prototype.dispose = function () {
        this.canvas.removeEventListener('pointerdown', this._onPointerDown);
        this.canvas.removeEventListener('pointermove', this._onPointerMove);
        this.canvas.removeEventListener('pointerup', this._onPointerUp);
        this.canvas.removeEventListener('pointercancel', this._onPointerUp);
        this.ref = null;
    };

    // =============================================================================
    // Module exports — instance registry keyed by canvas-id
    // =============================================================================
    const instances = new Map();

    function init(canvasId, imageId, dotNetRef) {
        const canvas = document.getElementById(canvasId);
        const img = document.getElementById(imageId);
        if (!canvas || !img) return false;

        const start = () => {
            const inst = new CardOverlay(canvas, img, dotNetRef);
            instances.set(canvasId, inst);
            const ro = new ResizeObserver(() => {
                inst._resize();
                inst._render();
            });
            ro.observe(canvas);
            inst._resizeObserver = ro;
        };

        if (img.complete && img.naturalWidth > 0) start();
        else img.addEventListener('load', start, { once: true });

        return true;
    }

    function setOverlay(canvasId, outer, inner) {
        const inst = instances.get(canvasId);
        if (inst) inst.setOverlay(outer, inner);
    }

    function snapshot(canvasId) {
        const inst = instances.get(canvasId);
        return inst ? inst.snapshot() : null;
    }

    function dispose(canvasId) {
        const inst = instances.get(canvasId);
        if (!inst) return;
        if (inst._resizeObserver) inst._resizeObserver.disconnect();
        inst.dispose();
        instances.delete(canvasId);
    }

    window.cardOverlay = { init, setOverlay, snapshot, dispose };
})();
