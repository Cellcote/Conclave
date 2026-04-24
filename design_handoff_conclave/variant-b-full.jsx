// Variant B — Linear/Raycast clean, full-bleed version with multiple states.
// Exposes: theme, accent, font, density, radius, sidebarWidth, rightPanel, toolStyle, scenario

const ACCENTS_B = {
  orange: 32,
  cream:  72,
  cool:   240,
  green:  158,
  magenta: 320,
};

const FONTS_B = {
  inter:    'Inter, -apple-system, BlinkMacSystemFont, "SF Pro Text", sans-serif',
  geist:    '"Geist", "Inter", -apple-system, BlinkMacSystemFont, sans-serif',
  ibmPlex:  '"IBM Plex Sans", -apple-system, sans-serif',
  sfpro:    '-apple-system, BlinkMacSystemFont, "SF Pro Text", "SF Pro", sans-serif',
};

function makeTokens(theme, accent, density, radius) {
  const dark = theme === 'dark';
  const hue = ACCENTS_B[accent] ?? 240;
  // density: 0 = cozy, 1 = normal, 2 = dense
  const d = density;
  const base = dark ? {
    bg:       '#0D0E10',
    panel:    '#131418',
    panel2:   '#181A1F',
    panelHi:  '#1E2026',
    border:   '#222429',
    borderHi: '#2E3139',
    text:     '#E8E9EC',
    textDim:  '#8A8D96',
    textMute: '#4E5058',
    accent:   `oklch(0.72 0.13 ${hue})`,
    accentFg: dark ? '#0D0E10' : '#FDFDFC',
    accentDim:`oklch(0.72 0.13 ${hue} / 0.14)`,
    accentMid:`oklch(0.72 0.13 ${hue} / 0.28)`,
    ok:       'oklch(0.74 0.14 158)',
    warn:     'oklch(0.80 0.15 80)',
    err:      'oklch(0.68 0.18 25)',
    info:     'oklch(0.72 0.12 240)',
  } : {
    bg:       '#FDFDFC',
    panel:    '#F7F7F6',
    panel2:   '#F1F1EF',
    panelHi:  '#ECECEA',
    border:   '#E6E6E3',
    borderHi: '#D4D4D0',
    text:     '#0F1012',
    textDim:  '#666870',
    textMute: '#9A9CA3',
    accent:   `oklch(0.55 0.14 ${hue})`,
    accentFg: '#FFFFFF',
    accentDim:`oklch(0.55 0.14 ${hue} / 0.10)`,
    accentMid:`oklch(0.55 0.14 ${hue} / 0.22)`,
    ok:       'oklch(0.50 0.14 158)',
    warn:     'oklch(0.58 0.15 65)',
    err:      'oklch(0.54 0.18 25)',
    info:     'oklch(0.55 0.14 240)',
  };
  // Spacing scale driven by density
  const pad = [{ row: 9, x: 12, main: 32, top: 16 },
               { row: 7, x: 10, main: 24, top: 12 },
               { row: 5, x: 9,  main: 18, top: 9  }][d];
  const radii = {
    0: { xs: 2, sm: 3, md: 4, lg: 6 },    // sharp
    1: { xs: 3, sm: 5, md: 7, lg: 9 },    // medium
    2: { xs: 5, sm: 8, md: 11, lg: 14 },  // soft
  }[radius];
  return { ...base, pad, radii };
}

// ─────────────────────────────────────────────────────────────────────

function VariantBFull({
  theme = 'dark', accent = 'cool', font = 'inter',
  density = 1, radius = 1,
  sidebarWidth = 264, rightPanel = true,
  toolStyle = 'pill',       // 'pill' | 'card'
  scenario = 'terminal',    // 'terminal' | 'plan' | 'logs' | 'empty' | 'error' | 'permission' | 'new-session'
}) {
  const tokens = makeTokens(theme, accent, density, radius);
  const fontFamily = FONTS_B[font] || FONTS_B.inter;
  const data = window.CONCLAVE_DATA;

  // Which session is "active" depends on the scenario
  let activeId = window.ACTIVE_SESSION_ID;
  if (scenario === 'error') activeId = 's5';
  const all = data.projects.flatMap(p => p.sessions.map(s => ({ ...s, project: p })));
  const active = all.find(s => s.id === activeId) || all[0];

  const rightCols = rightPanel && !['empty'].includes(scenario) ? '320px' : '0px';

  return (
    <div style={{
      width: '100%', height: '100%',
      background: tokens.bg,
      color: tokens.text,
      fontFamily,
      fontSize: 13,
      display: 'grid',
      gridTemplateColumns: `${sidebarWidth}px 1fr ${rightCols}`,
      gridTemplateRows: '38px 1fr',
      position: 'relative',
      overflow: 'hidden',
    }}>
      <Titlebar tokens={tokens} active={active} scenario={scenario} />
      <Sidebar tokens={tokens} active={active} scenario={scenario} />
      <Main tokens={tokens} active={active} scenario={scenario} toolStyle={toolStyle} fontFamily={fontFamily} />
      {rightPanel && scenario !== 'empty' && (
        <RightPanel tokens={tokens} active={active} scenario={scenario} />
      )}

      {scenario === 'permission' && <PermissionModal tokens={tokens} />}
      {scenario === 'new-session' && <NewSessionModal tokens={tokens} />}
    </div>
  );
}

// ─── Titlebar ────────────────────────────────────────────────────────

function Titlebar({ tokens, active, scenario }) {
  return (
    <div style={{
      gridColumn: '1 / -1',
      borderBottom: `1px solid ${tokens.border}`,
      display: 'flex', alignItems: 'center',
      paddingLeft: 80, paddingRight: 10,
      gap: 8,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 12.5, color: tokens.text, fontWeight: 600, letterSpacing: '-0.01em' }}>
        <span style={{
          width: 16, height: 16, borderRadius: tokens.radii.sm,
          background: tokens.text, color: tokens.bg,
          fontSize: 9, fontWeight: 800,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
        }}>C</span>
        Conclave
      </div>
      <span style={{ color: tokens.textMute }}>›</span>
      <span style={{ fontSize: 12.5, color: tokens.textDim }}>{active.project.name}</span>
      {scenario !== 'empty' && scenario !== 'new-session' && (
        <>
          <span style={{ color: tokens.textMute }}>›</span>
          <span style={{
            fontSize: 12.5, color: tokens.text, fontWeight: 500,
            maxWidth: 420, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
          }}>{active.title}</span>
        </>
      )}
      <div style={{ flex: 1 }} />
      <BTag tokens={tokens}>⌘K</BTag>
      <BTag tokens={tokens} primary>New session</BTag>
    </div>
  );
}

// ─── Sidebar ─────────────────────────────────────────────────────────

function Sidebar({ tokens, active, scenario }) {
  const data = window.CONCLAVE_DATA;
  const counts = {
    all: data.projects.reduce((n, p) => n + p.sessions.length, 0),
    running: data.projects.reduce((n, p) => n + p.sessions.filter(s => s.status === 'working' || s.status === 'running-tool').length, 0),
    attention: data.projects.reduce((n, p) => n + p.sessions.filter(s => s.status === 'waiting' || s.status === 'error').length, 0),
    idle: data.projects.reduce((n, p) => n + p.sessions.filter(s => s.status === 'idle' || s.status === 'completed').length, 0),
  };

  return (
    <div style={{
      borderRight: `1px solid ${tokens.border}`,
      overflow: 'auto',
      padding: `${tokens.pad.top - 4}px 8px`,
    }}>
      <div style={{
        margin: '2px 4px 10px',
        display: 'flex', alignItems: 'center', gap: 8,
        padding: `${tokens.pad.row - 1}px 10px`,
        border: `1px solid ${tokens.border}`,
        borderRadius: tokens.radii.md,
        fontSize: 12.5,
        color: tokens.textMute,
      }}>
        <span>⌕</span>
        <span style={{ flex: 1 }}>Search</span>
        <span style={{ fontSize: 10.5 }}>⌘P</span>
      </div>

      <BSideSection tokens={tokens} label="All sessions" count={counts.all} active />
      <BSideSection tokens={tokens} label="Running" count={counts.running} dot={tokens.accent} pulse />
      <BSideSection tokens={tokens} label="Needs attention" count={counts.attention} dot={tokens.warn} />
      <BSideSection tokens={tokens} label="Idle" count={counts.idle} dot={tokens.textMute} />

      <div style={{ height: 14 }} />

      {data.projects.map(p => (
        <BProject key={p.id} project={p} tokens={tokens} active={active} />
      ))}
    </div>
  );
}

function BSideSection({ tokens, label, count, dot, pulse, active }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 9,
      padding: `${tokens.pad.row - 1}px 10px`,
      borderRadius: tokens.radii.sm,
      background: active ? tokens.panel : 'transparent',
      fontSize: 12.5,
      color: active ? tokens.text : tokens.textDim,
      fontWeight: active ? 500 : 400,
      cursor: 'pointer',
    }}>
      {dot ? (
        <span style={{
          width: 6, height: 6, borderRadius: 999, background: dot,
          boxShadow: pulse ? `0 0 0 3px ${dot}22` : 'none',
          margin: '0 1px',
        }} />
      ) : (
        <span style={{ width: 8, fontSize: 10, opacity: 0.6 }}>◱</span>
      )}
      <span style={{ flex: 1 }}>{label}</span>
      <span style={{ fontSize: 11, color: tokens.textMute, fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' }}>{count}</span>
    </div>
  );
}

function BProject({ project, tokens, active }) {
  return (
    <div style={{ marginTop: 6 }}>
      <div style={{
        display: 'flex', alignItems: 'center', gap: 8,
        padding: `${tokens.pad.row - 1}px 10px`,
        fontSize: 11,
        color: tokens.textDim,
      }}>
        <span style={{ fontSize: 9 }}>▾</span>
        <span style={{ color: tokens.text, fontSize: 12, fontWeight: 600 }}>{project.name}</span>
        <span style={{ flex: 1 }} />
        <span style={{ fontSize: 10.5, color: tokens.textMute }}>{project.sessions.length}</span>
      </div>
      {project.sessions.map(s => (
        <BSessionRow key={s.id} s={s} tokens={tokens} active={s.id === active.id} />
      ))}
    </div>
  );
}

function BSessionRow({ s, tokens, active }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'flex-start', gap: 10,
      padding: `${tokens.pad.row}px 10px ${tokens.pad.row}px 22px`,
      borderRadius: tokens.radii.sm,
      background: active ? tokens.panel : 'transparent',
      margin: '1px 0',
      cursor: 'pointer',
    }}>
      <span style={{ marginTop: 5 }}><BStatusDot status={s.status} tokens={tokens} /></span>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{
          fontSize: 12.5,
          fontWeight: active ? 500 : 400,
          color: active ? tokens.text : tokens.textDim,
          overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
          letterSpacing: '-0.005em',
        }}>{s.title}</div>
        <div style={{
          fontSize: 10.5, color: tokens.textMute, marginTop: 2,
          fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
          overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
        }}>
          {s.branch}
          {s.diff.files > 0 && <> · <span style={{ color: tokens.ok }}>+{s.diff.add}</span> <span style={{ color: tokens.err }}>−{s.diff.del}</span></>}
          {s.pr && <> · #{s.pr.number}</>}
        </div>
      </div>
      {s.unread > 0 && (
        <span style={{
          marginTop: 3,
          minWidth: 14, height: 14, padding: '0 4px',
          borderRadius: 999,
          background: tokens.accentMid,
          color: tokens.accent,
          fontSize: 10, fontWeight: 700,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
        }}>{s.unread}</span>
      )}
    </div>
  );
}

function BStatusDot({ status, tokens }) {
  const c = {
    'working': tokens.accent,
    'waiting': tokens.warn,
    'running-tool': tokens.ok,
    'idle': tokens.textMute,
    'error': tokens.err,
    'queued': tokens.textMute,
    'completed': tokens.ok,
  }[status] || tokens.textMute;
  const pulse = status === 'working' || status === 'running-tool';
  return (
    <span style={{
      width: 6, height: 6, borderRadius: 999,
      background: c,
      boxShadow: pulse ? `0 0 0 3px ${c}22` : 'none',
      flexShrink: 0, display: 'inline-block',
    }} />
  );
}

function BStatusLabel({ status, tokens }) {
  const labels = {
    working: 'Working', waiting: 'Waiting for you', idle: 'Idle',
    'running-tool': 'Running tool', error: 'Error', queued: 'Queued', completed: 'Completed',
  };
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12, color: tokens.textDim }}>
      <BStatusDot status={status} tokens={tokens} />
      {labels[status]}
    </span>
  );
}

// ─── Main pane ───────────────────────────────────────────────────────

function Main({ tokens, active, scenario, toolStyle, fontFamily }) {
  if (scenario === 'empty') return <EmptyState tokens={tokens} />;

  const view = scenario === 'plan' ? 'Plan'
    : scenario === 'logs' ? 'Logs'
    : 'Terminal';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
      <div style={{
        padding: `${tokens.pad.top - 2}px ${tokens.pad.main}px`,
        borderBottom: `1px solid ${tokens.border}`,
        display: 'flex', alignItems: 'center', gap: 14,
      }}>
        <div style={{ minWidth: 0, flex: 1 }}>
          <div style={{
            fontSize: 15, fontWeight: 600, color: tokens.text,
            letterSpacing: '-0.015em',
            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
          }}>{active.title}</div>
          <div style={{ marginTop: 3, display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, color: tokens.textDim }}>
            <BStatusLabel status={active.status} tokens={tokens} />
            <span>·</span>
            <span style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', fontSize: 11.5 }}>{active.branch}</span>
            <span>·</span>
            <span>{active.model}</span>
            {active.pr && (
              <>
                <span>·</span>
                <span style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', fontSize: 11.5 }}>#{active.pr.number}</span>
              </>
            )}
          </div>
        </div>
        <BSeg tokens={tokens} items={['Terminal', 'Plan', 'Logs']} active={view} />
      </div>

      {/* View body */}
      {scenario === 'plan' ? (
        <PlanView tokens={tokens} />
      ) : scenario === 'logs' ? (
        <LogsView tokens={tokens} />
      ) : scenario === 'error' ? (
        <ErrorTranscript tokens={tokens} active={active} toolStyle={toolStyle} />
      ) : (
        <Transcript tokens={tokens} toolStyle={toolStyle} />
      )}

      <Composer tokens={tokens} scenario={scenario} />
    </div>
  );
}

// ─── Segmented ───────────────────────────────────────────────────────

function BSeg({ tokens, items, active }) {
  return (
    <div style={{
      display: 'inline-flex',
      border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.md,
      padding: 2, background: tokens.panel,
    }}>
      {items.map(i => (
        <span key={i} style={{
          padding: '4px 11px', fontSize: 12, fontWeight: 500,
          borderRadius: tokens.radii.sm,
          background: i === active ? tokens.bg : 'transparent',
          color: i === active ? tokens.text : tokens.textDim,
          border: i === active ? `1px solid ${tokens.border}` : '1px solid transparent',
          cursor: 'pointer',
        }}>{i}</span>
      ))}
    </div>
  );
}

function BTag({ children, tokens, primary, subtle, color }) {
  return (
    <span style={{
      padding: '3px 9px', fontSize: 11.5, fontWeight: 500,
      border: `1px solid ${primary ? tokens.text : tokens.border}`,
      background: primary ? tokens.text : subtle ? tokens.panel : 'transparent',
      color: primary ? tokens.bg : color || tokens.textDim,
      borderRadius: tokens.radii.sm,
      whiteSpace: 'nowrap', cursor: 'pointer',
    }}>{children}</span>
  );
}

// ─── Transcript ──────────────────────────────────────────────────────

function Transcript({ tokens, toolStyle }) {
  return (
    <div style={{
      flex: 1, minHeight: 0, overflow: 'auto',
      padding: `${tokens.pad.top + 6}px ${tokens.pad.main + 6}px 14px`,
      fontSize: 13.5, lineHeight: 1.6,
    }}>
      {window.TRANSCRIPT.map((m, i) => <BMsg key={i} msg={m} tokens={tokens} toolStyle={toolStyle} />)}
    </div>
  );
}

function ErrorTranscript({ tokens, active, toolStyle }) {
  const err = {
    role: 'assistant', time: '14:07',
    content: "Build failed. The ConPTY resize path on Windows 11 24H2 needs the HPCON extension API, which isn't exposed on net10.0-windows without TargetPlatformVersion 10.0.22621.",
    tools: [
      { kind: 'bash', target: 'dotnet build Porta.Pty -c Release', ok: false, meta: 'exit 1 · 3.2s' },
    ],
    error: {
      code: 'CS0117',
      file: 'src/Porta.Pty/Windows/ConPty.cs:142',
      message: "'PseudoConsole' does not contain a definition for 'ResizePseudoConsole'",
    },
  };
  return (
    <div style={{
      flex: 1, minHeight: 0, overflow: 'auto',
      padding: `${tokens.pad.top + 6}px ${tokens.pad.main + 6}px 14px`,
      fontSize: 13.5, lineHeight: 1.6,
    }}>
      <BMsg msg={{
        role: 'user', time: '14:05',
        content: "Make the terminal resize work on Windows 11. It's currently throwing on any window size change.",
      }} tokens={tokens} />
      <BMsg msg={err} tokens={tokens} toolStyle={toolStyle} />
      <ErrorCard tokens={tokens} err={err.error} />
    </div>
  );
}

function ErrorCard({ tokens, err }) {
  return (
    <div style={{
      border: `1px solid ${tokens.err}`,
      background: `color-mix(in oklch, ${tokens.err} 8%, transparent)`,
      borderRadius: tokens.radii.md,
      padding: '12px 14px',
      marginTop: 8,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
        <span style={{ color: tokens.err, fontWeight: 600, fontSize: 12 }}>✕ Build error</span>
        <span style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', fontSize: 11, color: tokens.textDim }}>{err.code}</span>
      </div>
      <div style={{ fontSize: 13, color: tokens.text, lineHeight: 1.5 }}>{err.message}</div>
      <div style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', fontSize: 11.5, color: tokens.textDim, marginTop: 8 }}>{err.file}</div>
      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <button style={{
          padding: '5px 11px', fontSize: 12, fontWeight: 500,
          background: tokens.err, color: '#FFF',
          border: 'none', borderRadius: tokens.radii.sm, cursor: 'pointer', fontFamily: 'inherit',
        }}>Ask Claude to fix</button>
        <button style={{
          padding: '5px 11px', fontSize: 12, fontWeight: 500,
          background: 'transparent', color: tokens.text,
          border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.sm, cursor: 'pointer', fontFamily: 'inherit',
        }}>Open file</button>
        <button style={{
          padding: '5px 11px', fontSize: 12, fontWeight: 500,
          background: 'transparent', color: tokens.textDim,
          border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.sm, cursor: 'pointer', fontFamily: 'inherit',
        }}>Copy error</button>
      </div>
    </div>
  );
}

function BMsg({ msg, tokens, toolStyle = 'pill' }) {
  if (msg.role === 'user') {
    return (
      <div style={{ margin: '0 0 24px' }}>
        <div style={{ fontSize: 11, color: tokens.textDim, marginBottom: 6, fontWeight: 500 }}>
          You · {msg.time}
        </div>
        <div style={{ color: tokens.text, fontSize: 13.5, lineHeight: 1.55 }}>{msg.content}</div>
      </div>
    );
  }
  return (
    <div style={{ margin: '0 0 24px' }}>
      <div style={{ fontSize: 11, color: tokens.textDim, marginBottom: 6, fontWeight: 500, display: 'flex', gap: 8, alignItems: 'center' }}>
        <span style={{ width: 6, height: 6, borderRadius: 999, background: tokens.accent }} />
        Claude · {msg.time}
      </div>
      <div style={{ color: tokens.text, fontSize: 13.5, lineHeight: 1.55 }}>{msg.content}</div>
      {msg.tools && msg.tools.length > 0 && (
        <div style={{ marginTop: 10, display: 'flex', flexDirection: 'column', gap: toolStyle === 'card' ? 6 : 3 }}>
          {msg.tools.map((t, i) => toolStyle === 'card'
            ? <ToolCard key={i} t={t} tokens={tokens} />
            : <ToolPill key={i} t={t} tokens={tokens} />
          )}
        </div>
      )}
    </div>
  );
}

function ToolPill({ t, tokens }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 10,
      padding: '6px 10px',
      border: `1px solid ${tokens.border}`,
      borderRadius: tokens.radii.sm,
      fontSize: 11.5,
      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
    }}>
      <span style={{
        fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.08em',
        color: tokens.textDim, minWidth: 36,
      }}>{t.kind}</span>
      <span style={{ color: tokens.text, flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{t.target}</span>
      <span style={{ color: t.ok === false ? tokens.err : t.ok === null ? tokens.warn : tokens.ok, fontSize: 11 }}>
        {t.ok === false ? '✕' : t.ok === null ? '…' : '✓'} {t.meta}
      </span>
    </div>
  );
}

function ToolCard({ t, tokens }) {
  const fgOk = t.ok === false ? tokens.err : t.ok === null ? tokens.warn : tokens.ok;
  return (
    <div style={{
      border: `1px solid ${tokens.border}`,
      borderRadius: tokens.radii.md,
      background: tokens.panel,
      overflow: 'hidden',
    }}>
      <div style={{
        display: 'flex', alignItems: 'center', gap: 10,
        padding: '7px 12px',
        borderBottom: `1px solid ${tokens.border}`,
        background: tokens.panel2,
      }}>
        <span style={{ color: fgOk, fontSize: 11 }}>●</span>
        <span style={{
          fontSize: 10.5, textTransform: 'uppercase', letterSpacing: '0.08em',
          color: tokens.textDim, fontWeight: 600,
        }}>{t.kind}</span>
        <span style={{
          color: tokens.text, flex: 1, fontSize: 12,
          fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
          overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
        }}>{t.target}</span>
        <span style={{ color: tokens.textMute, fontSize: 11, fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' }}>{t.meta}</span>
      </div>
      {t.kind === 'bash' && (
        <div style={{
          padding: '8px 12px', fontSize: 11.5,
          fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
          color: tokens.textDim, lineHeight: 1.5,
        }}>
          <div><span style={{ color: tokens.accent }}>$</span> {t.target.split(' ').slice(0, 4).join(' ')}…</div>
          <div style={{ color: tokens.textMute, marginTop: 2 }}>  info: Determining projects to restore…</div>
          <div style={{ color: tokens.textMute }}>  info: Restored /src/Conclave.App in 912ms.</div>
        </div>
      )}
      {(t.kind === 'edit' || t.kind === 'write') && (
        <div style={{
          padding: '8px 12px', fontSize: 11.5,
          fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
          lineHeight: 1.6,
        }}>
          <div style={{ color: tokens.ok }}>+ using Microsoft.Data.Sqlite;</div>
          <div style={{ color: tokens.ok }}>+ public sealed class Connection : IDbConnectionFactory {'{'}</div>
          <div style={{ color: tokens.textMute }}>  …  </div>
        </div>
      )}
    </div>
  );
}

// ─── Plan view ───────────────────────────────────────────────────────

function PlanView({ tokens }) {
  const plan = [
    { s: 'done', t: 'Read NOTES.md and inspect existing csproj' },
    { s: 'done', t: 'Add Microsoft.Data.Sqlite package reference' },
    { s: 'done', t: 'Create Db/Connection.cs with WAL-mode factory' },
    { s: 'done', t: 'Scaffold Db/Migrator.cs that runs embedded .sql files in order' },
    { s: 'done', t: 'Write 001_init.sql (projects + sessions tables)' },
    { s: 'doing', t: 'Wire the factory + migrator into Program.cs startup' },
    { s: 'todo', t: 'Register IDbConnectionFactory in DI container' },
    { s: 'todo', t: 'Add SessionRepository with CRUD + ListByProject' },
    { s: 'todo', t: 'Write migrator integration tests (creates schema, idempotent on rerun)' },
    { s: 'todo', t: 'Add first-launch path: migrate then open main window' },
  ];
  return (
    <div style={{
      flex: 1, minHeight: 0, overflow: 'auto',
      padding: `${tokens.pad.top + 8}px ${tokens.pad.main + 6}px 14px`,
    }}>
      <div style={{ fontSize: 11, color: tokens.textDim, textTransform: 'uppercase', letterSpacing: '0.08em', fontWeight: 600, marginBottom: 12 }}>
        Current plan · {plan.filter(p => p.s === 'done').length} of {plan.length} complete
      </div>
      <div style={{
        height: 4, background: tokens.panel, borderRadius: 999, overflow: 'hidden',
        marginBottom: 24,
      }}>
        <div style={{
          width: `${(plan.filter(p => p.s === 'done').length / plan.length) * 100}%`,
          height: '100%', background: tokens.accent,
        }} />
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        {plan.map((p, i) => (
          <div key={i} style={{
            display: 'flex', alignItems: 'center', gap: 12,
            padding: '10px 12px',
            borderRadius: tokens.radii.sm,
            background: p.s === 'doing' ? tokens.panel : 'transparent',
            border: p.s === 'doing' ? `1px solid ${tokens.borderHi}` : '1px solid transparent',
          }}>
            <PlanMarker tokens={tokens} s={p.s} />
            <span style={{
              flex: 1, fontSize: 13.5,
              color: p.s === 'done' ? tokens.textDim : tokens.text,
              textDecoration: p.s === 'done' ? 'line-through' : 'none',
              textDecorationColor: tokens.textMute,
            }}>{p.t}</span>
            {p.s === 'doing' && (
              <span style={{ fontSize: 11, color: tokens.accent, fontWeight: 500, display: 'flex', gap: 6, alignItems: 'center' }}>
                <span style={{ width: 6, height: 6, borderRadius: 999, background: tokens.accent, boxShadow: `0 0 0 3px ${tokens.accent}22` }} /> in progress
              </span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function PlanMarker({ tokens, s }) {
  if (s === 'done') return (
    <span style={{
      width: 16, height: 16, borderRadius: 999, background: tokens.ok,
      color: tokens.bg, fontSize: 10, fontWeight: 800,
      display: 'flex', alignItems: 'center', justifyContent: 'center',
    }}>✓</span>
  );
  if (s === 'doing') return (
    <span style={{
      width: 16, height: 16, borderRadius: 999,
      border: `2px solid ${tokens.accent}`,
      position: 'relative',
    }}>
      <span style={{
        position: 'absolute', inset: 3, borderRadius: 999, background: tokens.accent,
      }} />
    </span>
  );
  return (
    <span style={{ width: 16, height: 16, borderRadius: 999, border: `1.5px solid ${tokens.borderHi}` }} />
  );
}

// ─── Logs view ───────────────────────────────────────────────────────

function LogsView({ tokens }) {
  const lines = [
    { t: '14:02:08.142', l: 'INF', m: 'claude CLI 2.6.1 · sonnet-4.5 · conclave/wt/sqlite-persistence' },
    { t: '14:02:08.188', l: 'INF', m: 'PTY opened · 132×40 · TERM=xterm-256color' },
    { t: '14:02:09.002', l: 'DBG', m: 'tool_call read NOTES.md' },
    { t: '14:02:09.034', l: 'DBG', m: 'tool_result ok 142 lines · 2.1kb' },
    { t: '14:02:10.411', l: 'DBG', m: 'tool_call read src/Conclave.App/Conclave.App.csproj' },
    { t: '14:02:10.452', l: 'DBG', m: 'tool_result ok 38 lines · 0.9kb' },
    { t: '14:03:02.118', l: 'INF', m: 'tool_call bash dotnet add … Microsoft.Data.Sqlite' },
    { t: '14:03:03.551', l: 'INF', m: 'exit 0 · duration 1.43s' },
    { t: '14:03:04.001', l: 'DBG', m: 'tool_call write src/Conclave.App/Db/Connection.cs' },
    { t: '14:03:04.012', l: 'DBG', m: 'tool_result ok +68 lines' },
    { t: '14:03:04.211', l: 'DBG', m: 'tool_call write src/Conclave.App/Db/Migrator.cs' },
    { t: '14:03:04.225', l: 'DBG', m: 'tool_result ok +94 lines' },
    { t: '14:03:04.488', l: 'DBG', m: 'tool_call write src/Conclave.App/Db/Migrations/001_init.sql' },
    { t: '14:03:04.497', l: 'DBG', m: 'tool_result ok +41 lines' },
    { t: '14:04:12.902', l: 'DBG', m: 'tool_call edit src/Conclave.App/Program.cs' },
    { t: '14:04:12.918', l: 'DBG', m: 'tool_result ok +12 −2' },
    { t: '14:04:13.112', l: 'WRN', m: 'token usage 14,204 in · 3,812 out · ctx 38%' },
    { t: '14:04:14.003', l: 'INF', m: 'message complete · awaiting next prompt' },
  ];
  const lvlColor = (l) => l === 'WRN' ? tokens.warn : l === 'ERR' ? tokens.err : l === 'DBG' ? tokens.textDim : tokens.info;
  return (
    <div style={{
      flex: 1, minHeight: 0, overflow: 'auto',
      padding: `${tokens.pad.top}px ${tokens.pad.main - 8}px 12px`,
      fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
      fontSize: 11.5, lineHeight: 1.75,
      background: tokens.bg,
    }}>
      {lines.map((ln, i) => (
        <div key={i} style={{ display: 'flex', gap: 12, padding: '0 14px' }}>
          <span style={{ color: tokens.textMute, width: 96 }}>{ln.t}</span>
          <span style={{ color: lvlColor(ln.l), width: 30, fontWeight: 600 }}>{ln.l}</span>
          <span style={{ color: tokens.text, flex: 1 }}>{ln.m}</span>
        </div>
      ))}
    </div>
  );
}

// ─── Composer ────────────────────────────────────────────────────────

function Composer({ tokens, scenario }) {
  if (scenario === 'new-session') return null;
  return (
    <div style={{ padding: `${tokens.pad.top - 4}px ${tokens.pad.main}px ${tokens.pad.top}px`, borderTop: `1px solid ${tokens.border}` }}>
      <div style={{
        border: `1px solid ${tokens.borderHi}`,
        borderRadius: tokens.radii.md,
        background: tokens.panel,
        padding: '10px 12px',
      }}>
        <div style={{ fontSize: 13.5, color: tokens.textMute, minHeight: 22 }}>
          Continue the session…
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 8 }}>
          <BTag tokens={tokens}>@ file</BTag>
          <BTag tokens={tokens}>/plan</BTag>
          <BTag tokens={tokens}>Sonnet 4.5</BTag>
          <div style={{ flex: 1 }} />
          <span style={{ fontSize: 11, color: tokens.textMute }}>⏎</span>
          <button style={{
            padding: '5px 12px', fontSize: 12, fontWeight: 600,
            background: tokens.text, color: tokens.bg,
            border: 'none', borderRadius: tokens.radii.sm, cursor: 'pointer',
            fontFamily: 'inherit',
          }}>Send</button>
        </div>
      </div>
    </div>
  );
}

// ─── Right panel ─────────────────────────────────────────────────────

function RightPanel({ tokens, active, scenario }) {
  return (
    <div style={{
      borderLeft: `1px solid ${tokens.border}`,
      overflow: 'auto',
      padding: `${tokens.pad.top + 2}px 18px 24px`,
    }}>
      <RSectionHdr tokens={tokens}>Session</RSectionHdr>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginBottom: 18 }}>
        <BProp tokens={tokens} k="Status"><BStatusLabel status={active.status} tokens={tokens} /></BProp>
        <BProp tokens={tokens} k="Branch" mono>{active.branch}</BProp>
        <BProp tokens={tokens} k="Worktree" mono>{active.worktree}</BProp>
        <BProp tokens={tokens} k="Base" mono>{active.project.defaultBranch}</BProp>
        <BProp tokens={tokens} k="Model">{active.model}</BProp>
        <BProp tokens={tokens} k="Started" mono>14:02:08</BProp>
        <BProp tokens={tokens} k="PID" mono>51882</BProp>
      </div>

      {active.pr && (
        <>
          <RSectionHdr tokens={tokens}>Pull request</RSectionHdr>
          <div style={{
            border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.md, padding: 12, marginBottom: 18,
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
              <span style={{
                fontSize: 10.5, fontWeight: 600,
                padding: '2px 7px', borderRadius: tokens.radii.xs,
                background: tokens.panel2, color: tokens.textDim,
                textTransform: 'uppercase', letterSpacing: '0.06em',
              }}>{active.pr.state}</span>
              <span style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', fontSize: 12, color: tokens.text }}>
                #{active.pr.number}
              </span>
            </div>
            <div style={{ fontSize: 12.5, color: tokens.text }}>
              {active.branch} → {active.project.defaultBranch}
            </div>
            <div style={{ fontSize: 11.5, color: tokens.textDim, marginTop: 4 }}>
              3 commits · ready to push
            </div>
          </div>
        </>
      )}

      <RSectionHdr tokens={tokens}>Diff · {active.diff.files} files</RSectionHdr>
      <div style={{ display: 'flex', gap: 16, marginBottom: 10, fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', fontSize: 12 }}>
        <span style={{ color: tokens.ok }}>+{active.diff.add}</span>
        <span style={{ color: tokens.err }}>−{active.diff.del}</span>
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 2, fontSize: 11.5, fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' }}>
        {[
          ['M', 'src/…/Program.cs', '+12 −2'],
          ['A', 'src/…/Db/Connection.cs', '+68'],
          ['A', 'src/…/Db/Migrator.cs', '+94'],
          ['A', 'src/…/Db/001_init.sql', '+41'],
          ['M', 'src/…/Db/Migrations.cs', '+18'],
        ].map(([t, p, d], i) => (
          <div key={i} style={{ display: 'flex', gap: 8, padding: '4px 0', alignItems: 'center' }}>
            <span style={{ width: 14, textAlign: 'center', color: t === 'A' ? tokens.ok : tokens.warn, fontWeight: 600 }}>{t}</span>
            <span style={{ flex: 1, color: tokens.text, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{p}</span>
            <span style={{ color: tokens.textMute }}>{d}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function RSectionHdr({ children, tokens }) {
  return (
    <div style={{
      fontSize: 11, fontWeight: 600, color: tokens.textDim,
      textTransform: 'uppercase', letterSpacing: '0.08em',
      marginBottom: 10,
    }}>{children}</div>
  );
}

function BProp({ tokens, k, children, mono }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, fontSize: 12.5, alignItems: 'center' }}>
      <span style={{ color: tokens.textDim }}>{k}</span>
      <span style={{
        color: tokens.text,
        fontFamily: mono ? 'ui-monospace, SFMono-Regular, Menlo, monospace' : 'inherit',
        fontSize: mono ? 12 : 12.5, textAlign: 'right',
        overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
      }}>{children}</span>
    </div>
  );
}

// ─── Empty state ─────────────────────────────────────────────────────

function EmptyState({ tokens }) {
  return (
    <div style={{
      display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
      minHeight: 0, height: '100%', padding: 48, textAlign: 'center',
    }}>
      <div style={{
        width: 64, height: 64, borderRadius: tokens.radii.lg,
        border: `1px dashed ${tokens.borderHi}`,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontSize: 32, color: tokens.textMute, marginBottom: 20,
      }}>◱</div>
      <div style={{ fontSize: 18, fontWeight: 600, color: tokens.text, letterSpacing: '-0.02em', marginBottom: 6 }}>
        No sessions yet
      </div>
      <div style={{ fontSize: 13, color: tokens.textDim, maxWidth: 380, lineHeight: 1.55, marginBottom: 22 }}>
        A session runs claude in its own git worktree, so you can parallelize work without branch-switching.
      </div>
      <div style={{ display: 'flex', gap: 10 }}>
        <button style={{
          padding: '8px 14px', fontSize: 13, fontWeight: 600,
          background: tokens.text, color: tokens.bg,
          border: 'none', borderRadius: tokens.radii.md, cursor: 'pointer', fontFamily: 'inherit',
        }}>New session</button>
        <button style={{
          padding: '8px 14px', fontSize: 13, fontWeight: 500,
          background: 'transparent', color: tokens.text,
          border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.md, cursor: 'pointer', fontFamily: 'inherit',
        }}>Import from running claude</button>
      </div>
      <div style={{ marginTop: 28, fontSize: 11.5, color: tokens.textMute, fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' }}>
        ⌘N new session · ⌘K command palette
      </div>
    </div>
  );
}

// ─── Permission modal ────────────────────────────────────────────────

function PermissionModal({ tokens }) {
  return (
    <ModalShell tokens={tokens}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 4 }}>
        <span style={{
          width: 22, height: 22, borderRadius: 999,
          background: tokens.warn + '22', color: tokens.warn,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: 12, fontWeight: 700,
        }}>!</span>
        <div style={{ fontSize: 14, fontWeight: 600, color: tokens.text, letterSpacing: '-0.01em' }}>
          Allow Claude to run this command?
        </div>
      </div>
      <div style={{ fontSize: 12.5, color: tokens.textDim, marginBottom: 14 }}>
        From session <span style={{ color: tokens.text }}>"Worktree orchestration with LibGit2Sharp"</span>
      </div>

      <div style={{
        border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.md,
        background: tokens.panel2,
        padding: '10px 12px',
        fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
        fontSize: 12, color: tokens.text,
        marginBottom: 14,
      }}>
        <div style={{ color: tokens.textDim, fontSize: 10.5, marginBottom: 5, textTransform: 'uppercase', letterSpacing: '0.08em' }}>bash</div>
        <div><span style={{ color: tokens.accent }}>$</span> git worktree prune --verbose</div>
        <div style={{ color: tokens.textMute, marginTop: 2 }}>cwd: ~/Code/Conclave</div>
      </div>

      <div style={{ fontSize: 12, color: tokens.textDim, marginBottom: 16, lineHeight: 1.55 }}>
        This will remove worktree administrative files for any worktrees whose directories no longer exist. Read-only side effects only; no repo state is modified.
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 12, color: tokens.textDim, cursor: 'pointer' }}>
          <span style={{ width: 14, height: 14, borderRadius: tokens.radii.xs, border: `1px solid ${tokens.borderHi}` }} />
          Don't ask again for <span style={{ fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', color: tokens.text }}>git worktree *</span>
        </label>
        <div style={{ flex: 1 }} />
        <button style={{
          padding: '6px 12px', fontSize: 12.5, fontWeight: 500,
          background: 'transparent', color: tokens.text,
          border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.sm, cursor: 'pointer', fontFamily: 'inherit',
        }}>Deny</button>
        <button style={{
          padding: '6px 12px', fontSize: 12.5, fontWeight: 600,
          background: tokens.accent, color: tokens.accentFg,
          border: 'none', borderRadius: tokens.radii.sm, cursor: 'pointer', fontFamily: 'inherit',
        }}>Allow once</button>
      </div>
    </ModalShell>
  );
}

// ─── New-session modal ───────────────────────────────────────────────

function NewSessionModal({ tokens }) {
  return (
    <ModalShell tokens={tokens} width={540}>
      <div style={{ fontSize: 14, fontWeight: 600, color: tokens.text, letterSpacing: '-0.01em', marginBottom: 2 }}>
        New session
      </div>
      <div style={{ fontSize: 12, color: tokens.textDim, marginBottom: 18 }}>
        Spawn a claude instance in a fresh git worktree.
      </div>

      <Field tokens={tokens} label="Project">
        <div style={{
          display: 'flex', alignItems: 'center', gap: 8,
          padding: '8px 10px',
          border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.sm,
          fontSize: 13, color: tokens.text,
          background: tokens.bg,
        }}>
          <span>conclave</span>
          <span style={{ color: tokens.textMute, fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace', fontSize: 11.5, flex: 1 }}>
            ~/Code/Conclave
          </span>
          <span style={{ color: tokens.textMute }}>▾</span>
        </div>
      </Field>

      <Field tokens={tokens} label="Branch">
        <div style={{
          padding: '8px 10px',
          border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.sm,
          fontSize: 13, color: tokens.text, background: tokens.bg,
          fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
        }}>
          feat/session-repository
        </div>
        <div style={{ display: 'flex', gap: 6, marginTop: 6 }}>
          <BTag tokens={tokens} subtle>branch from main</BTag>
          <BTag tokens={tokens} subtle>existing branch</BTag>
          <BTag tokens={tokens} subtle>detached</BTag>
        </div>
      </Field>

      <Field tokens={tokens} label="Worktree path">
        <div style={{
          padding: '8px 10px',
          border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.sm,
          fontSize: 13, color: tokens.textDim, background: tokens.bg,
          fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
        }}>
          ~/Code/Conclave/wt/session-repository
        </div>
      </Field>

      <Field tokens={tokens} label="Model">
        <div style={{ display: 'flex', gap: 6 }}>
          {['Haiku 4.5', 'Sonnet 4.5', 'Opus 4'].map((m, i) => (
            <div key={m} style={{
              padding: '6px 12px', fontSize: 12.5, fontWeight: 500,
              border: `1px solid ${i === 1 ? tokens.text : tokens.border}`,
              background: i === 1 ? tokens.panel : 'transparent',
              color: tokens.text, borderRadius: tokens.radii.sm, cursor: 'pointer',
            }}>{m}</div>
          ))}
        </div>
      </Field>

      <Field tokens={tokens} label="Initial prompt (optional)">
        <div style={{
          padding: '10px 12px',
          border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.sm,
          fontSize: 13, color: tokens.textMute, background: tokens.bg,
          minHeight: 72, lineHeight: 1.5,
        }}>
          Start by reading NOTES.md and the existing Db/ folder. Then scaffold SessionRepository…
        </div>
      </Field>

      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 6 }}>
        <span style={{ fontSize: 11, color: tokens.textMute, fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' }}>
          ⌘⏎ create
        </span>
        <div style={{ flex: 1 }} />
        <button style={{
          padding: '6px 12px', fontSize: 12.5, fontWeight: 500,
          background: 'transparent', color: tokens.text,
          border: `1px solid ${tokens.border}`, borderRadius: tokens.radii.sm, cursor: 'pointer', fontFamily: 'inherit',
        }}>Cancel</button>
        <button style={{
          padding: '6px 14px', fontSize: 12.5, fontWeight: 600,
          background: tokens.text, color: tokens.bg,
          border: 'none', borderRadius: tokens.radii.sm, cursor: 'pointer', fontFamily: 'inherit',
        }}>Create session</button>
      </div>
    </ModalShell>
  );
}

function Field({ tokens, label, children }) {
  return (
    <div style={{ marginBottom: 14 }}>
      <div style={{ fontSize: 11, fontWeight: 600, color: tokens.textDim, textTransform: 'uppercase', letterSpacing: '0.08em', marginBottom: 6 }}>
        {label}
      </div>
      {children}
    </div>
  );
}

function ModalShell({ children, tokens, width = 480 }) {
  return (
    <div style={{
      position: 'absolute', inset: 0, zIndex: 20,
      background: 'color-mix(in oklch, black 40%, transparent)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      padding: 40,
    }}>
      <div style={{
        width, maxWidth: '100%',
        background: tokens.bg,
        border: `1px solid ${tokens.borderHi}`,
        borderRadius: tokens.radii.lg,
        boxShadow: '0 30px 60px -10px rgba(0,0,0,0.5), 0 10px 20px -5px rgba(0,0,0,0.25)',
        padding: 20,
      }}>
        {children}
      </div>
    </div>
  );
}

Object.assign(window, { VariantBFull });
