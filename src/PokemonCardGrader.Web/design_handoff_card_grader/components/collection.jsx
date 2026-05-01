/* Collection grid page */

const Collection = ({ goto }) => {
  const [view, setView] = React.useState('grid');
  const [filter, setFilter] = React.useState('all');

  const items = SAMPLE_SUBMISSIONS;
  const filtered = filter === 'all' ? items
    : filter === 'graded' ? items.filter(i => i.actual)
    : items.filter(i => !i.actual);

  return (
    <div className="page">
      <div style={{ display: 'flex', alignItems: 'flex-end', marginBottom: 24 }}>
        <div>
          <h1 className="display">Collection</h1>
          <p className="page-sub" style={{ marginBottom: 0 }}>{items.length} cards · updated 2 minutes ago</p>
        </div>
        <div style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
          <button className="btn btn-ghost"><Icon name="download" size={14} /> Export</button>
          <button className="btn btn-primary" onClick={() => goto('new')}>
            <Icon name="plus" size={14} /> New submission
          </button>
        </div>
      </div>

      {/* Filter bar */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 20, padding: '10px 14px', background: 'var(--paper)', border: '1px solid var(--rule)', borderRadius: 'var(--r-md)' }}>
        <div style={{ display: 'flex', gap: 4, padding: 2, background: 'var(--canvas-sunk)', borderRadius: 8 }}>
          {['all','estimated','graded'].map(f => (
            <button key={f} onClick={() => setFilter(f)}
              style={{
                border: 'none', padding: '5px 12px', fontSize: 12, fontWeight: 500,
                background: filter === f ? 'var(--paper)' : 'transparent',
                color: filter === f ? 'var(--ink)' : 'var(--ink-3)',
                borderRadius: 6, cursor: 'pointer',
                boxShadow: filter === f ? 'var(--shadow-sm)' : 'none',
                textTransform: 'capitalize',
              }}>{f}</button>
          ))}
        </div>
        <span className="mono" style={{ fontSize: 11, color: 'var(--ink-4)', marginLeft: 8 }}>·</span>
        <button className="btn btn-ghost" style={{ padding: '4px 10px', fontSize: 12 }}>Set: All</button>
        <button className="btn btn-ghost" style={{ padding: '4px 10px', fontSize: 12 }}>Grade: 7.0+</button>
        <button className="btn btn-ghost" style={{ padding: '4px 10px', fontSize: 12 }}>Date: All time</button>
        <div style={{ marginLeft: 'auto', display: 'flex', gap: 4 }}>
          <button onClick={() => setView('grid')} className="btn btn-ghost" style={{ padding: 6, background: view === 'grid' ? 'var(--canvas-sunk)' : 'transparent' }}>
            <Icon name="grid" size={14} />
          </button>
          <button onClick={() => setView('list')} className="btn btn-ghost" style={{ padding: 6, background: view === 'list' ? 'var(--canvas-sunk)' : 'transparent' }}>
            <Icon name="stack" size={14} />
          </button>
        </div>
      </div>

      {view === 'grid' ? (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 18 }}>
          {filtered.map(s => {
            const top = s.estimates[0];
            return (
              <div key={s.id} onClick={() => goto('detail', s.id)}
                style={{ cursor: 'pointer' }}>
                <div style={{ position: 'relative', marginBottom: 12 }}>
                  <CardArt card={s.card} />
                  {s.actual && (
                    <div style={{
                      position: 'absolute', top: 10, right: 10,
                      background: 'var(--paper)', padding: '4px 8px',
                      borderRadius: 6, boxShadow: 'var(--shadow-sm)',
                      display: 'flex', alignItems: 'center', gap: 6,
                      fontSize: 11, fontWeight: 500,
                    }}>
                      <span className="mono" style={{ fontSize: 9, color: 'var(--ink-3)' }}>{s.actual.co}</span>
                      <span style={{ fontFamily: 'var(--sans)', fontSize: 14 }}>{s.actual.grade}</span>
                    </div>
                  )}
                </div>
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: 8 }}>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ fontWeight: 500, fontSize: 13.5, letterSpacing: '-0.01em', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{s.card.name}</div>
                    <div className="mono" style={{ fontSize: 11, color: 'var(--ink-4)', marginTop: 2 }}>{s.card.setCode} · {s.card.number}</div>
                  </div>
                  <div style={{ textAlign: 'right' }}>
                    <div className="mono" style={{ fontSize: 9, color: 'var(--ink-3)', letterSpacing: '0.1em' }}>EST {top.co}</div>
                    <div style={{ fontFamily: 'var(--sans)', fontSize: 18, fontWeight: 500, letterSpacing: '-0.03em', lineHeight: 1 }}>{top.grade.toFixed(1)}</div>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      ) : (
        <div className="surface">
          <table className="clean">
            <thead>
              <tr>
                <th>Card</th><th>Set</th><th>Submitted</th>
                <th>Corners</th><th>Edges</th><th>Surface</th>
                <th>Estimate</th><th>Actual</th>
                <th style={{ width: 30 }}></th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(s => {
                const top = s.estimates[0];
                const sc = s.scores || { corners: '—', edges: '—', surface: '—' };
                return (
                  <tr key={s.id} onClick={() => goto('detail', s.id)}>
                    <td>
                      <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
                        <div style={{ width: 30, height: 42 }}><CardArt card={s.card} size="sm" /></div>
                        <div>
                          <div style={{ fontWeight: 500 }}>{s.card.name}</div>
                          <div className="mono" style={{ fontSize: 10.5, color: 'var(--ink-4)' }}>{s.id}</div>
                        </div>
                      </div>
                    </td>
                    <td><span style={{ fontSize: 12 }}>{s.card.set}</span></td>
                    <td><span className="mono" style={{ fontSize: 12, color: 'var(--ink-3)' }}>{s.date}</span></td>
                    <td className="mono">{typeof sc.corners === 'number' ? sc.corners.toFixed(1) : sc.corners}</td>
                    <td className="mono">{typeof sc.edges === 'number' ? sc.edges.toFixed(1) : sc.edges}</td>
                    <td className="mono">{typeof sc.surface === 'number' ? sc.surface.toFixed(1) : sc.surface}</td>
                    <td>
                      <span className="mono" style={{ fontSize: 10, color: 'var(--ink-3)' }}>{top.co}</span>{' '}
                      <span style={{ fontFamily: 'var(--sans)', fontWeight: 500 }}>{top.grade.toFixed(1)}</span>
                    </td>
                    <td>{s.actual
                      ? <span className="chip sage dot">{s.actual.co} {s.actual.grade}</span>
                      : <span style={{ color: 'var(--ink-4)', fontSize: 12 }}>—</span>}</td>
                    <td><Icon name="chevron" size={14} style={{ color: 'var(--ink-4)' }} /></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};

window.Collection = Collection;
