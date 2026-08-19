/* ============================================================
   Ember · 情感陪伴伙伴  —  前端交互逻辑
   依赖后端 REST API（{code, info} 信封格式，code=0 成功）
   ============================================================ */
"use strict";

/* ---------------- DOM 引用 ---------------- */
const $ = (id) => document.getElementById(id);

const els = {
  connStatus: $("connStatus"),
  connDot: $("connDot"),
  connText: $("connText"),
  modelChip: $("modelChip"),
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
  profileCard: $("profileCard"),
  profileBody: $("profileBody"),
  profileUpdatedBadge: $("profileUpdatedBadge"),
  refreshProfileBtn: $("refreshProfileBtn"),
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
  inputBox: $("inputBox"),
  sendBtn: $("sendBtn"),
  // 弹层
  personaModal: $("personaModal"),
  personaInput: $("personaInput"),
  personaSaveBtn: $("personaSaveBtn"),
  personaCancelBtn: $("personaCancelBtn"),
  shutdownModal: $("shutdownModal"),
  shutdownTokenInput: $("shutdownTokenInput"),
  shutdownConfirmBtn: $("shutdownConfirmBtn"),
  shutdownCancelBtn: $("shutdownCancelBtn"),
  farewellScreen: $("farewellScreen"),
  toast: $("toast"),
};

/* ---------------- 全局状态 ---------------- */
const state = {
  sending: false, // 对话请求进行中
  polling: true, // 状态轮询开关（服务关闭后停止）
  autoScroll: true, // 消息区是否跟随最新消息
  remoteShutdownEnabled: false,
  personaDescExpanded: false,
};

/* ============================================================
   API 客户端
   ============================================================ */
async function api(path, options = {}) {
  const resp = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
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

  const avatar = document.createElement("div");
  avatar.className = "msg-avatar";
  avatar.textContent = kind === "user" ? "🧑" : "🔥";

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

/** 添加思考过程折叠区 */
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

/** 思考中动画 */
function appendTyping() {
  hideWelcome();
  const t = document.createElement("div");
  t.className = "typing";
  t.innerHTML =
    '<div class="typing-avatar">🔥</div><div class="typing-bubble"><i></i><i></i><i></i></div>';
  els.messages.appendChild(t);
  scrollToBottom(true);
  return t;
}

/** 打字机逐字呈现（长文本自动加速，总时长上限约 6 秒） */
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

    // 每步字符数：保证总时长 ≤ 6s（基础 22ms/字符）
    const perStep = Math.max(1, Math.ceil(len / (6000 / 22)));
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

  // 远程关闭按钮可见性
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
    els.profileBody.innerHTML = `<p class="profile-empty">画像加载失败：${e.message}</p>`;
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

function escapeHtml(s) {
  return String(s).replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
}

async function refreshProfile() {
  els.refreshProfileBtn.disabled = true;
  els.refreshProfileBtn.textContent = "✨ 总结中…";
  try {
    const r = await postJSON("/api/profile/refresh");
    if (r.updated) {
      await loadProfile();
      els.profileCard.classList.remove("bounce");
      void els.profileCard.offsetWidth; // 重启动画
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
   历史加载
   ============================================================ */
async function loadHistory() {
  try {
    const h = await getJSON("/api/history?limit=50");
    if (h.messages && h.messages.length) {
      h.messages.forEach((m) => {
        if (m.role === "user" || m.role === "assistant") {
          appendMessage(m.role === "user" ? "user" : "ember", m.content, {
            animate: false,
          });
        }
      });
      scrollToBottom(true);
    }
  } catch (e) {
    console.warn("历史加载失败：", e);
  }
}

/* ============================================================
   对话
   ============================================================ */
async function sendMessage() {
  const text = els.inputBox.value.trim();
  if (!text || state.sending) return;

  state.sending = true;
  els.sendBtn.disabled = true;
  els.sendBtn.classList.add("sending");
  els.inputBox.value = "";
  autoGrow();

  appendMessage("user", text);
  const typing = appendTyping();

  try {
    const r = await postJSON("/api/chat", { message: text });
    typing.remove();
    const { bubble, body } = appendMessage("ember", "");
    await typewrite(
      bubble,
      r.reply || "（我没有想好怎么回复…可以再说一次吗？）",
    );
    appendThinkBlock(body, r.think);
    scrollToBottom();
  } catch (e) {
    typing.remove();
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
      // 服务器已开始优雅关闭：停止轮询，显示告别界面
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
  } catch (e) {
    // 请求本身失败也可能意味着服务已关闭
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
  els.personaModal.addEventListener("click", (e) => {
    if (e.target === els.personaModal) closePersonaModal();
  });
  els.shutdownModal.addEventListener("click", (e) => {
    if (e.target === els.shutdownModal) closeShutdownModal();
  });
}

/* ============================================================
   启动
   ============================================================ */
async function init() {
  bindEvents();
  await Promise.allSettled([loadPersona(), loadProfile(), loadHistory()]);
  els.inputBox.focus();
  pollStatus(); // 后台循环，不 await
}

document.addEventListener("DOMContentLoaded", init);
