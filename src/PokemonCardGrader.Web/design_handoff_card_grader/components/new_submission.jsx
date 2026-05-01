/* New Submission — 4-step wizard */

const Stepper = ({ step, steps, setStep }) => (
  <div style={{
    display: 'flex', gap: 0, marginBottom: 28, background: 'var(--paper)',
    border: '1px solid var(--rule)', borderRadius: 'var(--r-lg)', padding: 6,
  }}>
    {steps.map((s, i) => {
      const idx = i + 1;
      const active = step === idx;
      const done = step > idx;
      const clickable = done;
      return (
        <button key={s}
          disabled={!clickable && !active}
          onClick={() => clickable && setStep(idx)}
          style={{
            flex: 1, border: 'none',
            background: active ? 'var(--canvas-sunk)' : 'transparent',
            color: active ? 'var(--ink)' : done ? 'var(--ink-2)' : 'var(--ink-4)',
            padding: '10px 14px', borderRadius: 10,
            cursor: clickable ? 'pointer' : 'default',
            display: 'flex', alignItems: 'center', gap: 10,
            fontSize: 13, fontWeight: 500, fontFamily: 'inherit', textAlign: 'left',
          }}>
          <div style={{
            width: 22, height: 22, borderRadius: 11,
            background: active ? 'var(--ink)' : done ? 'var(--sage)' : 'var(--canvas-sunk)',
            color: active ? 'var(--canvas)' : done ? 'var(--paper)' : 'var(--ink-4)',
            display: 'grid', placeItems: 'center',
            fontSize: 11, fontWeight: 600,
            border: active ? 'none' : done ? 'none' : '1px solid var(--rule)',
          }}>
            {done ? <Icon name="check" size={11} stroke={2.5}/> : idx}
          </div>
          <span>{s}</span>
        </button>
      );
    })}
  </div>
);

const NewSubmission = ({ goto }) => {
  const [step, setStep] = React.useState(1);
  const [card, setCard] = React.useState(null);
  const [method, setMethod] = React.useState('images');
  const [q, setQ] = React.useState('');
  const [scores, setScores] = React.useState({
    centerFLR: 50, centerFTB: 50, centerBLR: 50, centerBTB: 50,
    corners: 8.5, edges: 8.5, surface: 8.5,
  });
  const [notes, setNotes] = React.useState('');
  const [frontUp, setFrontUp] = React.useState(false);
  const [backUp, setBackUp] = React.useState(false);
  const [analyzing, setAnalyzing] = React.useState(false);
  const [analyzed, setAnalyzed] = React.useState(false);

  const results = q ? SAMPLE_CARDS.filter(c => c.name.toLowerCase().includes(q.toLowerCase())) : SAMPLE_CARDS;

  const handleUpload = (which) => {
    if (which === 'front') setFrontUp(true); else setBackUp(true);
    const both = (which === 'front' ? true : frontUp) && (which === 'back' ? true : backUp);
    if (both) {
      setAnalyzing(true);
      setTimeout(() => {
        setAnalyzing(false); setAnalyzed(true);
        setScores(s => ({ ...s, corners: 9.0, edges: 8.5, surface: 9.0, centerFLR: 52, centerFTB: 48 }));
      }, 2200);
    }
  };

  return (
    <div className="page">
      <div style={{ display: 'flex', alignItems: 'flex-end', marginBottom: 20 }}>
        <div>
          <h1 className="display">New submission</h1>
          <p className="page-sub" style={{ marginBottom: 0 }}>Grade a single card for one or more grading companies.</p>
        </div>
        <button className="btn btn-ghost" style={{ marginLeft: 'auto' }} onClick={() => goto('collection')}>Cancel</button>
      </div>

      <Stepper step={step} setStep={setStep}
        steps={['Select card', 'Input method', 'Provide scores', 'Review']} />

      {step === 1 && (
        <div className="surface">
          <div className="surface-h">
            <h3>Find your card</h3>
            <span className="mono" style={{ fontSize: 11, color: 'var(--ink-4)', marginLeft: 'auto' }}>
              {results.length} results
            </span>
          </div>
          <div className="surface-b">
            <div className="search" style={{ marginBottom: 20, minWidth: 0 }}>
              <Icon name="search" size={14} style={{ color: 'var(--ink-4)' }}/>
              <input autoFocus placeholder="Search name, number, or set…" value={q} onChange={e => setQ(e.target.value)} />
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))', gap: 16 }}>
              {results.map(c => (
                <button key={c.id} onClick={() => { setCard(c); setStep(2); }}
                  style={{
                    border: card?.id === c.id ? '2px solid var(--ink)' : '1px solid var(--rule)',
                    background: 'var(--paper)', padding: 10, borderRadius: 12, cursor: 'pointer',
                    textAlign: 'left', fontFamily: 'inherit',
                  }}>
                  <CardArt card={c} />
                  <div style={{ marginTop: 8, fontWeight: 500, fontSize: 13 }}>{c.name}</div>
                  <div className="mono" style={{ fontSize: 10.5, color: 'var(--ink-4)', marginTop: 2 }}>
                    {c.setCode} · {c.number}
                  </div>
                </button>
              ))}
            </div>
          </div>
        </div>
      )}

      {step === 2 && (
        <div className="surface">
          <div className="surface-h"><h3>How do you want to score this card?</h3></div>
          <div className="surface-b" style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 14 }}>
            {[
              { k: 'images', title: 'Upload photos', icon: 'camera', body: 'Front and back. Our vision model measures centering, corners, edges, surface.', chip: 'Recommended' },
              { k: 'manual', title: 'Score manually', icon: 'settings', body: 'Use guided sliders — fastest if you already know the grades.' },
              { k: 'both',   title: 'Photos + fine-tune', icon: 'sparkle', body: 'Start from AI measurements, adjust whatever looks off.', chip: 'Best' },
            ].map(opt => (
              <button key={opt.k} onClick={() => setMethod(opt.k)}
                style={{
                  border: method === opt.k ? '2px solid var(--ink)' : '1px solid var(--rule)',
                  padding: 18, borderRadius: 12, background: method === opt.k ? 'var(--canvas-sunk)' : 'var(--paper)',
                  cursor: 'pointer', textAlign: 'left', fontFamily: 'inherit',
                  position: 'relative',
                }}>
                {opt.chip && (
                  <span style={{
                    position: 'absolute', top: 12, right: 12,
                    fontSize: 10, padding: '2px 7px', background: 'var(--accent-soft)',
                    color: 'var(--accent-ink)', borderRadius: 99, fontWeight: 500,
                  }}>{opt.chip}</span>
                )}
                <div style={{
                  width: 36, height: 36, borderRadius: 10,
                  background: 'var(--canvas-sunk)', display: 'grid', placeItems: 'center',
                  marginBottom: 14,
                }}>
                  <Icon name={opt.icon} size={18} />
                </div>
                <div style={{ fontWeight: 500, fontSize: 14, marginBottom: 4 }}>{opt.title}</div>
                <div style={{ color: 'var(--ink-3)', fontSize: 12, lineHeight: 1.5 }}>{opt.body}</div>
              </button>
            ))}
          </div>
          <div style={{ padding: '0 18px 18px', display: 'flex', gap: 8 }}>
            <button className="btn btn-ghost" onClick={() => setStep(1)}><Icon name="chevronL" size={14}/> Back</button>
            <div style={{ marginLeft: 'auto' }} />
            <button className="btn btn-primary" onClick={() => setStep(3)}>Continue <Icon name="arrow" size={14}/></button>
          </div>
        </div>
      )}

      {step === 3 && (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 280px', gap: 16 }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {(method === 'images' || method === 'both') && (
              <div className="surface">
                <div className="surface-h">
                  <h3>Upload images</h3>
                  {analyzing && <span className="chip accent dot">Analyzing</span>}
                  {analyzed && <span className="chip sage dot">Analysis complete</span>}
                </div>
                <div className="surface-b">
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
                    {[
                      { k: 'front', label: 'Front', up: frontUp },
                      { k: 'back',  label: 'Back',  up: backUp },
                    ].map(s => (
                      <div key={s.k}
                        onClick={() => !s.up && handleUpload(s.k)}
                        style={{
                          border: `1.5px dashed ${s.up ? 'var(--sage)' : 'var(--rule-strong)'}`,
                          borderRadius: 12, padding: 16, aspectRatio: '63/88',
                          display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                          cursor: s.up ? 'default' : 'pointer',
                          background: s.up ? 'var(--sage-soft)' : 'var(--canvas-sunk)',
                          textAlign: 'center',
                        }}>
                        {s.up ? (
                          <>
                            <Icon name="check" size={24} stroke={2} style={{ color: 'var(--sage)' }} />
                            <div style={{ marginTop: 8, fontWeight: 500, fontSize: 13 }}>{s.label} uploaded</div>
                            <div className="mono" style={{ fontSize: 11, color: 'var(--ink-3)', marginTop: 2 }}>card_{s.k}_2026.jpg</div>
                          </>
                        ) : (
                          <>
                            <Icon name="upload" size={22} style={{ color: 'var(--ink-3)' }} />
                            <div style={{ marginTop: 10, fontWeight: 500 }}>Drop {s.label.toLowerCase()} photo</div>
                            <div style={{ fontSize: 12, color: 'var(--ink-3)', marginTop: 2 }}>or click to browse</div>
                            <div className="mono" style={{ fontSize: 10, color: 'var(--ink-4)', marginTop: 10, letterSpacing: '0.08em' }}>
                              JPG · PNG · HEIC · up to 20 MB
                            </div>
                          </>
                        )}
                      </div>
                    ))}
                  </div>

                  {analyzing && (
                    <div style={{ marginTop: 16, padding: 14, background: 'var(--accent-soft)', borderRadius: 10, display: 'flex', gap: 12, alignItems: 'center' }}>
                      <div style={{
                        width: 20, height: 20, border: '2px solid var(--accent-ink)',
                        borderTopColor: 'transparent', borderRadius: 10,
                        animation: 'spin 0.8s linear infinite',
                      }} />
                      <div>
                        <div style={{ fontWeight: 500, fontSize: 13, color: 'var(--accent-ink)' }}>Reading measurements</div>
                        <div style={{ fontSize: 12, color: 'var(--accent-ink)', opacity: 0.7 }}>Detecting edges, centering, and surface defects…</div>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            )}

            {(method === 'manual' || method === 'both') && (
              <>
                <div className="surface">
                  <div className="surface-h">
                    <h3>Centering</h3>
                    <span className="mono" style={{ fontSize: 11, color: 'var(--ink-4)', marginLeft: 'auto' }}>
                      50% = perfectly centered
                    </span>
                  </div>
                  <div className="surface-b" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 20 }}>
                    {[
                      ['centerFLR', 'Front · Left / Right'],
                      ['centerFTB', 'Front · Top / Bottom'],
                      ['centerBLR', 'Back · Left / Right'],
                      ['centerBTB', 'Back · Top / Bottom'],
                    ].map(([k, label]) => (
                      <div key={k}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, marginBottom: 8 }}>
                          <span style={{ color: 'var(--ink-2)' }}>{label}</span>
                          <span className="mono" style={{ fontWeight: 500 }}>{scores[k].toFixed(0)}%</span>
                        </div>
                        <input type="range" className="slider" min={30} max={70} step={1}
                          value={scores[k]}
                          onChange={e => setScores(s => ({ ...s, [k]: +e.target.value }))} />
                      </div>
                    ))}
                  </div>
                </div>

                <div className="surface">
                  <div className="surface-h">
                    <h3>Physical condition</h3>
                    <span className="mono" style={{ fontSize: 11, color: 'var(--ink-4)', marginLeft: 'auto' }}>
                      1–10 · 0.5 steps
                    </span>
                  </div>
                  <div className="surface-b" style={{ display: 'grid', gridTemplateColumns: 'repeat(3,1fr)', gap: 24 }}>
                    {[
                      ['corners', 'Corners'],
                      ['edges', 'Edges'],
                      ['surface', 'Surface'],
                    ].map(([k, label]) => (
                      <div key={k}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 10 }}>
                          <span style={{ color: 'var(--ink-2)', fontSize: 12 }}>{label}</span>
                          <span style={{ fontFamily: 'var(--sans)', fontSize: 22, fontWeight: 500, letterSpacing: '-0.03em' }}>
                            {scores[k].toFixed(1)}
                          </span>
                        </div>
                        <input type="range" className="slider accent" min={1} max={10} step={0.5}
                          value={scores[k]}
                          onChange={e => setScores(s => ({ ...s, [k]: +e.target.value }))} />
                      </div>
                    ))}
                  </div>
                </div>
              </>
            )}

            <div className="surface">
              <div className="surface-b">
                <label style={{ fontSize: 12, fontWeight: 500, color: 'var(--ink-2)', display: 'block', marginBottom: 8 }}>
                  Notes <span style={{ color: 'var(--ink-4)', fontWeight: 400 }}>(optional)</span>
                </label>
                <textarea rows={3} value={notes} onChange={e => setNotes(e.target.value)}
                  placeholder="Any chips, scratches, print lines, or other observations…"
                  style={{
                    width: '100%', border: '1px solid var(--rule)', borderRadius: 8,
                    padding: 10, fontFamily: 'inherit', fontSize: 13, resize: 'vertical',
                    background: 'var(--canvas)',
                  }} />
              </div>
            </div>

            <div style={{ display: 'flex', gap: 8 }}>
              <button className="btn btn-ghost" onClick={() => setStep(2)}><Icon name="chevronL" size={14}/> Back</button>
              <div style={{ marginLeft: 'auto' }} />
              <button className="btn btn-primary" onClick={() => setStep(4)}>
                Review submission <Icon name="arrow" size={14}/>
              </button>
            </div>
          </div>

          <aside style={{ position: 'sticky', top: 84, alignSelf: 'start' }}>
            <div className="surface">
              <div style={{ padding: 14 }}>
                <CardArt card={card} />
              </div>
              <div style={{ padding: '0 14px 14px' }}>
                <div style={{ fontWeight: 500, fontSize: 13 }}>{card?.name}</div>
                <div className="mono" style={{ fontSize: 11, color: 'var(--ink-3)', marginTop: 2 }}>{card?.setCode} · {card?.number}</div>
              </div>
              <div className="divider"/>
              <div style={{ padding: 14 }}>
                <div className="mono" style={{ fontSize: 10, letterSpacing: '0.1em', color: 'var(--ink-3)', marginBottom: 10 }}>
                  LIVE ESTIMATE · PSA
                </div>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: 10 }}>
                  <span style={{ fontFamily: 'var(--sans)', fontSize: 48, fontWeight: 500, letterSpacing: '-0.04em', lineHeight: 1 }}>
                    {estimateFrom(scores).toFixed(1)}
                  </span>
                  <span className="chip accent">~{(0.6 + (analyzed ? 0.25 : 0)).toFixed(2) * 100 | 0}% conf.</span>
                </div>
                <div style={{ marginTop: 6, fontSize: 12, color: 'var(--ink-3)' }}>
                  {labelFor(estimateFrom(scores))}
                </div>
              </div>
            </div>
          </aside>
        </div>
      )}

      {step === 4 && (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
          <div className="surface">
            <div className="surface-h"><h3>Card</h3></div>
            <div className="surface-b">
              <div style={{ maxWidth: 240, margin: '0 auto 16px' }}>
                <CardArt card={card} />
              </div>
              <div style={{ textAlign: 'center' }}>
                <div style={{ fontFamily: 'var(--sans)', fontSize: 20, fontWeight: 500, letterSpacing: '-0.02em' }}>{card?.name}</div>
                <div className="mono" style={{ fontSize: 12, color: 'var(--ink-3)', marginTop: 4 }}>
                  {card?.set} · {card?.setCode} · {card?.number}
                </div>
              </div>
            </div>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <div className="surface">
              <div className="surface-h"><h3>Estimated grades</h3></div>
              <div className="surface-b" style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
                <GradeBadge co="PSA" grade={estimateFrom(scores)} lead size="lg" />
                <GradeBadge co="BGS" grade={estimateFrom(scores) + 0.5 > 10 ? 10 : estimateFrom(scores) + 0.5} />
                <GradeBadge co="CGC" grade={estimateFrom(scores)} />
                <GradeBadge co="SGC" grade={estimateFrom(scores) - 0.5} />
              </div>
            </div>

            <div className="surface">
              <div className="surface-h"><h3>Scores</h3></div>
              <div className="surface-b" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', rowGap: 10, columnGap: 20, fontSize: 13 }}>
                <Row label="Centering · Front L/R" val={`${scores.centerFLR.toFixed(0)}%`} />
                <Row label="Centering · Front T/B" val={`${scores.centerFTB.toFixed(0)}%`} />
                <Row label="Centering · Back L/R"  val={`${scores.centerBLR.toFixed(0)}%`} />
                <Row label="Centering · Back T/B"  val={`${scores.centerBTB.toFixed(0)}%`} />
                <Row label="Corners" val={scores.corners.toFixed(1)} />
                <Row label="Edges"   val={scores.edges.toFixed(1)} />
                <Row label="Surface" val={scores.surface.toFixed(1)} />
                <Row label="Method"  val={method === 'both' ? 'Photos + manual' : method === 'images' ? 'Photos' : 'Manual'} />
              </div>
            </div>

            <div style={{ display: 'flex', gap: 8 }}>
              <button className="btn btn-ghost" onClick={() => setStep(3)}><Icon name="chevronL" size={14}/> Back</button>
              <div style={{ marginLeft: 'auto' }} />
              <button className="btn btn-accent btn-lg" onClick={() => goto('detail', SAMPLE_SUBMISSIONS[0].id)}>
                Submit for grading <Icon name="arrow" size={14}/>
              </button>
            </div>
          </div>
        </div>
      )}

      <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
    </div>
  );
};

const Row = ({ label, val }) => (
  <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px dashed var(--rule)', padding: '6px 0' }}>
    <span style={{ color: 'var(--ink-3)' }}>{label}</span>
    <span className="mono" style={{ fontWeight: 500 }}>{val}</span>
  </div>
);

// Simple heuristic estimator so UI feels live
function estimateFrom(s) {
  const centerDev = (Math.abs(s.centerFLR - 50) + Math.abs(s.centerFTB - 50)) / 2;
  const centerScore = 10 - centerDev * 0.15;
  const avg = (centerScore + s.corners + s.edges + s.surface) / 4;
  return Math.max(1, Math.min(10, Math.round(avg * 2) / 2));
}
function labelFor(g) {
  if (g >= 10) return 'Gem Mint — flawless';
  if (g >= 9.5) return 'Gem Mint — near-flawless';
  if (g >= 9) return 'Mint — minor imperfections';
  if (g >= 8) return 'Near Mint-Mint';
  if (g >= 7) return 'Near Mint';
  return 'Excellent';
}

window.NewSubmission = NewSubmission;
