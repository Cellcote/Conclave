// Simple macOS window shell — traffic lights on a flat titlebar.
// For the Conclave mock we want the titlebar to blend with the app chrome,
// so we expose titlebarStyle + pass-through children.

function ConclaveWindow({ width = 1440, height = 900, titlebarStyle = {}, children, scale = 1 }) {
  const dot = (bg) => (
    <div style={{
      width: 12, height: 12, borderRadius: 999,
      background: bg,
      boxShadow: 'inset 0 0 0 0.5px rgba(0,0,0,0.2)',
    }} />
  );
  return (
    <div style={{
      width, height,
      borderRadius: 12,
      overflow: 'hidden',
      boxShadow: '0 40px 80px -20px rgba(0,0,0,0.45), 0 12px 24px -8px rgba(0,0,0,0.25), 0 0 0 0.5px rgba(0,0,0,0.4)',
      transform: `scale(${scale})`,
      transformOrigin: 'top left',
      position: 'relative',
      display: 'flex',
      flexDirection: 'column',
      background: '#000',
    }}>
      {/* Titlebar is rendered INSIDE each variant so the bg matches; we just provide a floating traffic-light cluster */}
      <div style={{
        position: 'absolute', top: 0, left: 0, right: 0, height: 36,
        display: 'flex', alignItems: 'center', paddingLeft: 14, gap: 8,
        zIndex: 10,
        pointerEvents: 'none',
      }}>
        {dot('#FF5F57')}
        {dot('#FEBC2E')}
        {dot('#28C840')}
      </div>
      {children}
    </div>
  );
}

Object.assign(window, { ConclaveWindow });
