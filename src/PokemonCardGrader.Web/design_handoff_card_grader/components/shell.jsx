/* Shared primitives: CardArt, GradeBadge, Sidebar, Topbar, Icon */

const Icon = ({ name, size = 16, stroke = 1.5, style }) => {
  const s = { width: size, height: size, stroke: 'currentColor', strokeWidth: stroke, strokeLinecap: 'round', strokeLinejoin: 'round', fill: 'none', ...style };
  const paths = {
    home:     <><path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V20h14V9.5"/></>,
    stack:    <><rect x="4" y="4" width="16" height="16" rx="2"/><path d="M8 4v16M4 8h16"/></>,
    plus:     <><path d="M12 5v14M5 12h14"/></>,
    grid:     <><rect x="4" y="4" width="7" height="7" rx="1.5"/><rect x="13" y="4" width="7" height="7" rx="1.5"/><rect x="4" y="13" width="7" height="7" rx="1.5"/><rect x="13" y="13" width="7" height="7" rx="1.5"/></>,
    clock:    <><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></>,
    search:   <><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></>,
    chevron:  <><path d="m9 6 6 6-6 6"/></>,
    chevronL: <><path d="m15 6-6 6 6 6"/></>,
    upload:   <><path d="M12 4v12m0-12-4 4m4-4 4 4"/><path d="M4 17v3h16v-3"/></>,
    check:    <><path d="M5 12l4 4 10-10"/></>,
    camera:   <><path d="M4 7h3l2-2h6l2 2h3a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V8a1 1 0 0 1 1-1z"/><circle cx="12" cy="13" r="4"/></>,
    sparkle:  <><path d="M12 3v6M12 15v6M3 12h6M15 12h6M5.5 5.5l4 4M14.5 14.5l4 4M18.5 5.5l-4 4M9.5 14.5l-4 4"/></>,
    settings: <><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .4 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.9-.4 1.7 1.7 0 0 0-1 1.6V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.9.4l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .4-1.9 1.7 1.7 0 0 0-1.6-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.4-1.9l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.4H9a1.7 1.7 0 0 0 1-1.6V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.4l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.4 1.9V9a1.7 1.7 0 0 0 1.6 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></>,
    dot:      <circle cx="12" cy="12" r="3" fill="currentColor" stroke="none"/>,
    arrow:    <><path d="M5 12h14"/><path d="m13 6 6 6-6 6"/></>,
    trash:    <><path d="M4 7h16M9 7V5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2M6 7v12a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V7"/></>,
    image:    <><rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="9" cy="10" r="1.5" fill="currentColor" stroke="none"/><path d="m4 17 5-5 4 4 3-3 4 4"/></>,
    target:   <><circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1"/></>,
    chart:    <><path d="M4 20V8M10 20v-9M16 20v-6M22 20V4"/></>,
    download: <><path d="M12 4v12m0 0-4-4m4 4 4-4"/><path d="M4 17v3h16v-3"/></>,
    tweaks:   <><path d="M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0"/><circle cx="16" cy="6" r="2"/><circle cx="10" cy="12" r="2"/><circle cx="18" cy="18" r="2"/></>,
  };
  return <svg viewBox="0 0 24 24" style={s}>{paths[name] || null}</svg>;
};

// Placeholder card art — stripy faux-card with hue tint per sample card
const CardArt = ({ card, size = 'md' }) => {
  const h = card?.hue ?? 220;
  const tint = `oklch(0.82 0.08 ${h})`;
  const tint2 = `oklch(0.72 0.12 ${h})`;
  const w = size === 'lg' ? 240 : size === 'sm' ? 60 : 140;
  return (
    <div style={{ width: '100%', aspectRatio: '63 / 88', position: 'relative', borderRadius: 10, overflow: 'hidden', border: '1px solid var(--rule)', background: tint }}>
      <div style={{
        position: 'absolute', inset: 0,
        background: `
          radial-gradient(ellipse at 30% 20%, rgba(255,255,255,0.35), transparent 60%),
          repeating-linear-gradient(135deg, ${tint2} 0 6px, ${tint} 6px 12px)`,
      }} />
      <div style={{
        position: 'absolute', inset: '8%',
        border: '1px solid rgba(255,255,255,0.4)',
        borderRadius: 4,
      }} />
      {/* Title bar */}
      <div style={{
        position: 'absolute', left: '12%', right: '12%', top: '12%',
        padding: '4px 6px',
        background: 'rgba(23,23,26,0.55)',
        borderRadius: 3,
        color: '#fff',
        fontFamily: 'var(--sans)',
        fontSize: 'clamp(7px, 1.6cqw, 11px)',
        fontWeight: 500,
        letterSpacing: '-0.01em',
        containerType: 'inline-size',
        display: 'flex', justifyContent: 'space-between', alignItems: 'center',
      }}>
        <span>{card?.name}</span>
        <span style={{ opacity: 0.7, fontSize: '0.85em' }}>HP 120</span>
      </div>
      {/* Art window */}
      <div style={{
        position: 'absolute', left: '12%', right: '12%', top: '24%', bottom: '36%',
        background: `linear-gradient(180deg, oklch(0.90 0.06 ${h}), oklch(0.65 0.14 ${h}))`,
        border: '1px solid rgba(23,23,26,0.12)',
        borderRadius: 2,
        display: 'grid', placeItems: 'center',
        color: 'rgba(23,23,26,0.35)',
        fontFamily: 'var(--mono)',
        fontSize: 'clamp(6px, 1.4cqw, 10px)',
        letterSpacing: '0.14em',
        containerType: 'inline-size',
      }}>
        {card?.rarity?.toUpperCase()}
      </div>
      {/* stats band */}
      <div style={{
        position: 'absolute', left: '12%', right: '12%', bottom: '10%',
        display: 'flex', flexDirection: 'column', gap: 2,
      }}>
        <div style={{
          height: 3, background: 'rgba(23,23,26,0.25)', borderRadius: 1,
        }} />
        <div style={{
          height: 2, width: '70%', background: 'rgba(23,23,26,0.2)', borderRadius: 1,
        }} />
        <div style={{
          height: 2, width: '55%', background: 'rgba(23,23,26,0.2)', borderRadius: 1,
        }} />
      </div>
      <div style={{
        position: 'absolute', left: '12%', bottom: '4%',
        fontFamily: 'var(--mono)', fontSize: 'clamp(5px, 1cqw, 8px)',
        color: 'rgba(23,23,26,0.6)', letterSpacing: '0.08em',
      }}>
        {card?.setCode} · {card?.number}
      </div>
    </div>
  );
};

const GradeBadge = ({ co, grade, lead, size = 'md' }) => (
  <div className={`grade ${lead ? 'lead' : ''}`} style={{
    minWidth: size === 'lg' ? 96 : 72,
    padding: size === 'lg' ? '14px 18px' : '10px 14px',
  }}>
    <span className="co">{co}</span>
    <span className="num" style={{ fontSize: size === 'lg' ? 40 : 28 }}>
      {Number.isInteger(grade) ? grade.toFixed(1) : grade.toFixed(1)}
    </span>
  </div>
);

const Sidebar = ({ current, setCurrent, counts }) => {
  const items = [
    { key: 'dashboard',  label: 'Overview',    icon: 'home' },
    { key: 'collection', label: 'Collection',  icon: 'grid',  count: counts.collection },
    { key: 'submissions',label: 'Submissions', icon: 'stack', count: counts.submissions },
    { key: 'new',        label: 'New submission', icon: 'plus' },
  ];
  const secondary = [
    { key: 'search', label: 'Card catalog', icon: 'search' },
    { key: 'graded', label: 'Graded archive', icon: 'clock', count: counts.graded },
  ];
  return (
    <aside className="sidebar">
      <div className="brand">
        <div className="brand-mark">⌘</div>
        <div className="brand-name">
          Calibra
          <small>Card Grading Studio</small>
        </div>
      </div>

      <div className="nav-label">Workspace</div>
      {items.map(it => (
        <button key={it.key}
          className={`nav-item ${current === it.key ? 'active' : ''}`}
          onClick={() => setCurrent(it.key)}>
          <Icon name={it.icon} size={15} />
          <span>{it.label}</span>
          {it.count != null && <span className="count">{it.count}</span>}
        </button>
      ))}

      <div className="nav-label">Library</div>
      {secondary.map(it => (
        <button key={it.key}
          className={`nav-item ${current === it.key ? 'active' : ''}`}
          onClick={() => setCurrent(it.key)}>
          <Icon name={it.icon} size={15} />
          <span>{it.label}</span>
          {it.count != null && <span className="count">{it.count}</span>}
        </button>
      ))}

      <div className="sidebar-footer">
        <div className="avatar">M</div>
        <div className="who">
          Marco Vey
          <small>Pro plan · 52 graded</small>
        </div>
      </div>
    </aside>
  );
};

const Topbar = ({ crumbs, right }) => (
  <div className="topbar">
    <div className="crumbs">
      {crumbs.map((c, i) => (
        <React.Fragment key={i}>
          {i > 0 && <span style={{ padding: '0 6px', color: 'var(--ink-4)' }}>/</span>}
          {i === crumbs.length - 1 ? <strong>{c}</strong> : <span>{c}</span>}
        </React.Fragment>
      ))}
    </div>
    <div className="grow" />
    <div className="search">
      <Icon name="search" size={13} style={{ color: 'var(--ink-4)' }} />
      <input placeholder="Search cards, sets, submissions…" />
      <span className="kbd">⌘K</span>
    </div>
    {right}
  </div>
);

Object.assign(window, { Icon, CardArt, GradeBadge, Sidebar, Topbar });
