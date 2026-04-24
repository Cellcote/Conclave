// Conclave mock data — projects, worktrees, sessions
// A "session" is a running claude CLI instance tied to a worktree.
// A worktree may be linked to a PR.

const CONCLAVE_DATA = {
  projects: [
    {
      id: 'conclave',
      name: 'conclave',
      path: '~/Code/Conclave',
      icon: '▣',
      defaultBranch: 'main',
      sessions: [
        {
          id: 's1',
          title: 'Add SQLite persistence layer',
          worktree: 'wt/sqlite-persistence',
          branch: 'feat/sqlite',
          pr: { number: 142, state: 'draft' },
          status: 'working', // working | waiting | idle | running-tool | error | queued
          model: 'Sonnet 4.5',
          diff: { files: 8, add: 412, del: 37 },
          lastActivity: '12s ago',
          unread: 3,
          lastTool: { kind: 'edit', target: 'src/Conclave.App/Db/Migrations.cs', ok: true },
          lastMessage: 'Wiring the Microsoft.Data.Sqlite connection factory and the first migration that creates projects + sessions tables.',
        },
        {
          id: 's2',
          title: 'Worktree orchestration with LibGit2Sharp',
          worktree: 'wt/git-worktrees',
          branch: 'feat/worktrees',
          pr: null,
          status: 'waiting',
          model: 'Sonnet 4.5',
          diff: { files: 3, add: 104, del: 9 },
          lastActivity: '1m ago',
          unread: 1,
          lastTool: { kind: 'bash', target: 'git worktree add ../wt-sqlite feat/sqlite', ok: true },
          lastMessage: 'Needs your permission to run `git worktree prune` before adding the new worktree.',
        },
        {
          id: 's3',
          title: 'Fix wide char / emoji alignment in VT grid',
          worktree: 'wt/wide-chars',
          branch: 'fix/vt-wide',
          pr: { number: 138, state: 'open' },
          status: 'idle',
          model: 'Opus 4',
          diff: { files: 2, add: 58, del: 14 },
          lastActivity: '14m ago',
          unread: 0,
          lastTool: { kind: 'read', target: 'src/Conclave.App/Terminal/GlyphCache.cs', ok: true },
          lastMessage: 'Verified east-asian-width table loads. Ready for next prompt.',
        },
        {
          id: 's4',
          title: 'Bracketed paste + OSC 8 hyperlinks',
          worktree: 'wt/vt-ext',
          branch: 'feat/vt-extensions',
          pr: null,
          status: 'running-tool',
          model: 'Sonnet 4.5',
          diff: { files: 1, add: 22, del: 2 },
          lastActivity: 'now',
          unread: 0,
          lastTool: { kind: 'bash', target: 'dotnet test Conclave.Tests --filter VtParserTests', ok: null },
          lastMessage: 'Running the VT parser test suite…',
        },
      ],
    },
    {
      id: 'portapty',
      name: 'porta.pty',
      path: '~/Code/oss/porta.pty',
      icon: '◇',
      defaultBranch: 'main',
      sessions: [
        {
          id: 's5',
          title: 'ConPTY resize race on Windows 11',
          worktree: 'wt/conpty-resize',
          branch: 'fix/conpty-resize',
          pr: { number: 44, state: 'open' },
          status: 'error',
          model: 'Opus 4',
          diff: { files: 4, add: 77, del: 31 },
          lastActivity: '3m ago',
          unread: 2,
          lastTool: { kind: 'bash', target: 'dotnet build -c Release', ok: false },
          lastMessage: 'Build failed: CS0117 — PseudoConsole.Resize is not defined on this target framework.',
        },
        {
          id: 's6',
          title: 'Document forkpty fallback path',
          worktree: 'wt/docs',
          branch: 'docs/forkpty',
          pr: null,
          status: 'completed',
          model: 'Haiku 4.5',
          diff: { files: 1, add: 38, del: 0 },
          lastActivity: '28m ago',
          unread: 0,
          lastTool: { kind: 'write', target: 'docs/posix.md', ok: true },
          lastMessage: 'Done. README updated with the POSIX section.',
        },
      ],
    },
    {
      id: 'glyphkit',
      name: 'glyphkit',
      path: '~/Code/glyphkit',
      icon: '△',
      defaultBranch: 'trunk',
      sessions: [
        {
          id: 's7',
          title: 'Ligature caching pass',
          worktree: 'wt/ligatures',
          branch: 'perf/ligatures',
          pr: { number: 7, state: 'merged' },
          status: 'queued',
          model: 'Sonnet 4.5',
          diff: { files: 0, add: 0, del: 0 },
          lastActivity: 'queued',
          unread: 0,
          lastTool: null,
          lastMessage: 'Waiting for free worker…',
        },
      ],
    },
  ],
};

// Active session for the mock — the one shown in the main pane
const ACTIVE_SESSION_ID = 's1';

// A realistic transcript for the active session
const TRANSCRIPT = [
  {
    role: 'user',
    time: '14:02',
    content: "Let's add the SQLite persistence layer. Start with the schema for projects and sessions tables, and wire up the connection factory. Follow the pattern in NOTES.md.",
  },
  {
    role: 'assistant',
    time: '14:02',
    content: "I'll read NOTES.md first to confirm the persistence pattern you have in mind, then scaffold the Db folder with a connection factory, a migration runner, and the first migration.",
    tools: [
      { kind: 'read', target: 'NOTES.md', ok: true, meta: '142 lines' },
      { kind: 'read', target: 'src/Conclave.App/Conclave.App.csproj', ok: true, meta: '38 lines' },
    ],
  },
  {
    role: 'assistant',
    time: '14:03',
    content: "NOTES.md is clear: SQLite lives next to the app binary, migrations are numbered .sql files compiled as embedded resources, and the connection factory opens WAL mode on startup. I'll add the Microsoft.Data.Sqlite package, create Db/ with Connection.cs + Migrator.cs, and drop in 001_init.sql with projects + sessions tables.",
    tools: [
      { kind: 'bash', target: 'dotnet add src/Conclave.App package Microsoft.Data.Sqlite --version 10.0.0', ok: true, meta: 'exit 0 · 1.4s' },
      { kind: 'write', target: 'src/Conclave.App/Db/Connection.cs', ok: true, meta: '+68 lines' },
      { kind: 'write', target: 'src/Conclave.App/Db/Migrator.cs', ok: true, meta: '+94 lines' },
      { kind: 'write', target: 'src/Conclave.App/Db/Migrations/001_init.sql', ok: true, meta: '+41 lines' },
    ],
  },
  {
    role: 'assistant',
    time: '14:04',
    content: "Wiring the factory into Program.cs so the migrator runs before the main window opens. I'll also register IDbConnectionFactory in the DI container so the session repository can take it by constructor injection in the next step.",
    tools: [
      { kind: 'edit', target: 'src/Conclave.App/Program.cs', ok: true, meta: '+12 −2' },
      { kind: 'edit', target: 'src/Conclave.App/Db/Migrations.cs', ok: true, meta: '+18 −0' },
    ],
  },
];

Object.assign(window, { CONCLAVE_DATA, ACTIVE_SESSION_ID, TRANSCRIPT });
