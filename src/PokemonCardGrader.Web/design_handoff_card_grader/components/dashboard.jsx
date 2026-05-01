/* Dashboard (Overview) page */

const Dashboard = ({ goto }) => {
  const totalSub = SAMPLE_SUBMISSIONS.length + 46;
  const graded = SAMPLE_SUBMISSIONS.filter(s => s.actual).length + 21;
  const avg = 9.1;

  return (
    <div className="page">
      <h1 className="display">Overview</h1>
      <p className="page-sub">At-a-glance performance across all your submissions.</p>

      {/* Top stat row */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 16, marginBottom: 20 }}>
        <div className="surface stat">
          <span className="label">Submissions</span>
          <span className="val num">{totalSub}</span>
          <span className="delta">↑ 8 this month</span>
        </div>
        <div className="surface stat">
          <span className="label">Graded</span>
          <span className="val num">{graded}</span>
          <span className="delta">52% of submissions</span>
        </div>
        <div className="surface stat accent">
          <span className="label">Avg estimated grade</span>
          <span className="val num">{avg.toFixed(1)}</span>
          <span className="delta">vs actual 8.9</span>
        </div>
        <div className="surface stat">
          <span className="label">Estimate accuracy</span>
          <span className="val num">91%</span>
          <span className="delta">within ±0.5 grade</span>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1.4fr 1fr', gap: 16, marginBottom: 20 }}>
        {/* Distribution chart */}
        <div className="surface">
          <div className="surface-h">
            <h3>Grade distribution</h3>
            <span className="chip">PSA estimates</span>
            <div style={{ marginLeft: 'auto' }}>
              <button className="btn btn-ghost" style={{ padding: '4px 10px', fontSize: 11 }}>6 months</button>
            </div>
          </div>
          <div className="surface-b">
            <div style={{ display: 'flex', alignItems: 'flex-end', gap: 16, height: 220, padding: '8px 4px' }}>
              {GRADE_DIST.map(d => {
                const max = Math.max(...GRADE_DIST.map(x => x.n));
                const pct = d.n / max;
                return (
                  <div key={d.g} style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
                    <div className="num" style={{ fontFamily: 'var(--mono)', fontSize: 11, color: 'var(--ink-3)' }}>{d.n}</div>
                    <div style={{
                      width: '100%',
                      height: `${pct * 100}%`,
                      minHeight: 4,
                      background: d.g >= 9 ? 'var(--ink)' : 'var(--canvas-sunk)',
                      borderRadius: '4px 4px 2px 2px',
                      position: 'relative',
                    }}>
                      {d.g === 9 && (
                        <div style={{
                          position: 'absolute', top: -24, left: '50%', transform: 'translateX(-50%)',
                          background: 'var(--accent)', color: 'var(--accent-ink)',
                          padding: '2px 6px', borderRadius: 4, fontSize: 10, fontWeight: 500, whiteSpace: 'nowrap',
                        }}>mode</div>
                      )}
                    </div>
                    <div className="num" style={{ fontFamily: 'var(--sans)', fontSize: 13, fontWeight: 500, letterSpacing: '-0.02em' }}>{d.g}</div>
                  </div>
                );
              })}
            </div>
          </div>
        </div>

        {/* Calibration panel */}
        <div className="surface">
          <div className="surface-h">
            <h3>Estimate vs actual</h3>
            <span className="chip sage dot">Calibrated</span>
          </div>
          <div className="surface-b" style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            {[
              { name: 'Centering', est: 9.1, act: 8.9 },
              { name: 'Corners',   est: 9.0, act: 9.1 },
              { name: 'Edges',     est: 8.8, act: 8.7 },
              { name: 'Surface',   est: 8.7, act: 8.5 },
            ].map(row => (
              <div key={row.name}>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6, fontSize: 12 }}>
                  <span style={{ color: 'var(--ink-2)', fontWeight: 500 }}>{row.name}</span>
                  <span className="mono" style={{ color: 'var(--ink-3)' }}>est {row.est.toFixed(1)} · act {row.act.toFixed(1)}</span>
                </div>
                <div style={{ position: 'relative', height: 6, background: 'var(--canvas-sunk)', borderRadius: 3 }}>
                  <div style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: `${row.est * 10}%`, background: 'var(--ink)', borderRadius: 3, opacity: 0.9 }} />
                  <div style={{ position: 'absolute', left: `${row.act * 10}%`, top: -2, width: 2, height: 10, background: 'var(--accent)' }} />
                </div>
              </div>
            ))}
            <hr className="hr-dotted" />
            <div style={{ fontSize: 12, color: 'var(--ink-3)', lineHeight: 1.5 }}>
              Our ML engine trends <strong style={{ color: 'var(--ink)' }}>+0.18 optimistic</strong> on surface scores. Recalibrate in Settings.
            </div>
          </div>
        </div>
      </div>

      {/* Recent */}
      <div className="surface">
        <div className="surface-h">
          <h3>Recent submissions</h3>
          <button className="btn btn-ghost" style={{ marginLeft: 'auto', padding: '4px 10px', fontSize: 11 }}
            onClick={() => goto('submissions')}>
            View all <Icon name="arrow" size={12} />
          </button>
        </div>
        <table className="clean">
          <thead>
            <tr>
              <th>Card</th>
              <th>Set</th>
              <th>Submitted</th>
              <th>Top estimate</th>
              <th>Actual</th>
              <th style={{ width: 40 }}></th>
            </tr>
          </thead>
          <tbody>
            {SAMPLE_SUBMISSIONS.slice(0, 5).map(s => {
              const top = s.estimates[0];
              return (
                <tr key={s.id} onClick={() => goto('detail', s.id)}>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                      <div style={{ width: 34, height: 48, flexShrink: 0 }}>
                        <CardArt card={s.card} size="sm" />
                      </div>
                      <div>
                        <div style={{ fontWeight: 500 }}>{s.card.name}</div>
                        <div className="mono" style={{ fontSize: 11, color: 'var(--ink-4)' }}>{s.id}</div>
                      </div>
                    </div>
                  </td>
                  <td>
                    <div>{s.card.set}</div>
                    <div className="mono" style={{ fontSize: 11, color: 'var(--ink-4)' }}>{s.card.number}</div>
                  </td>
                  <td><span className="mono" style={{ color: 'var(--ink-3)' }}>{s.date}</span></td>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <span className="mono" style={{ fontSize: 11, color: 'var(--ink-3)' }}>{top.co}</span>
                      <span style={{ fontFamily: 'var(--sans)', fontWeight: 500, fontSize: 16, letterSpacing: '-0.02em' }}>{top.grade.toFixed(1)}</span>
                      <span className="chip" style={{ fontSize: 10 }}>{Math.round(top.conf * 100)}%</span>
                    </div>
                  </td>
                  <td>
                    {s.actual ? (
                      <span className="chip sage dot">{s.actual.co} {s.actual.grade}</span>
                    ) : (
                      <span style={{ color: 'var(--ink-4)', fontSize: 12 }}>Pending</span>
                    )}
                  </td>
                  <td><Icon name="chevron" size={14} style={{ color: 'var(--ink-4)' }} /></td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
};

window.Dashboard = Dashboard;
