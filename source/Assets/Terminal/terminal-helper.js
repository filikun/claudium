'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const readline = require('readline');
const http = require('http');
const https = require('https');
const { execFileSync } = require('child_process');
const nodePty = require('./node-pty');

const helperRoot = __dirname;

const sessions = new Map();
let currentCols = 120;
let currentRows = 32;
let usagePollTimer = null;
let usagePollInFlight = false;

let notifyPort = 0;
let notificationHookConfigured = false;
const pendingSessionOpens = [];

function emit(line) {
  process.stdout.write(line + '\n');
}

function emitData(sessionId, text) {
  const payload = Buffer.from(text, 'utf8').toString('base64');
  emit('data:' + sessionId + ':' + payload);
}

function emitStatus(sessionId, text) {
  emit('status:' + sessionId + ':' + text);
}

function emitUsage(data) {
  const payload = Buffer.from(JSON.stringify(data), 'utf8').toString('base64');
  emit('usage:' + payload);
}

/** Builds the argv for a native `claude.exe` invocation — no shell, so no quoting needed. */
function buildClaudeArgs(session) {
  const args = [];

  if (session.pluginDirWindowsPath) {
    args.push('--plugin-dir', session.pluginDirWindowsPath);
  }

  if (session.permissionMode) {
    args.push('--permission-mode', session.permissionMode);
  }

  if (session.model) {
    args.push('--model', session.model);
  }

  if (session.effort) {
    args.push('--effort', session.effort);
  }

  if (session.extraArgs) {
    // Best-effort whitespace split (no shell involved to do real quoting/globbing);
    // covers plain flags like "--verbose --add-dir C:\foo" but not quoted args with spaces.
    args.push(...session.extraArgs.split(/\s+/).filter(Boolean));
  }

  return args;
}

function readJsonFile(filePath) {
  try {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch (error) {
    return null;
  }
}

function ensureNotificationHookConfigured() {
  if (notificationHookConfigured) {
    return;
  }
  notificationHookConfigured = true;

  try {
    const marker = 'claudium-tab-notify';
    // `kind` tells the notify server (and in turn the C# side) which activity state to
    // switch the tab to: "waiting" for a blocked prompt, "working" once a new turn starts,
    // "idle" once Claude has finished responding (or the CLI reports it's idle again).
    //
    // Shell-form hook commands run under Git Bash on Windows (PowerShell only if Git Bash
    // isn't installed) — never cmd.exe — so this must use $VAR/`#`/`/dev/null`, not
    // %VAR%/`rem`/`NUL`. An earlier cmd.exe-flavored version of this command silently did
    // nothing: the literal, unexpanded "%CLAUDE_TAB_NOTIFY_PORT%" text was sent to curl
    // instead of the port number, so the notify server never matched a session.
    function commandFor(kind) {
      return (
        'curl -s --max-time 2 "http://127.0.0.1:${CLAUDE_TAB_NOTIFY_PORT}/notify?session=${CLAUDE_TAB_SESSION_ID}&kind=' +
        kind +
        '" >/dev/null 2>&1 # ' +
        marker
      );
    }

    const settingsPath = path.join(os.homedir(), '.claude', 'settings.json');
    fs.mkdirSync(path.dirname(settingsPath), { recursive: true });

    let data = readJsonFile(settingsPath);
    if (!data || typeof data !== 'object') {
      data = {};
    }

    if (!data.hooks || typeof data.hooks !== 'object') {
      data.hooks = {};
    }

    let changed = false;

    // Ensures `hookName` has exactly one claudium-tagged entry for the given matcher (undefined
    // for hook types that don't use one) whose command is `expectedCommand`. Replaces any
    // stale claudium-tagged entry that doesn't match — e.g. left over from a previous version
    // of this hook's command, like the pre-`kind=` format that silently broke Notification
    // hooks — instead of treating "a claudium hook is present" as "the right one is present".
    // Other tools' hooks in the same array are left untouched.
    function ensureEntry(hookName, matcher, expectedCommand) {
      let groups = Array.isArray(data.hooks[hookName]) ? data.hooks[hookName] : [];

      const alreadyCorrect = groups.some(
        (group) =>
          group &&
          (group.matcher || undefined) === matcher &&
          Array.isArray(group.hooks) &&
          group.hooks.some((h) => h && h.command === expectedCommand)
      );
      if (alreadyCorrect) {
        data.hooks[hookName] = groups;
        return;
      }

      groups = groups.filter(
        (group) =>
          !(
            group &&
            (group.matcher || undefined) === matcher &&
            Array.isArray(group.hooks) &&
            group.hooks.some((h) => h && typeof h.command === 'string' && h.command.includes(marker))
          )
      );

      const entry = { hooks: [{ type: 'command', command: expectedCommand }] };
      if (matcher !== undefined) {
        entry.matcher = matcher;
      }
      groups.push(entry);
      data.hooks[hookName] = groups;
      changed = true;
    }

    ensureEntry('Notification', 'permission_prompt', commandFor('waiting'));
    ensureEntry('Notification', 'idle_prompt', commandFor('idle'));
    ensureEntry('UserPromptSubmit', undefined, commandFor('working'));
    ensureEntry('Stop', undefined, commandFor('idle'));

    if (changed) {
      fs.writeFileSync(settingsPath, JSON.stringify(data, null, 2) + '\n', 'utf8');
    }
  } catch (error) {
    // Best-effort: the activity-indicator feature simply won't fire if this fails.
  }
}

function startNotifyServer() {
  const server = http.createServer((req, res) => {
    try {
      const url = new URL(req.url, 'http://localhost');
      const sessionId = url.searchParams.get('session');
      const kind = url.searchParams.get('kind');
      if (sessionId && sessions.has(sessionId) && (kind === 'waiting' || kind === 'working' || kind === 'idle')) {
        emit('activity:' + sessionId + ':' + kind);
      }
    } catch (error) {
      // Malformed request; ignore.
    }
    res.statusCode = 204;
    res.end();
  });

  server.on('error', () => {
    // Non-fatal: the waiting-indicator feature is best-effort.
  });

  server.listen(0, '127.0.0.1', () => {
    notifyPort = server.address().port;
    ensureNotificationHookConfigured();

    while (pendingSessionOpens.length > 0) {
      const [sessionId, sessionRequest] = pendingSessionOpens.shift();
      openSessionNow(sessionId, sessionRequest);
    }
  });
}

function openSession(sessionId, sessionRequest) {
  if (sessions.has(sessionId)) {
    return;
  }

  if (!sessionRequest || !sessionRequest.windowsPath) {
    emitStatus(sessionId, 'Ingen katalog vald.');
    return;
  }

  if (notifyPort === 0) {
    pendingSessionOpens.push([sessionId, sessionRequest]);
    return;
  }

  openSessionNow(sessionId, sessionRequest);
}

function openSessionNow(sessionId, sessionRequest) {
  if (!fs.existsSync(sessionRequest.windowsPath) || !fs.statSync(sessionRequest.windowsPath).isDirectory()) {
    emitStatus(sessionId, 'Mappen saknas: ' + sessionRequest.windowsPath);
    return;
  }

  try {
    execFileSync('claude.exe', ['--version'], { timeout: 5000, windowsHide: true, stdio: 'ignore' });
  } catch (error) {
    emitStatus(
      sessionId,
      'Claude Code CLI hittades inte. Installera det (se docs.claude.com/claude-code) och starta om Claudium.'
    );
    return;
  }

  const ptyProcess = nodePty.spawn('claude.exe', buildClaudeArgs(sessionRequest), {
    name: 'xterm-256color',
    cols: currentCols,
    rows: currentRows,
    cwd: sessionRequest.windowsPath,
    env: Object.assign({}, process.env, {
      TERM: 'xterm-256color',
      CLAUDE_TAB_SESSION_ID: sessionId,
      CLAUDE_TAB_NOTIFY_PORT: String(notifyPort)
    })
  });

  sessions.set(sessionId, { pty: ptyProcess });
  emitStatus(sessionId, 'Claude kor');

  ptyProcess.onData((data) => {
    emitData(sessionId, data);
  });

  ptyProcess.onExit((event) => {
    emit('exit:' + sessionId + ':' + event.exitCode);
    sessions.delete(sessionId);
  });

  ensureUsagePolling();
}

function closeSession(sessionId) {
  const entry = sessions.get(sessionId);
  if (!entry) {
    return;
  }

  sessions.delete(sessionId);

  try {
    entry.pty.kill();
  } catch (error) {
    // Already gone; nothing to clean up.
  }
}

function resizeAll(cols, rows) {
  currentCols = cols;
  currentRows = rows;

  sessions.forEach((entry) => {
    try {
      entry.pty.resize(cols, rows);
    } catch (error) {
      // Pty may have just exited; ignore.
    }
  });
}

function ensureUsagePolling() {
  if (usagePollTimer) {
    return;
  }

  pollUsage();
  usagePollTimer = setInterval(pollUsage, 180000);
}

function stopUsagePolling() {
  if (usagePollTimer) {
    clearInterval(usagePollTimer);
    usagePollTimer = null;
  }
}

function normalizeApiUsage(payload) {
  function section(value) {
    if (!value) {
      return null;
    }
    const percent = value.utilization;
    const resetsAt = value.resets_at;
    if (percent == null && !resetsAt) {
      return null;
    }
    return { percent, resets_at: resetsAt };
  }

  return {
    ok: true,
    source: 'oauth_usage_api',
    session: section(payload.five_hour),
    all_models: section(payload.seven_day),
    sonnet: section(payload.seven_day_sonnet),
    opus: section(payload.seven_day_opus)
  };
}

function normalizeMonitorCache(payload) {
  const limits = payload.limits || {};

  function section(name) {
    const value = limits[name] || {};
    const percent = value.used_percentage;
    const resetEpoch = value.resets_at_epoch;
    let resetsAt = null;
    if (resetEpoch) {
      try {
        resetsAt = new Date(resetEpoch * 1000).toISOString();
      } catch (error) {
        resetsAt = null;
      }
    }
    if (percent == null && !resetsAt) {
      return null;
    }
    return { percent, resets_at: resetsAt };
  }

  return {
    ok: true,
    source: 'claude_monitor_cache',
    session: section('five_hour'),
    all_models: section('seven_day'),
    sonnet: section('seven_day_sonnet'),
    opus: section('seven_day_opus')
  };
}

function getOAuthToken() {
  if (process.env.CLAUDE_CODE_OAUTH_TOKEN) {
    return process.env.CLAUDE_CODE_OAUTH_TOKEN;
  }

  const payload = readJsonFile(path.join(os.homedir(), '.claude', '.credentials.json'));
  return (payload && payload.claudeAiOauth && payload.claudeAiOauth.accessToken) || null;
}

function resolveUsageFromLocalCache(callback) {
  const candidates = [
    path.join(os.homedir(), '.claude-monitor', 'api', 'latest.json'),
    path.join(os.homedir(), '.claude-monitor', 'state', 'latest.json')
  ];

  for (const candidate of candidates) {
    const payload = readJsonFile(candidate);
    if (!payload) {
      continue;
    }
    if (payload.five_hour || payload.seven_day) {
      callback(normalizeApiUsage(payload));
      return;
    }
    if (payload.limits) {
      callback(normalizeMonitorCache(payload));
      return;
    }
  }

  callback({ ok: false, reason: 'unavailable' });
}

function fetchUsage(callback) {
  const token = getOAuthToken();
  if (!token) {
    resolveUsageFromLocalCache(callback);
    return;
  }

  let version = '2.1.0';
  try {
    const output = execFileSync('claude.exe', ['--version'], { encoding: 'utf8', timeout: 3000 }).trim();
    if (output) {
      version = output.split(/\s+/).pop();
    }
  } catch (error) {
    // Keep the fallback version string.
  }

  const request = https.request(
    'https://api.anthropic.com/api/oauth/usage',
    {
      method: 'GET',
      headers: {
        Authorization: 'Bearer ' + token,
        'anthropic-beta': 'oauth-2025-04-20',
        'User-Agent': 'claude-code/' + version,
        'Content-Type': 'application/json'
      },
      timeout: 8000
    },
    (response) => {
      let body = '';
      response.on('data', (chunk) => {
        body += chunk;
      });
      response.on('end', () => {
        try {
          callback(normalizeApiUsage(JSON.parse(body)));
        } catch (error) {
          resolveUsageFromLocalCache(callback);
        }
      });
    }
  );

  request.on('error', () => resolveUsageFromLocalCache(callback));
  request.on('timeout', () => {
    request.destroy();
    resolveUsageFromLocalCache(callback);
  });
  request.end();
}

function pollUsage() {
  if (usagePollInFlight) {
    return;
  }

  usagePollInFlight = true;
  fetchUsage((result) => {
    usagePollInFlight = false;
    emitUsage(result);
  });
}

function handleLine(line) {
  if (line.startsWith('open:')) {
    const rest = line.slice(5);
    const separatorIndex = rest.indexOf(':');
    if (separatorIndex === -1) {
      return;
    }

    const sessionId = rest.slice(0, separatorIndex);
    const payload = rest.slice(separatorIndex + 1);

    let sessionRequest = null;
    try {
      sessionRequest = JSON.parse(Buffer.from(payload, 'base64').toString('utf8'));
    } catch (error) {
      emitStatus(sessionId, 'Ogiltig sessionsdata mottagen.');
      return;
    }

    openSession(sessionId, sessionRequest);
    return;
  }

  if (line.startsWith('close:')) {
    const sessionId = line.slice(6);
    closeSession(sessionId);
    return;
  }

  if (line === 'shutdown') {
    stopUsagePolling();
    sessions.forEach((entry) => {
      try {
        entry.pty.kill();
      } catch (error) {
        // Already gone; nothing to clean up.
      }
    });
    sessions.clear();
    process.exit(0);
    return;
  }

  if (line.startsWith('input:')) {
    const rest = line.slice(6);
    const separatorIndex = rest.indexOf(':');
    if (separatorIndex === -1) {
      return;
    }

    const sessionId = rest.slice(0, separatorIndex);
    const payload = rest.slice(separatorIndex + 1);
    const entry = sessions.get(sessionId);
    if (!entry) {
      return;
    }

    const text = Buffer.from(payload, 'base64').toString('utf8');
    entry.pty.write(text);
    return;
  }

  if (line.startsWith('resize:') || line.startsWith('init:')) {
    const parts = line.split(':');
    if (parts.length === 3) {
      const cols = parseInt(parts[1], 10);
      const rows = parseInt(parts[2], 10);
      if (!Number.isNaN(cols) && !Number.isNaN(rows)) {
        resizeAll(cols, rows);
      }
    }
    return;
  }
}

process.on('uncaughtException', (error) => {
  const text = '\r\n[helper error] ' + (error && error.stack ? error.stack : String(error)) + '\r\n';
  sessions.forEach((_entry, sessionId) => emitData(sessionId, text));
});

startNotifyServer();

const rl = readline.createInterface({
  input: process.stdin,
  crlfDelay: Infinity
});

rl.on('line', handleLine);
