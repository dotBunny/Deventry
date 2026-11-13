// Spike: could a Discord app installed to the user's own account, and in no server, be
// Deventry's remote client (DESIGN.md §18, "Remote clients", Later clients)?
//
// Discord documents that such an app's commands and buttons work in a DM with its bot. It
// doesn't say whether the bot can message the user first without sharing a server, which
// Deventry needs to say a tab is waiting. This script tries each step on a real account and
// keeps score; spikes/README.md ("Discord") has the steps.
//
// Usage: node user-install.mjs [--raw]
//   It asks for the bot's token without showing it, and never writes it anywhere.
//   --raw prints every event in full (without tokens) as well as logging it.
// What it learns stays in <OS temp>/deventry-discord-spike/: state.json (your Discord user id,
// your DM with the bot and the findings so far) and spike.log (the events, without tokens).
// DISCORD_BOT_TOKEN, if set, is used instead of asking; DISCORD_API points the script at a fake
// Discord instead of https://discord.com/api/v10.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import readline from 'node:readline';

const API = (process.env.DISCORD_API ?? 'https://discord.com/api/v10').replace(/\/+$/, '');
const USER_AGENT = 'DiscordBot (https://github.com/dotBunny/Deventry, spike)';
const RAW = process.argv.includes('--raw');

const INTENT_DIRECT_MESSAGES = 1 << 12;
const FLAG_SUPPRESS_NOTIFICATIONS = 1 << 12;
const FLAG_EPHEMERAL = 1 << 6;
const CONTEXT_BOT_DM = 1;
const CONTEXTS = { 0: 'in a server', 1: "in the bot's DM", 2: 'in another DM or group' };
const USER_INSTALL = '1';
const GUILD_INSTALL = '0';
const FATAL_CLOSES = {
  4004: 'Discord refused the token.',
  4010: 'Invalid shard.',
  4011: 'Sharding required.',
  4012: 'Invalid API version.',
  4013: 'Invalid intents.',
  4014: "Disallowed intents: the bot asked for one the Developer Portal hasn't turned on.",
};

const QUESTIONS = [
  ['noServer', 'The bot is in no server'],
  ['userInstall', 'User Install is on'],
  ['dmNotInstalled', 'A DM before you added it'],
  ['dmInstalled', 'A DM once added, before you used it'],
  ['commandInDm', "/pair in the bot's DM"],
  ['commandElsewhere', '/pair somewhere else'],
  ['buttonOnReply', "A button on /pair's answer"],
  ['plainMessage', 'Your own message reaches the bot'],
  ['plainText', '... with its text'],
  ['replyToPlain', 'The bot answers your message'],
  ['dmAfterContact', 'A DM after you used it'],
  ['dmAfter15', 'A DM 15 minutes or more after'],
  ['dmAfterRestart', 'A DM after the script restarted'],
  ['buttonOnUnprompted', 'A button on a DM the bot sent first'],
  ['edit', 'Editing a message it sent'],
  ['notified', 'A DM notified you'],
  ['silentQuiet', 'A silent DM stayed quiet'],
];
const QUESTION_TEXT = Object.fromEntries(QUESTIONS);

const HELP = `Commands:
  installed        you've added the app to your account
  dm [text]        the bot messages you now
  buttons          the bot messages you, with buttons
  silent           the bot messages you with @silent
  later <minutes>  the bot messages you after that many minutes
  edit             the bot edits the last message it sent
  notified yes|no  whether the last message notified you
  links            the links to add the app and open its DM
  status           the findings so far
  reset            forget the findings, to start again
  quit             print the findings and stop`;

const root = path.join(os.tmpdir(), 'deventry-discord-spike');
const statePath = path.join(root, 'state.json');
const logPath = path.join(root, 'spike.log');
const started = Date.now();

let token = '';
let state = { findings: {} };
let appId = '';
let botId = '';
let ownerId = '';
let gateway = null;
let rl = null;
let quitting = false;

// ---- Logging, state and findings

const redact = (text) => (token ? String(text).split(token).join('<token>') : String(text));
const time = () => new Date().toTimeString().slice(0, 8);

function log(line) {
  const text = `[${time()}] ${redact(line)}`;
  console.log(text);
  fs.appendFileSync(logPath, `${text}\n`);
}

function logRaw(label, value) {
  // Interaction tokens answer for 15 minutes; leave them out with the bot's.
  const json = redact(JSON.stringify(value, (key, v) => (key === 'token' ? '<interaction token>' : v)));
  fs.appendFileSync(logPath, `[${time()}] ${label} ${json}\n`);
  if (RAW) console.log(`[${time()}] ${label} ${json}`);
}

function loadState() {
  try {
    return JSON.parse(fs.readFileSync(statePath, 'utf8'));
  } catch {
    return { findings: {} };
  }
}

const saveState = () => fs.writeFileSync(statePath, JSON.stringify(state, null, 2));

function found(key, ok, detail = '', replace = false) {
  const finding = replace ? { yes: 0, no: 0 } : (state.findings[key] ?? { yes: 0, no: 0 });
  if (ok) {
    finding.yes++;
    finding.lastYes = detail;
  } else {
    finding.no++;
    finding.lastNo = detail;
  }
  state.findings[key] = finding;
  saveState();
  log(`${ok ? 'YES' : 'NO '} ${QUESTION_TEXT[key]}${detail ? ` (${detail})` : ''}`);
}

function printFindings() {
  const width = Math.max(...QUESTIONS.map(([, q]) => q.length)) + 3;
  const lines = [`Discord user-install spike, findings on ${new Date().toISOString().slice(0, 10)}:`];
  for (const [key, question] of QUESTIONS) {
    const f = state.findings[key];
    let answer = 'not tried';
    if (f?.yes && !f.no) answer = `yes${f.lastYes ? ` (${f.lastYes})` : ''}`;
    else if (f?.no && !f.yes) answer = `no${f.lastNo ? ` (${f.lastNo})` : ''}`;
    else if (f) answer = `yes ${f.yes}x, no ${f.no}x (last no: ${f.lastNo || '?'})`;
    lines.push(`  ${`${question} `.padEnd(width, '.')} ${answer}`);
  }
  console.log(`\n${lines.join('\n')}\n`);
}

// ---- Discord's HTTP API

const describe = (r) =>
  r.status === 0
    ? `no answer: ${r.error}`
    : `${r.status}${r.json?.code !== undefined ? ` code ${r.json.code}` : ''}${r.json?.message ? ` ${r.json.message}` : ''}`;

async function api(method, route, body, { auth = true, label = `${method} ${route}` } = {}) {
  for (let attempt = 1; ; attempt++) {
    let response;
    try {
      response = await fetch(API + route, {
        method,
        headers: {
          'User-Agent': USER_AGENT,
          ...(auth ? { Authorization: `Bot ${token}` } : {}),
          ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
    } catch (e) {
      const failed = { ok: false, status: 0, json: null, error: e.cause?.message ?? e.message };
      log(`${label}: ${describe(failed)}`);
      return failed;
    }
    const text = await response.text();
    let json = null;
    try {
      json = text ? JSON.parse(text) : null;
    } catch {
      json = { message: text.slice(0, 200) };
    }
    if (response.status === 429 && attempt <= 3) {
      const seconds = Number(json?.retry_after ?? response.headers.get('retry-after') ?? 1);
      log(`${label}: Discord asked the bot to wait ${seconds} s.`);
      await new Promise((resolve) => setTimeout(resolve, seconds * 1000 + 100));
      continue;
    }
    const result = { ok: response.ok, status: response.status, json };
    if (!response.ok) log(`${label}: ${describe(result)}${json?.errors ? ` ${JSON.stringify(json.errors)}` : ''}`);
    return result;
  }
}

// ---- The Gateway: a WebSocket Discord sends DMs and interactions down

class Gateway {
  ws = null;
  url = null;
  resumeUrl = null;
  sessionId = null;
  seq = null;
  beat = null;
  acked = true;
  stopping = false;

  constructor(onDispatch) {
    this.onDispatch = onDispatch;
  }

  async start() {
    const where = await api('GET', '/gateway/bot');
    if (!where.ok) {
      console.error(`Couldn't find Discord's Gateway: ${describe(where)}`);
      process.exit(1);
    }
    this.url = where.json.url;
    this.open(false);
  }

  open(resume) {
    const base = resume && this.resumeUrl ? this.resumeUrl : this.url;
    const ws = new WebSocket(`${base}/?v=10&encoding=json`);
    this.ws = ws;
    ws.addEventListener('message', (e) => {
      if (ws === this.ws) this.receive(JSON.parse(e.data), resume);
    });
    ws.addEventListener('close', (e) => {
      if (ws === this.ws) this.closed(e.code, e.reason);
    });
    ws.addEventListener('error', () => {}); // a close follows
  }

  send(payload) {
    if (this.ws?.readyState === WebSocket.OPEN) this.ws.send(JSON.stringify(payload));
  }

  receive(p, resume) {
    if (p.s != null) this.seq = p.s;
    switch (p.op) {
      case 10: // Hello
        this.heartbeat(p.d.heartbeat_interval);
        if (resume && this.sessionId) {
          this.send({ op: 6, d: { token, session_id: this.sessionId, seq: this.seq } });
        } else {
          const properties = { os: process.platform, browser: 'deventry-spike', device: 'deventry-spike' };
          this.send({ op: 2, d: { token, intents: INTENT_DIRECT_MESSAGES, properties } });
        }
        break;
      case 11: // Heartbeat ACK
        this.acked = true;
        break;
      case 1: // Heartbeat asked for
        this.send({ op: 1, d: this.seq });
        break;
      case 7: // Reconnect
        log('Discord asked the bot to reconnect.');
        this.restart(true);
        break;
      case 9: // Invalid Session
        log(`Discord ended the session${p.d ? ', which can be resumed' : ''}.`);
        if (!p.d) {
          this.sessionId = null;
          this.seq = null;
        }
        clearInterval(this.beat);
        setTimeout(() => this.restart(Boolean(p.d)), 1000 + Math.random() * 4000);
        break;
      case 0: // Dispatch
        if (p.t === 'READY') {
          this.sessionId = p.d.session_id;
          this.resumeUrl = p.d.resume_gateway_url;
        }
        Promise.resolve(this.onDispatch(p.t, p.d)).catch((e) => log(`Handling ${p.t} failed: ${e.stack ?? e}`));
        break;
    }
  }

  heartbeat(interval) {
    clearInterval(this.beat);
    this.acked = true;
    const beat = () => {
      if (!this.acked) {
        log('Discord stopped answering heartbeats; reconnecting.');
        this.restart(true);
        return;
      }
      this.acked = false;
      this.send({ op: 1, d: this.seq });
    };
    this.beat = setTimeout(() => {
      beat();
      this.beat = setInterval(beat, interval);
    }, interval * Math.random());
  }

  restart(resume) {
    clearInterval(this.beat);
    const old = this.ws;
    this.ws = null;
    try {
      old?.close(4000); // not 1000, which would end the session
    } catch {}
    this.open(resume);
  }

  closed(code, reason) {
    clearInterval(this.beat);
    if (this.stopping) return;
    if (FATAL_CLOSES[code]) {
      log(`Discord closed the Gateway: ${code} ${FATAL_CLOSES[code]}`);
      process.exit(1);
    }
    if (code === 4007 || code === 4009) {
      this.sessionId = null;
      this.seq = null;
    }
    log(`The Gateway closed (${code}${reason ? ` ${reason}` : ''}); reconnecting in 2 s.`);
    setTimeout(() => this.open(Boolean(this.sessionId)), 2000);
  }

  stop() {
    this.stopping = true;
    clearInterval(this.beat);
    try {
      this.ws?.close(1000);
    } catch {}
  }
}

// ---- What arrives

async function dispatch(type, d) {
  switch (type) {
    case 'READY':
      logRaw('READY', { user: d.user, guilds: d.guilds.length, application: d.application });
      log(`Connected as ${d.user.username}, in ${d.guilds.length} server(s).`);
      found('noServer', d.guilds.length === 0,
        d.guilds.length === 0 ? '' : `it's in ${d.guilds.length}; remove it, or the results don't answer the question`, true);
      break;
    case 'RESUMED':
      log('Resumed the session.');
      break;
    case 'MESSAGE_CREATE':
      await onMessage(d);
      break;
    case 'INTERACTION_CREATE':
      await onInteraction(d);
      break;
    default:
      logRaw(type, d);
  }
}

function contact(dmChannelId) {
  const now = Date.now();
  state.installedAt ??= now; // using it means it's on the account
  state.contactAt ??= now;
  state.lastUserActionAt = now;
  if (dmChannelId) state.dmChannelId = dmChannelId;
  saveState();
}

async function onMessage(m) {
  logRaw('MESSAGE_CREATE', m);
  if (m.author?.id === botId) return;
  if (m.guild_id) {
    log("A message from a server arrived: the bot is in a server, so this isn't the case being tested.");
    return;
  }
  if (m.author?.id !== ownerId) {
    log(`Ignored a DM from ${m.author?.username}: the spike answers its owner only.`);
    return;
  }
  contact(m.channel_id);
  const text = m.content ?? '';
  found('plainMessage', true);
  found('plainText', text.length > 0, text ? '' : 'the message arrived with no text');
  const reply = await api('POST', `/channels/${m.channel_id}/messages`, {
    content: text ? `Got your message (${text.length} characters).` : 'Got your message, but not its text.',
    message_reference: { message_id: m.id },
  });
  found('replyToPlain', reply.ok, reply.ok ? '' : describe(reply));
  if (reply.ok) remember(m.channel_id, reply.json, 'reply');
}

const respond = (i, body) =>
  api('POST', `/interactions/${i.id}/${i.token}/callback`, body, { auth: false, label: 'Answering an interaction' });

const buttons = (kind) => [
  {
    type: 1,
    components: [
      { type: 2, style: 3, label: 'Allow', custom_id: `spike:${kind}:allow` },
      { type: 2, style: 4, label: 'Deny', custom_id: `spike:${kind}:deny` },
    ],
  },
];

async function onInteraction(i) {
  logRaw('INTERACTION_CREATE', i);
  const user = i.user ?? i.member?.user;
  const where = CONTEXTS[i.context] ?? `in context ${i.context}`;
  const inDm = i.context === CONTEXT_BOT_DM;
  if (user?.id !== ownerId) {
    await respond(i, { type: 4, data: { content: 'This spike answers its owner only.', flags: FLAG_EPHEMERAL } });
    log(`Turned away ${user?.username} ${where}.`);
    return;
  }
  // Answer first: Discord gives an interaction 3 seconds.
  if (i.type === 2) {
    const r = await respond(i, {
      type: 4,
      data: { content: 'Paired. Try a button:', components: buttons('reply'), flags: inDm ? 0 : FLAG_EPHEMERAL },
    });
    contact(inDm ? (i.channel?.id ?? i.channel_id) : null);
    const owners = Object.keys(i.authorizing_integration_owners ?? {});
    const installed = owners.includes(USER_INSTALL)
      ? 'installed to you'
      : owners.includes(GUILD_INSTALL) ? 'installed to a server' : 'installed to nothing it named';
    found(inDm ? 'commandInDm' : 'commandElsewhere', r.ok,
      `${where}, ${installed}${r.ok ? '' : `; answering failed: ${describe(r)}`}`);
  } else if (i.type === 3) {
    const [, kind, choice] = String(i.data?.custom_id ?? '').split(':');
    const r = await respond(i, {
      type: 7,
      data: { content: `${i.message?.content ?? ''}\nYou clicked ${choice} at ${time()}.`, components: [] },
    });
    contact(inDm ? (i.channel?.id ?? i.channel_id) : null);
    found(kind === 'unprompted' ? 'buttonOnUnprompted' : 'buttonOnReply', r.ok,
      `${where}${r.ok ? '' : `; answering failed: ${describe(r)}`}`);
  }
}

// ---- What the bot sends first

function dmKeys() {
  if (!state.contactAt) return [state.installedAt ? 'dmInstalled' : 'dmNotInstalled'];
  const keys = ['dmAfterContact'];
  if (Date.now() - state.lastUserActionAt >= 15 * 60_000) keys.push('dmAfter15');
  if (state.lastUserActionAt < started) keys.push('dmAfterRestart');
  return keys;
}

function when() {
  if (!state.contactAt) return state.installedAt ? 'added, never used' : 'not added';
  const minutes = Math.round((Date.now() - state.lastUserActionAt) / 60_000);
  return `${minutes} min after you last used it${state.lastUserActionAt < started ? ', after a restart' : ''}`;
}

function remember(channelId, message, kind) {
  state.lastSent = { channelId, messageId: message.id, content: message.content, kind };
  saveState();
}

async function sendDm(kind, text) {
  if (!state.contactAt && !state.installedAt) {
    console.log("Counting this as before you added the app. If you've added it, type `installed` first.");
  }
  const keys = dmKeys();
  const detail = [when()];
  const body = { content: text };
  if (kind === 'buttons') body.components = buttons('unprompted');
  if (kind === 'silent') body.flags = FLAG_SUPPRESS_NOTIFICATIONS;

  // Opening a DM gives back the one there is, if there is one.
  const open = await api('POST', '/users/@me/channels', { recipient_id: ownerId });
  let channelId = open.ok ? open.json.id : null;
  if (!open.ok) detail.push(`opening the DM: ${describe(open)}`);
  if (!channelId && state.dmChannelId) {
    channelId = state.dmChannelId;
    detail.push('so it tried the DM you opened');
  }
  const sent = channelId ? await api('POST', `/channels/${channelId}/messages`, body) : null;
  if (sent && !sent.ok) detail.push(`sending: ${describe(sent)}`);
  for (const key of keys) found(key, Boolean(sent?.ok), detail.join('; '));
  if (!sent?.ok) return;
  remember(channelId, sent.json, kind);
  console.log(kind === 'silent'
    ? 'Sent silently. Did Discord notify you anyway? Type `notified yes` or `notified no`.'
    : 'Sent. Did Discord notify you? Type `notified yes` or `notified no`.');
}

async function edit() {
  const last = state.lastSent;
  if (!last) {
    console.log("The bot hasn't sent anything to edit yet.");
    return;
  }
  const r = await api('PATCH', `/channels/${last.channelId}/messages/${last.messageId}`, {
    content: `${last.content} (edited at ${time()})`,
  });
  found('edit', r.ok, r.ok ? '' : describe(r));
}

function notified(answer) {
  if (!/^(y|n)/i.test(answer)) {
    console.log('Type `notified yes` or `notified no`.');
    return;
  }
  const yes = /^y/i.test(answer);
  if (state.lastSent?.kind === 'silent') found('silentQuiet', !yes, yes ? 'it notified you' : '');
  else found('notified', yes);
}

function links() {
  console.log(`
1. Add the app to your account (choose "Add to My Apps"), then type \`installed\`:
   https://discord.com/oauth2/authorize?client_id=${appId}&integration_type=1&scope=applications.commands
2. Open its profile and choose Message, for a DM with it:
   https://discord.com/users/${botId}
`);
}

// ---- Commands typed here

async function command(line) {
  const [name = '', ...rest] = line.trim().split(/\s+/);
  const arg = rest.join(' ');
  switch (name.toLowerCase()) {
    case '':
      return;
    case 'installed':
      state.installedAt ??= Date.now();
      saveState();
      console.log('Noted: the app is on your account.');
      return;
    case 'dm':
      return sendDm('dm', arg || `A message the bot sent first, at ${time()}.`);
    case 'buttons':
      return sendDm('buttons', `The bot sent this first, at ${time()}. Try a button:`);
    case 'silent':
      return sendDm('silent', `A silent message (@silent), at ${time()}.`);
    case 'later': {
      const minutes = Number(arg);
      if (!(minutes > 0)) {
        console.log('Type `later <minutes>`, such as `later 20`.');
        return;
      }
      log(`The bot will message you in ${minutes} min.`);
      setTimeout(() => {
        sendDm('dm', `Sent ${minutes} min after you asked, at ${time()}.`).catch((e) => log(`That failed: ${e.stack ?? e}`));
      }, minutes * 60_000);
      return;
    }
    case 'edit':
      return edit();
    case 'notified':
      return notified(arg);
    case 'links':
      return links();
    case 'status':
      return printFindings();
    case 'reset':
      state = { appId, findings: {} };
      saveState();
      console.log('Forgot everything found so far.');
      return;
    case 'quit':
    case 'exit':
      return quit();
    case 'help':
      console.log(HELP);
      return;
    default:
      console.log(`Unknown command "${name}".\n${HELP}`);
  }
}

function quit() {
  if (quitting) return;
  quitting = true;
  printFindings();
  console.log(`The log is ${logPath}; the findings stay in ${statePath} for the next run.`);
  gateway?.stop();
  rl?.close();
  process.exit(0);
}

// ---- Starting

function askHidden(question) {
  return new Promise((resolve) => {
    const stdin = process.stdin;
    process.stdout.write(question);
    stdin.setRawMode(true);
    stdin.setEncoding('utf8');
    stdin.resume();
    let text = '';
    const onData = (chunk) => {
      for (const c of chunk) {
        if (c === '\r' || c === '\n') {
          stdin.off('data', onData);
          stdin.setRawMode(false);
          stdin.pause();
          process.stdout.write('\n');
          resolve(text.trim());
          return;
        }
        if (c === '\u0003') process.exit(130);
        if (c === '\u0008' || c === '\u007f') text = text.slice(0, -1);
        else text += c;
      }
    };
    stdin.on('data', onData);
  });
}

async function main() {
  fs.mkdirSync(root, { recursive: true });
  token = process.env.DISCORD_BOT_TOKEN?.trim() ?? '';
  if (!token) {
    if (!process.stdin.isTTY) {
      console.error('Run this in a terminal, so it can ask for the token, or set DISCORD_BOT_TOKEN.');
      process.exit(1);
    }
    token = await askHidden("The bot's token (Developer Portal, Bot, Reset Token; it isn't shown or saved): ");
  }
  if (!token) {
    console.error('No token.');
    process.exit(1);
  }

  const app = await api('GET', '/applications/@me');
  if (!app.ok) {
    console.error(app.status === 401 ? 'Discord refused the token.' : `Couldn't read the app: ${describe(app)}`);
    process.exit(1);
  }
  const bot = await api('GET', '/users/@me');
  if (!bot.ok) process.exit(1);
  appId = app.json.id;
  botId = bot.json.id;
  ownerId = app.json.team ? app.json.team.owner_user_id : app.json.owner?.id;
  state = loadState();
  if (state.appId !== appId) state = { appId, findings: {} };
  saveState();
  fs.appendFileSync(logPath, `\n[${time()}] ---- started ${new Date().toISOString()}\n`);
  log(`The app is ${app.json.name}; it answers its owner, ${app.json.team ? 'the team owner' : app.json.owner?.username}, only.`);

  const installs = Object.keys(app.json.integration_types_config ?? {});
  found('userInstall', installs.includes(USER_INSTALL),
    installs.includes(USER_INSTALL) ? '' : 'Developer Portal, Installation: tick User Install', true);
  if (installs.includes(GUILD_INSTALL)) {
    log("Guild Install is on too. Untick it under Installation, so the app can't be added to a server by mistake.");
  }
  if (app.json.interactions_endpoint_url) {
    log('The app has an Interactions Endpoint URL, so commands and buttons go there instead of to this script. Clear it under General Information.');
  }

  // A command for installs to an account, in the bot's DM and anywhere else the user is.
  const commands = await api('PUT', `/applications/${appId}/commands`, [
    {
      name: 'pair',
      type: 1,
      description: 'Pair with the Deventry spike (it answers its owner only)',
      integration_types: [1],
      contexts: [0, 1, 2],
    },
  ]);
  if (commands.ok) {
    const pair = commands.json.find((c) => c.name === 'pair');
    log(`Registered /pair (installs ${JSON.stringify(pair?.integration_types)}, contexts ${JSON.stringify(pair?.contexts)}).`);
  }

  gateway = new Gateway(dispatch);
  await gateway.start();
  links();
  console.log(`Type a command; \`help\` lists them. The findings so far are kept between runs.\n`);

  rl = readline.createInterface({ input: process.stdin, output: process.stdout, terminal: Boolean(process.stdin.isTTY) });
  rl.on('line', (line) => command(line).catch((e) => log(`That failed: ${e.stack ?? e}`)));
  rl.on('SIGINT', quit);
  rl.on('close', quit);
  process.on('SIGINT', quit);
}

main().catch((e) => {
  console.error(redact(e.stack ?? e));
  process.exit(1);
});
