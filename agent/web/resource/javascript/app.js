/* ============================================================
   Ember · 情感陪伴伙伴  —  前端交互逻辑
   依赖后端 REST API（{code, info} 信封格式，code=0 成功）
   ============================================================ */
"use strict";

/* ---------------- DOM 引用 ---------------- */
const $ = (id) => document.getElementById(id);

const els = {
  connStatus: $("connStatus"),
  connText: $("connText"),
  modelChip: $("modelChip"),
  // 语音朗读
  ttsToggleBtn: $("ttsToggleBtn"),
  // 主题
  themeBtn: $("themeBtn"),
  themeModal: $("themeModal"),
  themeGrid: $("themeGrid"),
  themeCloseBtn: $("themeCloseBtn"),
  // 侧栏
  sidebar: $("sidebar"),
  sidebarMask: $("sidebarMask"),
  hamburgerBtn: $("hamburgerBtn"),
  personaName: $("personaName"),
  personaBadge: $("personaBadge"),
  personaDesc: $("personaDesc"),
  personaUpdated: $("personaUpdated"),
  editPersonaBtn: $("editPersonaBtn"),
  resetPersonaBtn: $("resetPersonaBtn"),
  personaAvatarBtn: $("personaAvatarBtn"),
  personaAvatarImg: $("personaAvatarImg"),
  profileCard: $("profileCard"),
  profileBody: $("profileBody"),
  profileUpdatedBadge: $("profileUpdatedBadge"),
  refreshProfileBtn: $("refreshProfileBtn"),
  diaryCard: $("diaryCard"),
  diaryBody: $("diaryBody"),
  diaryDateBadge: $("diaryDateBadge"),
  openDiaryBtn: $("openDiaryBtn"),
  writeDiaryBtn: $("writeDiaryBtn"),
  stBackend: $("stBackend"),
  stTurns: $("stTurns"),
  stAutosave: $("stAutosave"),
  stTokenText: $("stTokenText"),
  stTokenFill: $("stTokenFill"),
  saveBtn: $("saveBtn"),
  shutdownBtn: $("shutdownBtn"),
  // 聊天区
  messages: $("messages"),
  welcomeHero: $("welcomeHero"),
  welcomeAvatar: $("welcomeAvatar"),
  inputBox: $("inputBox"),
  sendBtn: $("sendBtn"),
  // 弹层
  avatarModal: $("avatarModal"),
  avatarGrid: $("avatarGrid"),
  avatarCloseBtn: $("avatarCloseBtn"),
  personaModal: $("personaModal"),
  personaInput: $("personaInput"),
  personaSaveBtn: $("personaSaveBtn"),
  personaCancelBtn: $("personaCancelBtn"),
  diaryModal: $("diaryModal"),
  diaryListPane: $("diaryListPane"),
  diaryReader: $("diaryReader"),
  diaryCloseBtn: $("diaryCloseBtn"),
  diaryRegenBtn: $("diaryRegenBtn"),
  shutdownModal: $("shutdownModal"),
  shutdownTokenInput: $("shutdownTokenInput"),
  shutdownConfirmBtn: $("shutdownConfirmBtn"),
  shutdownCancelBtn: $("shutdownCancelBtn"),
  farewellScreen: $("farewellScreen"),
  toast: $("toast"),
  // 密码锁
  lockScreen: $("lockScreen"),
  lockInput: $("lockInput"),
  lockSubmit: $("lockSubmit"),
  lockError: $("lockError"),
};

/* ---------------- 全局状态 ---------------- */
const state = {
  sending: false, // 对话请求进行中
  polling: true, // 状态轮询开关（服务关闭后停止）
  autoScroll: true, // 消息区是否跟随最新消息
  remoteShutdownEnabled: false,
  personaDescExpanded: false,
  // 实时流
  liveTimer: null, // /api/chat/live 轮询定时器
  // 头像
  avatarPrefix: "/resource/images/avatars/",
  avatarList: [], // 可用头像文件名（服务端扫描）
  avatarFile: localStorage.getItem("ember-avatar") || "", // 用户选择（'' = 默认）
  // 语音朗读（默认开启，localStorage: ember-tts）
  ttsEnabled: localStorage.getItem("ember-tts") !== "off",
  ttsAudio: null,        // 当前播放的 Audio 实例
  ttsUrls: [],           // 已创建待释放的 objectURL 列表
  ttsAborted: false,     // 打断标记（新对话/关闭开关时置位）
  ttsFailedNotified: false, // 本轮是否已提示 TTS 不可用（最多一次）
};

/* ============================================================
   主题管理（localStorage: ember-theme）
   ============================================================ */
const THEMES = ["coral", "mint", "sky", "sakura", "lemon"];
const THEME_NAMES = {
  coral: "珊瑚橙",
  mint: "薄荷绿",
  sky: "天空蓝",
  sakura: "樱花粉",
  lemon: "柠檬黄",
};

function applyTheme(name) {
  if (!THEMES.includes(name)) name = "coral";
  document.body.dataset.theme = name;
  localStorage.setItem("ember-theme", name);

  // 更新弹层选中态
  els.themeGrid.querySelectorAll(".theme-cell").forEach((cell) => {
    cell.classList.toggle("active", cell.dataset.theme === name);
  });
}

function initTheme() {
  const saved = localStorage.getItem("ember-theme");
  applyTheme(saved || "coral");
}

/* ============================================================
   语音朗读（TTS）
   ============================================================ */

/** 将文本按换行分割、清洗为可朗读的片段列表（跳过空段，去掉 *动作* 标记）。 */
function splitTtsSegments(text) {
  return String(text)
    .split(/\r\n|\r|\n/)
    .map((line) => line
      .replace(/\*([^*]+)\*/g, "$1") // 去掉 markdown 星号动作标记
      .trim())
    .filter((line) => line.length > 0);
}

/** 释放全部已创建、尚未回收的 objectURL，防止内存/句柄泄漏。 */
function revokeTtsUrls() {
  state.ttsUrls.forEach((u) => {
    try { URL.revokeObjectURL(u); } catch (e) {}
  });
  state.ttsUrls = [];
}

/** 打断当前朗读：暂停音频 + 清空队列 + 释放 URL。新对话/重播/关闭开关时调用。 */
function stopCurrentTts() {
  state.ttsAborted = true;
  if (state.ttsAudio) {
    try { state.ttsAudio.pause(); } catch (e) {}
    state.ttsAudio = null;
  }
  revokeTtsUrls();
  // 移除挂在点上的状态条
  document.querySelectorAll(".tts-status").forEach((n) => n.remove());
  document.querySelectorAll(".tts-replay.playing").forEach((n) => n.classList.remove("playing"));
}

/** 更新顶栏开关视觉与记忆。 */
function applyTtsEnabled() {
  els.ttsToggleBtn.classList.toggle("tts-off", !state.ttsEnabled);
  els.ttsToggleBtn.title = state.ttsEnabled ? "语音朗读：开（点击关闭）" : "语音朗读：关（点击开启）";
}

/**
 * 在消息气泡 body 下挂“重播语音”按钮，点击对整条文本重新分片朗读。
 * 历史消息与新回复共用，保证行为一致；服务端命中缓存即秒回。
 * @param {HTMLElement} body 消息气泡容器（appendMessage 返回的 body）
 * @param {string} text 完整回复文本
 */
function attachReplayBtn(body, text) {
  if (!body) return;
  const btn = document.createElement("button");
  btn.className = "tts-replay";
  btn.innerHTML = "🔊 重播语音";
  btn.addEventListener("click", () => playReplyTts(text, btn));
  body.appendChild(btn);
  return btn;
}

/** 逐段合成并顺序播放：段1 合成→播放→ended 后续段。单段失败跳过续播。 */
function playReplyTts(text, replayBtn) {
  if (!state.ttsEnabled) return;

  // 先打断上一条（若有），并准备本轮状态
  stopCurrentTts();
  state.ttsAborted = false;
  state.ttsFailedNotified = false;

  const segments = splitTtsSegments(text);
  if (segments.length === 0) return;

  if (replayBtn) replayBtn.classList.add("playing");

  const statusEl = document.createElement("div");
  statusEl.className = "tts-status synth";
  statusEl.innerHTML = `<span class="tts-wave" style="display:none"><i></i><i></i><i></i></span><span class="tts-text">合成中…</span>`;
  if (replayBtn && replayBtn.parentNode) {
    replayBtn.parentNode.insertBefore(statusEl, replayBtn.nextSibling);
  } else {
    // 兜底：追加到消息区
    els.messages.appendChild(statusEl);
  }

  let index = 0;

  function finish() {
    try { statusEl.remove(); } catch (e) {}
    if (replayBtn) replayBtn.classList.remove("playing");
    revokeTtsUrls();
  }

  function playNext() {
    if (state.ttsAborted) { finish(); return; }
    if (index >= segments.length) { finish(); return; }

    const seg = segments[index];
    const total = segments.length;
    index += 1;

    statusEl.className = "tts-status synth";
    statusEl.querySelector(".tts-text").textContent = `合成中（第 ${index}/${total} 段）`;
    statusEl.querySelector(".tts-wave").style.display = "none";

    const url = `/api/tts?text=${encodeURIComponent(seg)}`;
    fetch(url)
      .then((r) => {
        if (!r.ok) throw new Error("tts_http_" + r.status);
        return r.blob();
      })
      .then((blob) => {
        if (state.ttsAborted) { finish(); return; }
        const objUrl = URL.createObjectURL(blob);
        state.ttsUrls.push(objUrl);

        const audio = new Audio(objUrl);
        state.ttsAudio = audio;

        audio.onended = () => { try { URL.revokeObjectURL(objUrl); } catch (e) {} playNext(); };
        audio.onerror = () => { playNext(); }; // 播放失败：跳过续播

        // 切换为“播放中”状态
        statusEl.className = "tts-status playing";
        statusEl.querySelector(".tts-text").textContent = `播放中（第 ${index}/${total} 段）`;
        statusEl.querySelector(".tts-wave").style.display = "inline-flex";

        audio.play().catch(() => playNext());
      })
      .catch(() => {
        // 单段失败：跳过并续播；全部失败仅提示一次
        if (!state.ttsFailedNotified) {
          state.ttsFailedNotified = true;
          toast("语音合成暂时不可用，已静默跳过", true);
        }
        playNext();
      });
  }

  playNext();
}

/** 绑定顶栏语音开关。 */
function initTts() {
  applyTtsEnabled();
  els.ttsToggleBtn.addEventListener("click", () => {
    state.ttsEnabled = !state.ttsEnabled;
    localStorage.setItem("ember-tts", state.ttsEnabled ? "on" : "off");
    applyTtsEnabled();
    if (!state.ttsEnabled) {
      stopCurrentTts();
      toast("已关闭语音朗读");
    } else {
      toast("已开启语音朗读");
    }
  });
}

/** 给欢迎横幅头像容器补齐 img+emoji 结构（HTML 中初始只有 emoji 文本） */
function ensureWelcomeAvatarStructure() {
  const el = els.welcomeAvatar;
  if (!el || el.querySelector("img")) return;

  const text = el.textContent.trim();
  el.textContent = "";
  const emoji = document.createElement("span");
  emoji.className = "avatar-emoji";
  emoji.textContent = text || "🔥";
  const img = document.createElement("img");
  img.alt = "Ember 头像";
  el.appendChild(emoji);
  el.appendChild(img);
}

/* ============================================================
   头像管理（localStorage: ember-avatar；三级回退链）
   ============================================================ */

/** 当前应显示的头像 URL（优先用户选择，缺省默认图） */
function avatarUrl() {
  const file = state.avatarFile || "ember_default.jpg";
  return state.avatarPrefix + encodeURIComponent(file);
}

/**
 * 为一个头像容器元素应用当前头像。
 * 结构要求：容器内含 <img>（z-index 2）与 .avatar-emoji（z-index 1）；
 * onerror 回退链：所选图 → ember_default.jpg → emoji。
 */
function applyAvatarTo(container) {
  const img = container.querySelector("img");
  if (!img) return;

  const file = state.avatarFile || "ember_default.jpg";
  const src = state.avatarPrefix + encodeURIComponent(file);

  container.classList.remove("avatar-fallback");
  img.style.display = "";
  img.onerror = () => {
    if (!file.toLowerCase().startsWith("ember_default")) {
      // 所选图缺失 → 回退默认图
      container.classList.remove("avatar-fallback");
      img.style.display = "";
      img.onerror = () => {
        // 默认图也缺失 → emoji
        img.style.display = "none";
        container.classList.add("avatar-fallback");
      };
      img.src = state.avatarPrefix + "ember_default.jpg";
    } else {
      img.style.display = "none";
      container.classList.add("avatar-fallback");
    }
  };
  img.src = src;

  $("logoBadge").src = src;
}

/** 刷新页面全部 Ember 头像位 */
function refreshAllAvatars() {
  [els.personaAvatarBtn, els.welcomeAvatar].forEach(
    (el) => el && applyAvatarTo(el),
  );
  // 消息流中的头像（历史与新增）同步刷新
  els.messages
    .querySelectorAll(
      ".msg.ember .msg-avatar, .live-panel .msg-avatar, .typing .typing-avatar",
    )
    .forEach((el) => applyAvatarTo(el));
}

/** 创建带图片+回退链的头像元素（供消息/实时面板使用） */
function createAvatarEl(className) {
  const wrap = document.createElement("div");
  wrap.className = className;
  const emoji = document.createElement("span");
  emoji.className = "avatar-emoji";
  emoji.textContent = "🔥";
  const img = document.createElement("img");
  img.alt = "Ember 头像";
  wrap.appendChild(emoji);
  wrap.appendChild(img);
  applyAvatarTo(wrap);
  return wrap;
}

/** 加载服务端头像列表并渲染选择网格 */
async function loadAvatars() {
  try {
    const r = await getJSON("/api/avatars");
    state.avatarPrefix = r.urlPrefix || state.avatarPrefix;
    state.avatarList = Array.isArray(r.avatars) ? r.avatars : [];
  } catch {
    state.avatarList = [];
  }
  renderAvatarGrid();

  // 校验本地偏好是否仍存在于列表（不存在则清空回默认）
  if (
    state.avatarFile &&
    state.avatarList.length &&
    !state.avatarList.some(
      (f) => f.toLowerCase() === state.avatarFile.toLowerCase(),
    )
  ) {
    state.avatarFile = "";
    localStorage.removeItem("ember-avatar");
  }
  refreshAllAvatars();
}

function renderAvatarGrid() {
  els.avatarGrid.innerHTML = "";

  if (state.avatarList.length === 0) {
    els.avatarGrid.innerHTML =
      '<p class="profile-empty">头像库还是空的（服务器未扫描到头像图片）<br>可将图片放入 agent\\web\\resource\\images\\avatars 目录</p>';
    return;
  }

  state.avatarList.forEach((file) => {
    const cell = document.createElement("button");
    cell.className =
      "avatar-cell" +
      (file.toLowerCase() ===
      (state.avatarFile || "ember_default.jpg").toLowerCase()
        ? " active"
        : "");
    cell.title = file;

    const img = document.createElement("img");
    img.src = state.avatarPrefix + encodeURIComponent(file);
    img.alt = file;
    img.loading = "lazy";
    img.onerror = () => {
      cell.style.opacity = ".35";
    };

    const name = document.createElement("span");
    name.className = "avatar-name";
    name.textContent = file.replace(/\.[^.]+$/, "");

    cell.appendChild(img);
    cell.appendChild(name);
    cell.addEventListener("click", () => selectAvatar(file));
    els.avatarGrid.appendChild(cell);
  });
}

function selectAvatar(file) {
  state.avatarFile = file.toLowerCase() === "ember_default.jpg" ? "" : file;
  if (state.avatarFile) {
    localStorage.setItem("ember-avatar", state.avatarFile);
  } else {
    localStorage.removeItem("ember-avatar");
  }
  renderAvatarGrid();
  refreshAllAvatars();
  toast(`头像已更换 💫`);
}

/* ============================================================
   API 客户端
   ============================================================ */
async function api(path, options = {}) {
  // 携带会话令牌（密码锁解锁后由后端返回），后端统一鉴权
  const headers = Object.assign(
    { "Content-Type": "application/json" },
    options.headers || {},
  );
  if (sessionToken) headers["X-Access-Token"] = sessionToken;

  const resp = await fetch(path, { headers, ...options });

  // 401：会话失效（令牌过期 / 未解锁），退回锁屏
  if (resp.status === 401 && els.lockScreen && !els.lockScreen.classList.contains("active")) {
    showLockScreen("会话已失效，请重新输入密码");
  }

  if (!resp.ok) {
    throw new Error(`HTTP ${resp.status}`);
  }
  const data = await resp.json();
  if (data.code !== 0) {
    throw new Error(
      data.info && data.info.errorMessage
        ? data.info.errorMessage
        : typeof data.info === "string"
          ? data.info
          : "请求失败",
    );
  }
  return data.info;
}

const getJSON = (path) => api(path);
const postJSON = (path, body) =>
  api(path, { method: "POST", body: JSON.stringify(body || {}) });

/* ============================================================
   密码锁（Web 前端安全）：仅在后台 enable_password=true 时启用
   - 启动先查询 /api/info 是否启用密码
   - 启用时显示全屏锁屏，用户输入密码 POST /api/unlock 换取会话令牌
   - 之后所有 api() 请求自动携带 X-Access-Token（见 api()）
   - 提示：纯 HTTP 下令牌可被嗅探，公网部署建议前置 HTTPS 反向代理
   ============================================================ */
let sessionToken = ""; // 解锁成功后由后端返回的会话令牌；空=未解锁
let lockResolver = null; // ensureUnlocked 的 Promise resolve，解锁成功后调用
let passwordEnabled = false;

// 查询后端是否启用密码锁；未启用直接放行，启用则显示锁屏并等待解锁
async function ensureUnlocked() {
  let info;
  try {
    info = await getJSON("/api/info");
  } catch (e) {
    // 无法获取 info（网络/服务异常）时保守放行，避免锁死整个界面
    console.warn("[lock] 获取 /api/info 失败，按未启用密码锁处理：", e);
    return;
  }
  passwordEnabled = !!(info && info.passwordEnabled);
  if (!passwordEnabled) {
    hideLockScreen();
    return;
  }
  // 启用：显示锁屏并阻塞初始化，直到用户成功解锁
  showLockScreen();
  await new Promise((resolve) => {
    lockResolver = resolve;
  });
}

function showLockScreen(msg) {
  if (els.lockScreen.classList.contains("lock-hide")) {
    els.lockScreen.classList.remove("lock-hide");
  }
  els.lockScreen.classList.add("active");
  if (msg) {
    els.lockError.textContent = msg;
    els.lockError.classList.add("show");
  }
  els.lockInput.value = "";
  els.lockInput.focus();
}

// 解锁成功：保存令牌，淡出锁屏，放行被阻塞的初始化
function unlockSucceeded(token) {
  sessionToken = token || "";
  hideLockScreen();
  if (lockResolver) {
    const r = lockResolver;
    lockResolver = null;
    r();
  }
}

function hideLockScreen() {
  els.lockScreen.classList.remove("active");
  els.lockScreen.classList.add("lock-hide");
  // 淡出结束后彻底移除激活态，避免遮挡点击
  setTimeout(() => {
    if (!els.lockScreen.classList.contains("active")) {
      els.lockScreen.classList.remove("lock-hide");
    }
  }, 480);
}

// 用户提交密码：POST /api/unlock 校验，成功则解锁，失败提示错误
async function submitUnlock() {
  const pwd = els.lockInput.value || "";
  if (!pwd) {
    showLockError("请输入密码");
    return;
  }
  els.lockSubmit.disabled = true;
  try {
    const res = await fetch("/api/unlock", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: "password=" + encodeURIComponent(pwd),
    });
    if (res.status === 401) {
      showLockError("密码不正确，请重试");
      shakeLockInput();
      return;
    }
    if (!res.ok) {
      showLockError("解锁失败，请稍后重试");
      return;
    }
    const data = await res.json();
    if (data.code !== 0 || !data.info || !data.info.token) {
      showLockError("解锁失败，请稍后重试");
      return;
    }
    unlockSucceeded(data.info.token);
  } catch (e) {
    showLockError("网络异常，无法解锁");
  } finally {
    els.lockSubmit.disabled = false;
  }
}

function showLockError(msg) {
  els.lockError.textContent = msg;
  els.lockError.classList.add("show");
}
function shakeLockInput() {
  els.lockInput.classList.remove("shake");
  void els.lockInput.offsetWidth; // 重启动画
  els.lockInput.classList.add("shake");
}

/* ============================================================
   工具函数
   ============================================================ */
function toast(msg, isError = false) {
  els.toast.textContent = msg;
  els.toast.classList.toggle("toast-error", isError);
  els.toast.classList.add("show");
  clearTimeout(toast._timer);
  toast._timer = setTimeout(() => els.toast.classList.remove("show"), 2400);
}

function fmtTokens(n) {
  if (n >= 1000000) return (n / 1000000).toFixed(1) + "M";
  if (n >= 1000) return (n / 1000).toFixed(1) + "K";
  return String(n);
}

function setOnline(online) {
  els.connStatus.classList.toggle("online", online);
  els.connText.textContent = online ? "在线" : "服务不可达";
}

function scrollToBottom(force = false) {
  if (!state.autoScroll && !force) return;
  requestAnimationFrame(() => {
    els.messages.scrollTop = els.messages.scrollHeight;
  });
}

function escapeHtml(s) {
  return String(s).replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
}

function todayStr() {
  const d = new Date();
  const p = (x) => String(x).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/* ============================================================
   消息渲染
   ============================================================ */
function hideWelcome() {
  if (els.welcomeHero) {
    els.welcomeHero.remove();
    els.welcomeHero = null;
  }
}

/** 创建一条消息 DOM；kind = 'user' | 'ember' */
function appendMessage(kind, text, { animate = true } = {}) {
  hideWelcome();

  const wrap = document.createElement("div");
  wrap.className = `msg ${kind}`;

  const avatar =
    kind === "user"
      ? Object.assign(document.createElement("div"), {
          className: "msg-avatar",
          textContent: "🧑",
        })
      : createAvatarEl("msg-avatar");

  const body = document.createElement("div");
  body.className = "msg-body";

  const sender = document.createElement("div");
  sender.className = "msg-sender";
  sender.textContent = kind === "user" ? "你" : "Ember";

  const bubble = document.createElement("div");
  bubble.className = "bubble";
  bubble.textContent = text;

  body.appendChild(sender);
  body.appendChild(bubble);
  wrap.appendChild(avatar);
  wrap.appendChild(body);
  els.messages.appendChild(wrap);

  if (!animate) wrap.style.animation = "none";
  scrollToBottom();
  return { wrap, bubble, body };
}

/** 添加思考过程折叠区（对话完成后） */
function appendThinkBlock(body, thinkText) {
  if (!thinkText || !thinkText.trim()) return;
  const details = document.createElement("details");
  details.className = "think-box";
  const summary = document.createElement("summary");
  summary.textContent = "思考过程";
  const content = document.createElement("div");
  content.className = "think-content";
  content.textContent = thinkText.trim();
  details.appendChild(summary);
  details.appendChild(content);
  body.appendChild(details);
}

/** 打字机逐字呈现（无实时数据时的兜底；总时长上限约 4 秒） */
function typewrite(bubble, fullText) {
  return new Promise((resolve) => {
    const len = fullText.length;
    if (len === 0) {
      resolve();
      return;
    }

    const caret = document.createElement("span");
    caret.className = "caret";
    bubble.appendChild(caret);

    const perStep = Math.max(1, Math.ceil(len / (4000 / 22)));
    let i = 0;

    const timer = setInterval(() => {
      i = Math.min(len, i + perStep);
      bubble.textContent = fullText.slice(0, i);
      bubble.appendChild(caret);
      scrollToBottom();

      if (i >= len) {
        clearInterval(timer);
        caret.remove();
        resolve();
      }
    }, 22);
  });
}

/* ============================================================
   实时思考/回复面板（轮询 /api/chat/live）
   ============================================================ */

/** 创建对话进行中的实时面板（思考框 + 实时回复气泡） */
function createLivePanel() {
  hideWelcome();

  const panel = document.createElement("div");
  panel.className = "live-panel";

  const avatar = createAvatarEl("msg-avatar");

  const body = document.createElement("div");
  body.className = "live-body";

  // 思考框
  const thinkBox = document.createElement("div");
  thinkBox.className = "thinking-box";
  const head = document.createElement("div");
  head.className = "thinking-head";
  head.innerHTML =
    '<span class="thinking-dots"><i></i><i></i><i></i></span><span class="thinking-label">正在思考…</span>';
  const thinkText = document.createElement("div");
  thinkText.className = "thinking-text";
  thinkBox.appendChild(head);
  thinkBox.appendChild(thinkText);

  // 实时回复气泡（首个 output token 出现时显示）
  const reply = document.createElement("div");
  reply.className = "live-reply hidden";

  body.appendChild(thinkBox);
  body.appendChild(reply);
  panel.appendChild(avatar);
  panel.appendChild(body);
  els.messages.appendChild(panel);
  scrollToBottom(true);

  return {
    panel,
    thinkBox,
    thinkText,
    reply,
    label: head.querySelector(".thinking-label"),
    shownOutputLen: -1, // -1 = 尚未收到任何 output
  };
}

/** 开始 300ms 轮询实时流并渲染 */
function startLivePolling(ui) {
  stopLivePolling();

  const tick = async () => {
    try {
      const s = await getJSON("/api/chat/live");
      if (!s || !s.active) return;

      // 思考文本实时滚动
      if (s.think) {
        ui.thinkText.textContent = s.think;
        ui.thinkText.scrollTop = ui.thinkText.scrollHeight;
      }

      // 首个 output token：切换到"正在回复"
      if (s.output && ui.shownOutputLen < 0) {
        ui.shownOutputLen = 0;
        ui.reply.classList.remove("hidden");
        if (ui.label) ui.label.textContent = "想好了，正在回复…";
        // 收起思考框正文（保留可滚动区域，降低视觉噪音）
        ui.thinkBox.style.opacity = ".62";
      }

      if (s.output) {
        ui.reply.textContent = s.output;
        scrollToBottom();
      }
    } catch {
      /* 轮询失败静默，下一帧重试 */
    }
  };

  state.liveTimer = setInterval(tick, 300);
  tick(); // 立即执行一次
}

function stopLivePolling() {
  if (state.liveTimer) {
    clearInterval(state.liveTimer);
    state.liveTimer = null;
  }
}

/* ============================================================
   状态轮询
   ============================================================ */
async function pollStatus() {
  while (state.polling) {
    try {
      const s = await getJSON("/api/status");
      setOnline(true);
      renderStatus(s);
    } catch {
      setOnline(false);
    }
    await new Promise((r) => setTimeout(r, 2000));
  }
}

function renderStatus(s) {
  els.modelChip.textContent = s.model || "…";
  els.stBackend.textContent = s.model ? s.model : "…";
  els.stBackend.title = s.backend || "";
  els.stTurns.textContent = `${s.turns} 轮`;
  els.stAutosave.textContent = s.autosave ? "每轮自动保存" : "退出时保存";

  const pct =
    s.maxTokens > 0
      ? Math.min(100, Math.round((s.tokens / s.maxTokens) * 100))
      : 0;
  els.stTokenText.textContent = `${fmtTokens(s.tokens)} / ${fmtTokens(s.maxTokens)}`;
  els.stTokenFill.style.width = `${pct}%`;

  if (s.remoteShutdownEnabled !== state.remoteShutdownEnabled) {
    state.remoteShutdownEnabled = s.remoteShutdownEnabled;
    els.shutdownBtn.classList.toggle("hidden", !s.remoteShutdownEnabled);
  }
}

/* ============================================================
   人设
   ============================================================ */
async function loadPersona() {
  try {
    const p = await getJSON("/api/persona");
    els.personaName.textContent = p.name || "Ember";
    els.personaBadge.textContent = p.isDefault ? "默认人设" : "自定义人设";
    els.personaDesc.textContent = p.description || "（暂无人设描述）";
    els.personaDesc.classList.remove("expanded");
    state.personaDescExpanded = false;
    els.personaUpdated.textContent = p.updatedAt ? `更新于 ${p.updatedAt}` : "";
  } catch (e) {
    els.personaDesc.textContent = `人设加载失败：${e.message}`;
  }
}

async function savePersona() {
  const text = els.personaInput.value.trim();
  if (!text) {
    toast("人设描述不能为空", true);
    return;
  }

  els.personaSaveBtn.disabled = true;
  try {
    await postJSON("/api/persona", { description: text });
    closePersonaModal();
    await loadPersona();
    toast("人设已更新 ✨");
  } catch (e) {
    toast(`保存失败：${e.message}`, true);
  } finally {
    els.personaSaveBtn.disabled = false;
  }
}

async function resetPersona() {
  if (!confirm("确定恢复内置默认人设吗？当前自定义人设将被覆盖。")) return;
  try {
    await postJSON("/api/persona/reset");
    await loadPersona();
    toast("已恢复默认人设 ↺");
  } catch (e) {
    toast(`操作失败：${e.message}`, true);
  }
}

/* ============================================================
   画像
   ============================================================ */
async function loadProfile() {
  try {
    const p = await getJSON("/api/profile");
    renderProfile(p);
  } catch (e) {
    els.profileBody.innerHTML = `<p class="profile-empty">画像加载失败：${escapeHtml(e.message)}</p>`;
  }
}

function renderProfile(p) {
  els.profileUpdatedBadge.textContent = p.updatedAt
    ? `更新于 ${p.updatedAt}`
    : "";

  if (p.isEmpty) {
    els.profileBody.innerHTML =
      '<p class="profile-empty">💬 多聊几轮之后，我会慢慢更懂你…</p>';
    return;
  }

  let html = "";
  if (p.summary)
    html += `<p class="profile-summary">${escapeHtml(p.summary)}</p>`;

  if (Array.isArray(p.traits) && p.traits.length) {
    html +=
      '<div class="tag-group"><div class="tag-group-label">性格特征</div><div class="tags">' +
      p.traits
        .map(
          (t, i) =>
            `<span class="tag tag-trait" style="animation-delay:${i * 50}ms">${escapeHtml(t)}</span>`,
        )
        .join("") +
      "</div></div>";
  }
  if (Array.isArray(p.interests) && p.interests.length) {
    html +=
      '<div class="tag-group"><div class="tag-group-label">兴趣话题</div><div class="tags">' +
      p.interests
        .map(
          (t, i) =>
            `<span class="tag tag-interest" style="animation-delay:${i * 50}ms">${escapeHtml(t)}</span>`,
        )
        .join("") +
      "</div></div>";
  }
  if (p.emotionalState)
    html += `<p class="profile-line"><span class="emoji-dot">🌤️</span>近期情绪：<b>${escapeHtml(p.emotionalState)}</b></p>`;
  if (p.communicationStyle)
    html += `<p class="profile-line"><span class="emoji-dot">💬</span>沟通偏好：<b>${escapeHtml(p.communicationStyle)}</b></p>`;

  els.profileBody.innerHTML =
    html || '<p class="profile-empty">💬 多聊几轮之后，我会慢慢更懂你…</p>';
}

async function refreshProfile() {
  els.refreshProfileBtn.disabled = true;
  els.refreshProfileBtn.textContent = "✨ 总结中…";
  try {
    const r = await postJSON("/api/profile/refresh");
    if (r.updated) {
      await loadProfile();
      els.profileCard.classList.remove("bounce");
      void els.profileCard.offsetWidth;
      els.profileCard.classList.add("bounce");
      toast("画像已更新，我会更懂你一点 ✨");
    } else {
      toast("暂时没有足够的对话内容来总结画像");
    }
  } catch (e) {
    toast(`总结失败：${e.message}`, true);
  } finally {
    els.refreshProfileBtn.disabled = false;
    els.refreshProfileBtn.textContent = "✨ 重新总结";
  }
}

/* ============================================================
   日记
   ============================================================ */
async function loadDiaryCard() {
  try {
    const d = await getJSON("/api/diary");
    els.diaryDateBadge.textContent = todayStr();
    if (d.exists) {
      els.diaryBody.innerHTML =
        `<p class="diary-title">《${escapeHtml(d.title)}》</p>` +
        `<p class="diary-excerpt">${escapeHtml(d.content)}</p>`;
    } else {
      els.diaryBody.innerHTML =
        '<p class="profile-empty">✍️ 今天的日记还没有写，聊过几句后我会自动动笔～</p>';
    }
  } catch {
    els.diaryBody.innerHTML = '<p class="profile-empty">📔 日记加载失败</p>';
  }
}

async function openDiaryModal() {
  els.diaryModal.classList.remove("hidden");
  await loadDiaryList();
}

async function loadDiaryList(selectedDate) {
  els.diaryListPane.innerHTML = '<p class="profile-empty">加载中…</p>';
  try {
    const r = await getJSON("/api/diary/list");
    const diaries = r.diaries || [];

    if (diaries.length === 0) {
      els.diaryListPane.innerHTML =
        '<p class="profile-empty">还没有日记，<br>先和我聊几句吧～</p>';
      els.diaryReader.innerHTML =
        '<p class="profile-empty" style="align-self:center;text-align:center;">还没有可阅读的日记</p>';
      return;
    }

    els.diaryListPane.innerHTML = "";
    diaries.forEach((d) => {
      const item = document.createElement("button");
      item.className =
        "diary-item" +
        (d.date === (selectedDate || todayStr()) ? " active" : "");
      item.innerHTML =
        `<div class="diary-item-date">📅 ${escapeHtml(d.date)}</div>` +
        `<div class="diary-item-title">${escapeHtml(d.title || "（无标题）")}</div>`;
      item.addEventListener("click", () => loadDiaryContent(d.date));
      els.diaryListPane.appendChild(item);
    });

    // 默认选中（指定日期或最新一篇）
    const target =
      selectedDate && diaries.some((d) => d.date === selectedDate)
        ? selectedDate
        : diaries.some((d) => d.date === todayStr())
          ? todayStr()
          : diaries[0].date;
    await loadDiaryContent(target);
  } catch (e) {
    els.diaryListPane.innerHTML = `<p class="profile-empty">列表加载失败：${escapeHtml(e.message)}</p>`;
  }
}

async function loadDiaryContent(date) {
  // 更新列表选中态
  els.diaryListPane.querySelectorAll(".diary-item").forEach((it) => {
    it.classList.toggle(
      "active",
      it.querySelector(".diary-item-date").textContent.includes(date),
    );
  });

  els.diaryReader.innerHTML = '<p class="profile-empty">翻开的这一页…</p>';
  try {
    const d = await getJSON(`/api/diary?date=${encodeURIComponent(date)}`);
    if (d.exists) {
      els.diaryReader.innerHTML =
        `<h3>《${escapeHtml(d.title)}》</h3>` +
        `<div class="diary-meta">📅 ${escapeHtml(d.date)} · 写于 ${escapeHtml(d.generatedAt)}</div>` +
        `<div class="diary-content">${escapeHtml(d.content)}</div>`;
      els.diaryReader.scrollTop = 0;
    } else {
      els.diaryReader.innerHTML = `<p class="profile-empty">${escapeHtml(date)} 这一天没有日记</p>`;
    }
  } catch (e) {
    els.diaryReader.innerHTML = `<p class="profile-empty">加载失败：${escapeHtml(e.message)}</p>`;
  }
}

async function generateDiary(regen = false) {
  if (regen && !confirm("用今天以来的对话重新写一篇今日日记？")) return;

  els.writeDiaryBtn.disabled = true;
  els.writeDiaryBtn.textContent = "✍️ 写日记中…";
  try {
    const r = await postJSON("/api/diary/generate");
    if (r.ok) {
      toast("今日日记已写好 📔");
      await loadDiaryCard();
      els.diaryCard.classList.remove("bounce");
      void els.diaryCard.offsetWidth;
      els.diaryCard.classList.add("bounce");
      // 若日记弹层开着则刷新列表并定位今日
      if (!els.diaryModal.classList.contains("hidden")) {
        await loadDiaryList(r.date);
      }
    } else {
      toast("今天还没有可写进日记的对话内容");
    }
  } catch (e) {
    toast(`日记生成失败：${e.message}`, true);
  } finally {
    els.writeDiaryBtn.disabled = false;
    els.writeDiaryBtn.textContent = "✍️ 写日记";
  }
}

/* ============================================================
   历史加载
   ============================================================ */
async function loadHistory() {
  try {
    const h = await getJSON("/api/history?limit=50");
    if (h.messages && h.messages.length) {
      h.messages.forEach((m) => {
        if (m.role === "user" || m.role === "assistant") {
          const { body } = appendMessage(m.role === "user" ? "user" : "ember", m.content, {
            animate: false,
          });
          // 历史 Ember 回复挂重播按钮：刷新后仍可重听（服务端命中缓存秒回）
          if (m.role === "assistant") {
            attachReplayBtn(body, m.content || "");
          }
        }
      });
      scrollToBottom(true);
    }
  } catch (e) {
    console.warn("历史加载失败：", e);
  }
}

/* ============================================================
   对话（实时思考流 + 增量回复）
   ============================================================ */
async function sendMessage() {
  const text = els.inputBox.value.trim();
  if (!text || state.sending) return;

  stopCurrentTts(); // 新对话：立即打断上一条朗读，避免重叠

  state.sending = true;
  els.sendBtn.disabled = true;
  els.sendBtn.classList.add("sending");
  els.inputBox.value = "";
  autoGrow();

  appendMessage("user", text);
  const liveUI = createLivePanel();
  startLivePolling(liveUI);

  try {
    const r = await postJSON("/api/chat", { message: text });
    stopLivePolling();
    liveUI.panel.remove();

    // 定格：完整回复 + 思考折叠（无实时数据时打字机兜底）
    const { bubble, body } = appendMessage("ember", "");
    const hadLive = liveUI.shownOutputLen >= 0;
    if (hadLive) {
      bubble.textContent = r.reply || "（我没有想好怎么回复…可以再说一次吗？）";
    } else {
      await typewrite(
        bubble,
        r.reply || "（我没有想好怎么回复…可以再说一次吗？）",
      );
    }
    appendThinkBlock(body, r.think);

    // 每条 Ember 回复下附加重播按钮（对整条文本重新分片朗读）
    const finalReply = r.reply || "";
    const replayBtn = attachReplayBtn(body, finalReply);

    // 回复定格后自动朗读（受开关控制）
    playReplyTts(finalReply, replayBtn);

    scrollToBottom();

    // 对话可能触发画像/日记更新，异步刷新侧栏
    loadProfile().catch(() => {});
    loadDiaryCard().catch(() => {});
  } catch (e) {
    stopLivePolling();
    liveUI.panel.remove();
    const { bubble } = appendMessage("ember", "");
    bubble.classList.add("bubble-error");
    bubble.textContent = `💬 对话出了点小问题：${e.message}\n稍等片刻再试试吧～`;
  } finally {
    state.sending = false;
    els.sendBtn.disabled = false;
    els.sendBtn.classList.remove("sending");
    els.inputBox.focus();
  }
}

/* ============================================================
   手动保存
   ============================================================ */
async function manualSave() {
  els.saveBtn.disabled = true;
  try {
    await postJSON("/api/save");
    toast("全部记忆已保存 💾");
  } catch (e) {
    toast(`保存失败：${e.message}`, true);
  } finally {
    els.saveBtn.disabled = false;
  }
}

/* ============================================================
   远程关闭（Flute 内置 OPTIONS /ctrl/kill + X-Shutdown-Token）
   ============================================================ */
async function remoteShutdown() {
  const token = els.shutdownTokenInput.value.trim();
  if (!token) {
    toast("请输入关闭令牌", true);
    return;
  }

  els.shutdownConfirmBtn.disabled = true;
  try {
    const resp = await fetch("/ctrl/kill", {
      method: "OPTIONS",
      headers: { "X-Shutdown-Token": token },
    });
    const text = (await resp.text()).trim();

    if (text.includes("OK")) {
      closeShutdownModal();
      state.polling = false;
      setOnline(false);
      els.farewellScreen.classList.remove("hidden");
    } else if (text.includes("disabled")) {
      toast("服务端未启用远程关闭", true);
    } else if (text.includes("Invalid")) {
      toast("令牌不正确，请重新输入", true);
    } else {
      toast(`关闭失败：${text || "未知响应"}`, true);
    }
  } catch {
    closeShutdownModal();
    state.polling = false;
    setOnline(false);
    els.farewellScreen.classList.remove("hidden");
  } finally {
    els.shutdownConfirmBtn.disabled = false;
    els.shutdownTokenInput.value = "";
  }
}

/* ============================================================
   弹层控制
   ============================================================ */
async function openPersonaModal() {
  try {
    const p = await getJSON("/api/persona");
    els.personaInput.value = p.description || "";
  } catch {
    /* 保留输入框当前内容 */
  }
  els.personaModal.classList.remove("hidden");
  els.personaInput.focus();
}

function closePersonaModal() {
  els.personaModal.classList.add("hidden");
}

function openShutdownModal() {
  els.shutdownTokenInput.value = "";
  els.shutdownModal.classList.remove("hidden");
  els.shutdownTokenInput.focus();
}

function closeShutdownModal() {
  els.shutdownModal.classList.add("hidden");
}

/* ============================================================
   输入框自动增高
   ============================================================ */
function autoGrow() {
  const box = els.inputBox;
  box.style.height = "auto";
  box.style.height = Math.min(box.scrollHeight, 150) + "px";
}

/* ============================================================
   事件绑定
   ============================================================ */
function bindEvents() {
  // 密码锁：解锁按钮与回车提交
  els.lockSubmit.addEventListener("click", submitUnlock);
  els.lockInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      e.preventDefault();
      submitUnlock();
    }
  });
  els.lockInput.addEventListener("input", () => {
    if (els.lockError.classList.contains("show")) {
      els.lockError.classList.remove("show");
      els.lockInput.classList.remove("shake");
    }
  });

  // 发送
  els.sendBtn.addEventListener("click", sendMessage);
  els.inputBox.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  });
  els.inputBox.addEventListener("input", autoGrow);

  // 滚动跟随：用户上滚时暂停自动跟随，接近底部时恢复
  els.messages.addEventListener("scroll", () => {
    const nearBottom =
      els.messages.scrollHeight -
        els.messages.scrollTop -
        els.messages.clientHeight <
      80;
    state.autoScroll = nearBottom;
  });

  // 主题
  els.themeBtn.addEventListener("click", () =>
    els.themeModal.classList.remove("hidden"),
  );
  els.themeCloseBtn.addEventListener("click", () =>
    els.themeModal.classList.add("hidden"),
  );
  els.themeGrid.addEventListener("click", (e) => {
    const cell = e.target.closest(".theme-cell");
    if (cell) {
      applyTheme(cell.dataset.theme);
      toast(`已切换到${THEME_NAMES[cell.dataset.theme]}主题 🎨`);
    }
  });

  // 头像
  els.personaAvatarBtn.addEventListener("click", () =>
    els.avatarModal.classList.remove("hidden"),
  );
  els.avatarCloseBtn.addEventListener("click", () =>
    els.avatarModal.classList.add("hidden"),
  );

  // 人设
  els.editPersonaBtn.addEventListener("click", openPersonaModal);
  els.personaSaveBtn.addEventListener("click", savePersona);
  els.personaCancelBtn.addEventListener("click", closePersonaModal);
  els.personaInput.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closePersonaModal();
  });
  els.resetPersonaBtn.addEventListener("click", resetPersona);
  els.personaDesc.addEventListener("click", () => {
    state.personaDescExpanded = !state.personaDescExpanded;
    els.personaDesc.classList.toggle("expanded", state.personaDescExpanded);
  });

  // 画像
  els.refreshProfileBtn.addEventListener("click", refreshProfile);

  // 日记
  els.openDiaryBtn.addEventListener("click", openDiaryModal);
  els.writeDiaryBtn.addEventListener("click", () => generateDiary(false));
  els.diaryCloseBtn.addEventListener("click", () =>
    els.diaryModal.classList.add("hidden"),
  );
  els.diaryRegenBtn.addEventListener("click", () => generateDiary(true));

  // 保存 / 关闭
  els.saveBtn.addEventListener("click", manualSave);
  els.shutdownBtn.addEventListener("click", openShutdownModal);
  els.shutdownConfirmBtn.addEventListener("click", remoteShutdown);
  els.shutdownCancelBtn.addEventListener("click", closeShutdownModal);
  els.shutdownTokenInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter") remoteShutdown();
    if (e.key === "Escape") closeShutdownModal();
  });

  // 窄屏侧栏抽屉
  els.hamburgerBtn.addEventListener("click", () => {
    els.sidebar.classList.add("open");
    els.sidebarMask.classList.add("show");
  });
  els.sidebarMask.addEventListener("click", () => {
    els.sidebar.classList.remove("open");
    els.sidebarMask.classList.remove("show");
  });

  // 弹层遮罩点击关闭
  const maskClose = [
    [els.themeModal, () => els.themeModal.classList.add("hidden")],
    [els.avatarModal, () => els.avatarModal.classList.add("hidden")],
    [els.personaModal, closePersonaModal],
    [els.diaryModal, () => els.diaryModal.classList.add("hidden")],
    [els.shutdownModal, closeShutdownModal],
  ];
  maskClose.forEach(([modal, fn]) => {
    modal.addEventListener("click", (e) => {
      if (e.target === modal) fn();
    });
  });
}

/* ============================================================
   启动
   ============================================================ */
async function init() {
  initTheme();
  initTts();
  ensureWelcomeAvatarStructure();
  bindEvents();

  // 密码锁前置检查：未启用则跳过；启用则需先解锁再加载数据。
  // 必须在任何 /api/* 请求之前完成，避免未携带令牌导致 401。
  await ensureUnlocked();

  await loadAvatars();
  await Promise.allSettled([
    loadPersona(),
    loadProfile(),
    loadHistory(),
    loadDiaryCard(),
  ]);
  els.inputBox.focus();
  pollStatus(); // 后台循环，不 await
}

document.addEventListener("DOMContentLoaded", init);
