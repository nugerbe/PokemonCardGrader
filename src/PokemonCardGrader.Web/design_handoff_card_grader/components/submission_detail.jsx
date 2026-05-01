/* Submission detail page */

const SubmissionDetail = ({ subId, goto }) => {
  const s = SAMPLE_SUBMISSIONS.find(x => x.id === subId) || SAMPLE_SUBMISSIONS[0];
  const [recording, setRecording] = React.useState(false);
  const top = s.estimates[0];

  return (
    <div className="page">
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 20 }}>
        <button className="btn btn-ghost" onClick={() => goto('collection')}>
          <Icon name="chevronL" size={14}/> Collection
        </button>
        <span className="mono" style={{ fontSize: 11, color: 'var(--ink-4)', marginLeft: 'auto' }}>
          {s.id} · submitted {s.date}
        </span>
        <button className="btn btn-ghost"><Icon name="download" size={14}/> Export</button>
        <button className="btn btn-ghost" style={{ color: 'var(--rose)' }}><Icon name="trash" size={14}/></button>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '320px 1fr', gap: 24 }}>
        {/* Left: card image + meta */}
        <div>
          <CardArt card={s.card} />
          <div style={{ marginTop: 14 }}>
            <h1 style={{ fontFamily: 'var(--sans)', fontWeight: 500, fontSize: 24, letterSpacing: '-0.025em', margin: 0 }}>
              {s.card.name}
            </h1>
            <div style={{ color: 'var(--ink-3)', fontSize: 13, marginTop: 4 }}>{s.card.subtitle}</div>
            <div className="mono" style={{ fontSize: 12, color: 'var(--ink-4)', marginTop: 6 }}>
              {s.card.set} · {s.card.setCode} · #{s.card.number}
            </div>
            <div style={{ marginTop: 12, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              <span className="chip">{s.card.rarity}</span>
              <span className="chip accent dot">{s.status}</span>
            </div>
          </div>

          <div style={{ marginTop: 20 }}>
            <div className="mono" style={{ fontSize: 10, letterSpacing: '0.1em', color: 'var(--ink-3)', marginBottom: 8 }}>
              UPLOADED PHOTOS
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
              {['Front', 'Back'].map(side => (
                <div key={side} className="surface" style={{ padding: 6, aspectRatio: '63/88', display: 'flex', flexDirection: 'column' }}>
                  <div style={{ flex: 1 }}><CardArt card={s.card}/></div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', padding: '6px 2px 0' }}>
                    <span style={{ fontSize: 11, fontWeight: 500 }}>{side}</span>
                    <span className="mono" style={{ fontSize: 9, color: 'var(--sage)' }}>ANALYZED</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Right: the main attraction */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          {/* Hero grade */}
          <div className="surface" style={{ padding: 24, display: 'flex', gap: 28, alignItems: 'center' }}>
            <div>
              <div className="mono" style={{ fontSize: 10, letterSpacing: '0.14em', color: 'var(--ink-3)', marginBottom: 4 }}>
                {s.actual ? 'FINAL GRADE' : 'TOP ESTIMATE'}
              </div>
              <div className="mono" style={{ fontSize: 11, color: 'var(--ink-3)' }}>{(s.actual || top).co}</div>
              <div style={{
                fontFamily: 'var(--sans)', fontSize: 120, fontWeight: 500,
                letterSpacing: '-0.06em', lineHeight: 0.9, marginTop: 4,
                color: s.actual ? 'var(--ink)' : 'var(--ink)',
              }}>
                {(s.actual ? s.actual.grade : top.grade).toFixed(1)}
              </div>
              <div style={{ fontSize: 14, color: 'var(--ink-2)', marginTop: 4 }}>
                {s.actual ? labelFor2(s.actual.grade) : top.label}
              </div>
            </div>
            <div style={{ width: 1, background: 'var(--rule)', alignSelf: 'stretch' }} />
            <div style={{ flex: 1 }}>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12 }}>
                {(s.sub || { Centering: 9, Corners: 9, Edges: 9, Surface: 8.5 }) &&
                  Object.entries(s.sub || { Centering: 9, Corners: 9, Edges: 9, Surface: 8.5 }).map(([k, v]) => (
                    <div key={k}>
                      <div className="mono" style={{ fontSize: 9, letterSpacing: '0.1em', color: 'var(--ink-3)' }}>
                        {k.toUpperCase()}
                      </div>
                      <div style={{
                        fontFamily: 'var(--sans)', fontSize: 30, fontWeight: 500,
                        letterSpacing: '-0.03em', lineHeight: 1, marginTop: 4,
                      }}>
                        {v.toFixed(1)}
                      </div>
                      <div className="bar" style={{ marginTop: 8 }}>
                        <span style={{ width: `${v * 10}%` }}/>
                      </div>
                    </div>
                  ))}
              </div>
              {s.actual && (
                <div style={{ marginTop: 18, padding: 12, background: 'var(--sage-soft)', borderRadius: 10, display: 'flex', gap: 12, alignItems: 'center' }}>
                  <Icon name="check" size={18} stroke={2.5} style={{ color: 'var(--sage)' }}/>
                  <div>
                    <div style={{ fontWeight: 500, fontSize: 13 }}>Actual grade recorded</div>
                    <div className="mono" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
                      Cert #{s.actual.cert} · {s.actual.co} · recorded Apr 19, 2026
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* Estimates */}
          <div className="surface">
            <div className="surface-h">
              <h3>Estimates by company</h3>
              <span className="chip" style={{ marginLeft: 'auto' }}>
                <Icon name="sparkle" size={11} /> ML + rule-based
              </span>
            </div>
            <div style={{ padding: '6px 0' }}>
              {s.estimates.map((e, i) => (
                <div key={e.co} style={{
                  padding: '14px 18px',
                  borderTop: i > 0 ? '1px solid var(--rule)' : 'none',
                  display: 'grid',
                  gridTemplateColumns: '60px 70px 1fr 140px 90px',
                  gap: 16, alignItems: 'center',
                }}>
                  <span style={{ fontWeight: 600, fontSize: 13, letterSpacing: '-0.01em' }}>{e.co}</span>
                  <span style={{
                    fontFamily: 'var(--sans)', fontSize: 28, fontWeight: 500,
                    letterSpacing: '-0.03em', lineHeight: 1,
                  }}>
                    {e.grade.toFixed(1)}
                  </span>
                  <span style={{ color: 'var(--ink-3)', fontSize: 13 }}>{e.label}</span>
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 11, marginBottom: 4 }}>
                      <span className="mono" style={{ color: 'var(--ink-3)' }}>confidence</span>
                      <span className="mono" style={{ fontWeight: 500 }}>{Math.round(e.conf * 100)}%</span>
                    </div>
                    <div className="bar accent">
                      <span style={{ width: `${e.conf * 100}%` }}/>
                    </div>
                  </div>
                  <span className="chip" style={{ justifySelf: 'end' }}>{e.method}</span>
                </div>
              ))}
            </div>
          </div>

          {/* Record actual */}
          {!s.actual && (
            <div className="surface" style={{ background: recording ? 'var(--paper)' : 'var(--canvas-sunk)', borderStyle: recording ? 'solid' : 'dashed' }}>
              {!recording ? (
                <div style={{ padding: 20, display: 'flex', alignItems: 'center', gap: 14 }}>
                  <div style={{
                    width: 38, height: 38, borderRadius: 10,
                    background: 'var(--paper)', display: 'grid', placeItems: 'center',
                    border: '1px solid var(--rule)',
                  }}>
                    <Icon name="target" size={18} />
                  </div>
                  <div style={{ flex: 1 }}>
                    <div style={{ fontWeight: 500, fontSize: 13 }}>Already graded by a company?</div>
                    <div style={{ fontSize: 12, color: 'var(--ink-3)' }}>Record the actual grade to improve future estimates.</div>
                  </div>
                  <button className="btn btn-primary" onClick={() => setRecording(true)}>Record grade</button>
                </div>
              ) : (
                <>
                  <div className="surface-h"><h3>Record actual grade</h3></div>
                  <div className="surface-b">
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 14, marginBottom: 14 }}>
                      <Field label="Company">
                        <select style={selectS}>
                          <option>PSA</option><option>BGS</option><option>CGC</option><option>SGC</option>
                        </select>
                      </Field>
                      <Field label="Grade">
                        <input type="number" step="0.5" min="1" max="10" defaultValue="9" style={selectS} />
                      </Field>
                      <Field label="Cert #">
                        <input type="text" placeholder="Optional" style={selectS} />
                      </Field>
                    </div>
                    <div className="mono" style={{ fontSize: 10, letterSpacing: '0.1em', color: 'var(--ink-3)', marginBottom: 8 }}>
                      SUB-GRADES (OPTIONAL)
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4,1fr)', gap: 10 }}>
                      {['Centering','Corners','Edges','Surface'].map(l => (
                        <Field key={l} label={l}>
                          <input type="number" step="0.5" style={selectS} />
                        </Field>
                      ))}
                    </div>
                    <div style={{ marginTop: 18, display: 'flex', gap: 8 }}>
                      <button className="btn btn-ghost" onClick={() => setRecording(false)}>Cancel</button>
                      <div style={{ marginLeft: 'auto' }} />
                      <button className="btn btn-primary">Save grade</button>
                    </div>
                  </div>
                </>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

const selectS = {
  width: '100%', padding: '8px 10px', fontFamily: 'inherit', fontSize: 13,
  border: '1px solid var(--rule)', borderRadius: 8, background: 'var(--paper)',
};

const Field = ({ label, children }) => (
  <div>
    <label style={{ fontSize: 11, color: 'var(--ink-3)', display: 'block', marginBottom: 6 }}>{label}</label>
    {children}
  </div>
);

function labelFor2(g) {
  if (g >= 10) return 'Gem Mint';
  if (g >= 9) return 'Mint';
  if (g >= 8) return 'Near Mint-Mint';
  return 'Near Mint';
}

window.SubmissionDetail = SubmissionDetail;
